using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CoilInspectionApp
{
    public static class InferenceContextFactory
    {
        private static readonly object HashCacheLock = new object();
        private static readonly Dictionary<string, CachedFileHash> HashCache =
            new Dictionary<string, CachedFileHash>(StringComparer.OrdinalIgnoreCase);

        public static InferenceContextInfo Create(string packagePath, PipelinePackageConfig config)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("packagePath is empty.", nameof(packagePath));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            string configPath = Path.Combine(packagePath, "config", "pipeline.json");
            string pipelineHash = ComputeFileSha256(configPath);
            string anomaHash = config.RequiresAnoma
                ? ComputeFileSha256(Path.Combine(packagePath, config.anoma.model))
                : "";
            string yoloHash = config.RequiresYolo
                ? ComputeFileSha256(Path.Combine(packagePath, config.yolo.model))
                : "";
            string packageFingerprint = ComputeTextSha256(
                string.Join("|", pipelineHash, anomaHash, yoloHash));

            return new InferenceContextInfo
            {
                status = "recorded",
                context_id = "ctx_" + packageFingerprint.Substring(0, 16),
                captured_at_utc = DateTime.UtcNow.ToString("O"),
                pipeline_mode = config.pipeline.mode ?? "",
                package_fingerprint = packageFingerprint,
                pipeline_sha256 = pipelineHash,
                anoma = config.RequiresAnoma
                    ? new AnomaInferenceContext
                    {
                        model_file = Path.GetFileName(config.anoma.model),
                        model_sha256 = anomaHash,
                        score_threshold = config.anoma.score_thres,
                        input_size = config.anoma.input_size,
                        mode = config.anoma.mode ?? ""
                    }
                    : null,
                yolo = config.RequiresYolo
                    ? new YoloInferenceContext
                    {
                        model_file = Path.GetFileName(config.yolo.model),
                        model_sha256 = yoloHash,
                        confidence_threshold = config.yolo.conf_thres,
                        iou_threshold = config.yolo.iou_thres,
                        input_size = config.yolo.imgsz
                    }
                    : null
            };
        }

        public static string ReadPipelineSnapshot(string packagePath)
        {
            string path = Path.Combine(packagePath, "config", "pipeline.json");
            return File.ReadAllText(path);
        }

        private static string ComputeFileSha256(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Inference package file not found.", path);

            string fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            long length = info.Length;
            long lastWriteTicks = info.LastWriteTimeUtc.Ticks;

            lock (HashCacheLock)
            {
                if (HashCache.TryGetValue(fullPath, out var cached) &&
                    cached.Length == length &&
                    cached.LastWriteUtcTicks == lastWriteTicks)
                {
                    return cached.Sha256;
                }
            }

            string hash;
            using (var stream = File.OpenRead(fullPath))
            using (var sha256 = SHA256.Create())
                hash = ToHex(sha256.ComputeHash(stream));

            info.Refresh();
            if (info.Length == length && info.LastWriteTimeUtc.Ticks == lastWriteTicks)
            {
                lock (HashCacheLock)
                {
                    HashCache[fullPath] = new CachedFileHash
                    {
                        Length = length,
                        LastWriteUtcTicks = lastWriteTicks,
                        Sha256 = hash
                    };
                }
            }

            return hash;
        }

        private static string ComputeTextSha256(string value)
        {
            using (var sha256 = SHA256.Create())
                return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private sealed class CachedFileHash
        {
            public long Length { get; set; }
            public long LastWriteUtcTicks { get; set; }
            public string Sha256 { get; set; } = "";
        }
    }
}
