using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace CoilInspectionApp
{
    // 데이터 모델 클래스들
    public class InferJson
    {
        public int schema_version { get; set; } = 1;
        public string image_id { get; set; }
        public ImageSize image_size { get; set; }
        public YoloInfo yolo { get; set; } = new YoloInfo();
        public AnomaInfo anoma { get; set; } = new AnomaInfo();
        public FinalInfo final { get; set; } = new FinalInfo();
    }

    public class ImageSize { public int w { get; set; } public int h { get; set; } }
    public class YoloInfo
    {
        public bool executed { get; set; }
        public string skipped_reason { get; set; }
        public List<Detection> detections { get; set; } = new List<Detection>();
    }
    public class Detection
    {
        public string class_name { get; set; }
        public float conf { get; set; }
        public float[] bbox_xywh_norm { get; set; }
    }
    public class AnomaInfo
    {
        public bool executed { get; set; }
        public float score { get; set; }
        public string decision { get; set; }
    }
    public class FinalInfo { public bool is_defect { get; set; } public List<string> reason { get; set; } = new List<string>(); }

    public class ManifestJson
    {
        public int schema_version { get; set; } = 2;
        public string batch_type { get; set; } = "inference";
        public string batch_id { get; set; }
        public string created_at { get; set; }
        public List<ManifestItem> items { get; set; } = new List<ManifestItem>();
    }

    public class ManifestItem
    {
        public string id { get; set; }
        public string processed_image { get; set; }
        public string raw_image { get; set; }
        public string infer_json { get; set; }
    }

    public class BatchExporter
    {
        private readonly string _baseOutputDir;
        private readonly string _workingDir;
        private string _currentBatchDir;
        private ManifestJson _currentManifest;

        public string CurrentBatchDirectory => _currentBatchDir;
        public string ExportBaseDirectory => _baseOutputDir;
        public string LastExportDirectory { get; private set; }

        public sealed class PreparedImagePaths
        {
            public string RawImagePath { get; set; }
            public string ProcessedImagePath { get; set; }
        }

        public BatchExporter(string baseOutputDir)
        {
            _baseOutputDir = baseOutputDir;
            if (!Directory.Exists(_baseOutputDir)) Directory.CreateDirectory(_baseOutputDir);
            _workingDir = Path.Combine(_baseOutputDir, "_working", "current_session");
        }

        public void StartNewBatch()
        {
            if (Directory.Exists(_workingDir))
                Directory.Delete(_workingDir, true);

            _currentBatchDir = _workingDir;
            EnsureBatchDirectories();

            _currentManifest = new ManifestJson
            {
                batch_id = "current_session",
                created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
        }

        public void StartOrResumeBatch()
        {
            _currentBatchDir = _workingDir;
            EnsureBatchDirectories();
            _currentManifest = LoadManifestOrCreate("current_session");
        }

        public void AddResult(
            string imageId,
            Mat rawImg,
            Mat processedImg,
            bool anomaExecuted,
            float anomaScore,
            string anomaDecision,
            bool yoloExecuted,
            string yoloSkippedReason,
            List<Detection> detections,
            bool isDefect,
            List<string> reasons)
        {
            if (_currentManifest == null) StartNewBatch();
            EnsureBatchDirectories();

            string relProcessedPath = $"images/{imageId}_masked.bmp";
            string relRawPath = $"raw/{imageId}.bmp";
            string relInferPath = $"inference/{imageId}.infer.json";

            if (rawImg != null && !rawImg.Empty())
                Cv2.ImWrite(Path.Combine(_currentBatchDir, relRawPath), rawImg);

            if (processedImg != null && !processedImg.Empty())
                Cv2.ImWrite(Path.Combine(_currentBatchDir, relProcessedPath), processedImg);

            var inferData = new InferJson
            {
                image_id = imageId,
                image_size = new ImageSize { w = processedImg.Width, h = processedImg.Height },
                yolo = new YoloInfo
                {
                    executed = yoloExecuted,
                    skipped_reason = yoloSkippedReason,
                    detections = detections ?? new List<Detection>()
                },
                anoma = new AnomaInfo
                {
                    executed = anomaExecuted,
                    score = anomaScore,
                    decision = anomaDecision ?? ""
                },
                final = new FinalInfo { is_defect = isDefect, reason = reasons }
            };

            string inferFullPath = Path.Combine(_currentBatchDir, relInferPath);
            // Formatting 오류 해결을 위해 Newtonsoft.Json.Formatting 명시
            File.WriteAllText(inferFullPath, JsonConvert.SerializeObject(inferData, Newtonsoft.Json.Formatting.Indented));

            _currentManifest.items.RemoveAll(item => string.Equals(item.id, imageId, StringComparison.OrdinalIgnoreCase));
            _currentManifest.items.Add(new ManifestItem
            {
                id = imageId,
                processed_image = relProcessedPath,
                raw_image = relRawPath,
                infer_json = relInferPath
            });
            SaveManifest();
        }

        public PreparedImagePaths SavePreparedImages(string imageId, Mat rawImg, Mat processedImg)
        {
            if (_currentManifest == null) StartNewBatch();
            EnsureBatchDirectories();

            string relProcessedPath = $"images/{imageId}_masked.bmp";
            string relRawPath = $"raw/{imageId}.bmp";

            string rawFullPath = Path.Combine(_currentBatchDir, relRawPath);
            string processedFullPath = Path.Combine(_currentBatchDir, relProcessedPath);

            if (rawImg != null && !rawImg.Empty())
                Cv2.ImWrite(rawFullPath, rawImg);

            if (processedImg != null && !processedImg.Empty())
                Cv2.ImWrite(processedFullPath, processedImg);

            return new PreparedImagePaths
            {
                RawImagePath = rawFullPath,
                ProcessedImagePath = processedFullPath,
            };
        }

        public void CloseBatch()
        {
            if (_currentManifest == null || _currentManifest.items.Count == 0) return;
            EnsureBatchDirectories();

            string batchId = BuildExportBatchId();
            string exportDir = Path.Combine(_baseOutputDir, batchId);
            CreateExportDirectories(exportDir);

            _currentManifest.batch_id = batchId;
            _currentManifest.created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

            foreach (ManifestItem item in _currentManifest.items)
            {
                CopyIfExists(Path.Combine(_currentBatchDir, item.raw_image), Path.Combine(exportDir, item.raw_image));
                CopyIfExists(Path.Combine(_currentBatchDir, item.processed_image), Path.Combine(exportDir, item.processed_image));
                CopyIfExists(Path.Combine(_currentBatchDir, item.infer_json), Path.Combine(exportDir, item.infer_json));
            }

            string manifestPath = Path.Combine(exportDir, "meta", "manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(_currentManifest, Newtonsoft.Json.Formatting.Indented));

            string flagPath = Path.Combine(exportDir, "meta", "DONE.flag");
            File.WriteAllText(flagPath, "READY_FOR_TRAINING_UI");

            LastExportDirectory = exportDir;
            _currentManifest = null;
        }

        private void SaveManifest()
        {
            if (_currentManifest == null)
                return;

            EnsureBatchDirectories();
            string manifestPath = Path.Combine(_currentBatchDir, "meta", "manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(_currentManifest, Newtonsoft.Json.Formatting.Indented));
        }

        private void EnsureBatchDirectories()
        {
            if (string.IsNullOrWhiteSpace(_currentBatchDir))
                throw new InvalidOperationException("Current batch directory is not initialized.");

            Directory.CreateDirectory(_currentBatchDir);
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "images"));
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "raw"));
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "inference"));
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "meta"));
        }

        private static void CreateExportDirectories(string exportDir)
        {
            Directory.CreateDirectory(exportDir);
            Directory.CreateDirectory(Path.Combine(exportDir, "images"));
            Directory.CreateDirectory(Path.Combine(exportDir, "raw"));
            Directory.CreateDirectory(Path.Combine(exportDir, "inference"));
            Directory.CreateDirectory(Path.Combine(exportDir, "meta"));
        }

        private string BuildExportBatchId()
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string batchId = $"export_batch_{timeStamp}";
            int suffix = 1;
            while (Directory.Exists(Path.Combine(_baseOutputDir, batchId)))
            {
                batchId = $"export_batch_{timeStamp}_{suffix:000}";
                suffix++;
            }
            return batchId;
        }

        private static void CopyIfExists(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
                return;

            string destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDir))
                Directory.CreateDirectory(destinationDir);

            File.Copy(sourcePath, destinationPath, true);
        }

        private ManifestJson LoadManifestOrCreate(string batchId)
        {
            string manifestPath = Path.Combine(_currentBatchDir, "meta", "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    ManifestJson manifest = JsonConvert.DeserializeObject<ManifestJson>(File.ReadAllText(manifestPath));
                    if (manifest != null)
                        return manifest;
                }
                catch
                {
                    // 손상된 manifest는 현재 세션 복원보다 우선하지 않는다.
                }
            }

            return new ManifestJson
            {
                batch_id = batchId,
                created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
        }
    }
}
