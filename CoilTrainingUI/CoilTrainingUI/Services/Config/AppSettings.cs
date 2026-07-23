using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services
{
    public class AppSettings
    {
        public string PythonExe { get; set; } = "";
        public string YoloPythonExe { get; set; } = "";
        public string AnomaPythonExe { get; set; } = "";
        public string BatchLibraryRoot { get; set; } = "";
        public string AiProjectRoot { get; set; } = "coil-ai-runtime";
        public WorkspaceSection Workspace { get; set; } = new();
        public YoloTrainingSection YoloTraining { get; set; } = new();
        public AnomaTrainingSection AnomaTraining { get; set; } = new();
        public YoloInferSection YoloInfer { get; set; } = new();
        public AnomaInferSection AnomaInfer { get; set; } = new();
        public MaskInferSection MaskInfer { get; set; } = new();
        public AutoReviewSection AutoReview { get; set; } = new();

        public class WorkspaceSection
        {
            public double TrainRatio { get; set; } = 0.8;
            public double ValRatio { get; set; } = 0.2;
            public int Seed { get; set; } = 42;
            public int? YoloMaxBackground { get; set; }
            public string YoloOversampleClass { get; set; } = "";
            public double YoloOversampleFactor { get; set; } = 1.0;
            public string YoloAugmentClass { get; set; } = "all";
            public double YoloAugmentFactor { get; set; } = 2.0;
        }

        public class YoloTrainingSection
        {
            public string Model { get; set; } = "yolo26n.pt";
            public int Epochs { get; set; } = 100;
            public int FineTuneEpochs { get; set; } = 40;
            public double FineTuneLearningRate { get; set; } = 0.001;
            public int ImageSize { get; set; } = 1280;
            public int Batch { get; set; } = 4;
            public string Device { get; set; } = "auto";
            public int Seed { get; set; } = 42;
        }

        public class AnomaTrainingSection
        {
            public string Model { get; set; } = "dinomaly";
            public int ImageSize { get; set; } = 448;
            public int Batch { get; set; } = 4;
            public string Device { get; set; } = "auto";
            public int Seed { get; set; } = 42;
            public string Encoder { get; set; } = "vit_large_patch14_reg4_dinov2";
            public double Dropout { get; set; } = 0.1;
            public int DecoderDepth { get; set; } = 12;
            public int MaxSteps { get; set; } = 5000;
            public double LearningRate { get; set; } = 0.002;
            public double TargetRecall { get; set; } = 0.90;
        }

    }

    public static class AppSettingsLoader
    {
        public static AppSettings LoadOrThrow(
            string projectRoot,
            bool requireYoloPython = true,
            bool requireAnomaPython = true)
        {
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            string settingsRoot = ResolveSettingsRootOrThrow(projectRoot, appBaseDir);
            string basePath = Path.Combine(settingsRoot, "config", "appsettings.json");
            if (!File.Exists(basePath))
                throw new FileNotFoundException($"Missing appsettings.json: {basePath}");

            var baseSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(basePath))
                              ?? new AppSettings();

            // local 덮어쓰기
            string localPath = Path.Combine(settingsRoot, "config", "appsettings.local.json");
            if (File.Exists(localPath))
            {
                var local = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(localPath));
                if (local != null)
                {
                    // 필요한 필드만 덮어쓰기 (특히 PythonExe)
                    if (!string.IsNullOrWhiteSpace(local.PythonExe))
                        baseSettings.PythonExe = local.PythonExe;

                    if (!string.IsNullOrWhiteSpace(local.YoloPythonExe))
                        baseSettings.YoloPythonExe = local.YoloPythonExe;

                    if (!string.IsNullOrWhiteSpace(local.AnomaPythonExe))
                        baseSettings.AnomaPythonExe = local.AnomaPythonExe;

                    if (!string.IsNullOrWhiteSpace(local.BatchLibraryRoot))
                        baseSettings.BatchLibraryRoot = local.BatchLibraryRoot;

                    if (!string.IsNullOrWhiteSpace(local.AiProjectRoot))
                        baseSettings.AiProjectRoot = local.AiProjectRoot;
                }
            }

            string? resolvedLegacyPython = ResolvePythonExePath(
                baseSettings.PythonExe,
                settingsRoot,
                projectRoot,
                appBaseDir);
            string? resolvedYoloPython = ResolvePythonExePath(
                baseSettings.YoloPythonExe,
                settingsRoot,
                projectRoot,
                appBaseDir,
                new[]
                {
                    @"python_env_yolo\Scripts\python.exe",
                    @"python_env_yolo\python.exe",
                    @"coil-ai\.venv_train\Scripts\python.exe",
                    "coil-ai/.venv_train/bin/python"
                }) ?? resolvedLegacyPython;
            string? resolvedAnomaPython = ResolvePythonExePath(
                baseSettings.AnomaPythonExe,
                settingsRoot,
                projectRoot,
                appBaseDir,
                new[]
                {
                    @"python_env_dinomaly\Scripts\python.exe",
                    @"python_env_dinomaly\python.exe",
                    @"coil-ai\.venv_dinomaly\Scripts\python.exe",
                    "coil-ai/.venv_dinomaly/bin/python"
                }) ?? resolvedLegacyPython;

            baseSettings.YoloPythonExe = resolvedYoloPython ?? "";
            baseSettings.AnomaPythonExe = resolvedAnomaPython ?? "";
            baseSettings.PythonExe = resolvedLegacyPython ?? resolvedYoloPython ?? resolvedAnomaPython ?? "";
            ValidateRequiredPythonEnvironments(baseSettings, requireYoloPython, requireAnomaPython);
            baseSettings.AiProjectRoot = ResolveAiProjectRootOrThrow(baseSettings.AiProjectRoot, settingsRoot, projectRoot, appBaseDir);

            return baseSettings;
        }

        public static void ValidateRequiredPythonEnvironments(
            AppSettings settings,
            bool requireYoloPython,
            bool requireAnomaPython)
        {
            var missingEnvironments = new List<string>();
            if (requireYoloPython && !File.Exists(settings.YoloPythonExe))
                missingEnvironments.Add("YOLO");
            if (requireAnomaPython && !File.Exists(settings.AnomaPythonExe))
                missingEnvironments.Add("Anoma");

            if (missingEnvironments.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{string.Join("/", missingEnvironments)} Python 실행 파일을 찾을 수 없습니다. " +
                    "config/appsettings.local.json의 YoloPythonExe와 AnomaPythonExe를 설정하거나, " +
                    "선택한 학습에 필요한 Python 환경을 앱과 함께 배포하세요.");
            }
        }

        private static string ResolveSettingsRootOrThrow(string projectRoot, string appBaseDir)
        {
            string? foundFromAppBase = FindNearestSettingsRoot(appBaseDir);
            if (!string.IsNullOrWhiteSpace(foundFromAppBase))
                return foundFromAppBase;

            string candidateFromProjectRoot = Path.GetFullPath(projectRoot);
            if (File.Exists(Path.Combine(candidateFromProjectRoot, "config", "appsettings.json")))
                return candidateFromProjectRoot;

            throw new InvalidOperationException(
                "appsettings.json을 찾을 수 없습니다. " +
                "앱 기준 폴더 또는 프로젝트 루트 아래 config/appsettings.json이 필요합니다.");
        }

        private static string? FindNearestSettingsRoot(string startDir)
        {
            if (string.IsNullOrWhiteSpace(startDir))
                return null;

            DirectoryInfo? current = new DirectoryInfo(startDir);
            while (current != null)
            {
                string configPath = Path.Combine(current.FullName, "config", "appsettings.json");
                if (File.Exists(configPath))
                    return current.FullName;

                current = current.Parent;
            }

            return null;
        }

        private static string ResolveAiProjectRootOrThrow(string configuredAiProjectRoot, string settingsRoot, string projectRoot, string appBaseDir)
        {
            string candidateRoot = string.IsNullOrWhiteSpace(configuredAiProjectRoot)
                ? "coil-ai-runtime"
                : configuredAiProjectRoot.Trim();

            if (Path.IsPathRooted(candidateRoot))
            {
                if (Directory.Exists(candidateRoot))
                    return Path.GetFullPath(candidateRoot);

                throw new InvalidOperationException(
                    $"AI 학습 프로젝트 폴더를 찾을 수 없습니다: {candidateRoot}");
            }

            foreach (string baseDir in GetCandidateBaseDirs(settingsRoot, projectRoot, appBaseDir))
            {
                string resolved = Path.GetFullPath(Path.Combine(baseDir, candidateRoot));
                if (Directory.Exists(resolved))
                    return resolved;
            }

            // Source checkout에서는 배포용 coil-ai-runtime 대신 같은 저장소의 coil-ai를 사용한다.
            if (candidateRoot.Equals("coil-ai-runtime", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string baseDir in GetCandidateBaseDirs(settingsRoot, projectRoot, appBaseDir))
                {
                    string sourceCheckout = Path.GetFullPath(Path.Combine(baseDir, "coil-ai"));
                    if (Directory.Exists(sourceCheckout))
                        return sourceCheckout;
                }
            }

            throw new InvalidOperationException(
                "AI 학습 프로젝트 폴더를 찾을 수 없습니다. " +
                "config/appsettings.local.json의 AiProjectRoot를 설정하거나, " +
                "앱 폴더 또는 프로젝트 루트 아래에 coil-ai 폴더를 배치하세요.");
        }

        private static string? ResolvePythonExePath(
            string configuredPythonExe,
            string settingsRoot,
            string projectRoot,
            string appBaseDir,
            IEnumerable<string>? preferredRelativeCandidates = null)
        {
            if (!string.IsNullOrWhiteSpace(configuredPythonExe))
            {
                string trimmed = configuredPythonExe.Trim();
                if (Path.IsPathRooted(trimmed))
                    return File.Exists(trimmed) ? trimmed : null;

                foreach (string baseDir in GetCandidateBaseDirs(settingsRoot, projectRoot, appBaseDir))
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDir, trimmed));
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            if (preferredRelativeCandidates != null)
            {
                foreach (string baseDir in GetCandidateBaseDirs(settingsRoot, projectRoot, appBaseDir))
                {
                    foreach (string relativePath in preferredRelativeCandidates)
                    {
                        string candidate = Path.GetFullPath(Path.Combine(baseDir, relativePath));
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }

            foreach (string candidate in GetBundledPythonCandidates(settingsRoot, projectRoot, appBaseDir))
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetCandidateBaseDirs(string settingsRoot, string projectRoot, string appBaseDir)
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string baseDir in new[]
            {
                settingsRoot,
                Directory.GetParent(settingsRoot)?.FullName ?? "",
                appBaseDir,
                projectRoot
            })
            {
                if (string.IsNullOrWhiteSpace(baseDir))
                    continue;

                string fullPath = Path.GetFullPath(baseDir);
                if (yielded.Add(fullPath))
                    yield return fullPath;
            }
        }

        private static IEnumerable<string> GetBundledPythonCandidates(string settingsRoot, string projectRoot, string appBaseDir)
        {
            string[] relativeCandidates =
            {
                @"python_env\Scripts\python.exe",
                @"python_env\python.exe",
                @"python\python.exe",
                @".venv\Scripts\python.exe",
                @"venv\Scripts\python.exe",
                "python_env/bin/python",
                ".venv/bin/python",
                "venv/bin/python"
            };

            foreach (string baseDir in GetCandidateBaseDirs(settingsRoot, projectRoot, appBaseDir))
            {
                foreach (string relativePath in relativeCandidates)
                    yield return Path.GetFullPath(Path.Combine(baseDir, relativePath));
            }
        }

    }
    public class YoloInferSection
    {
        public int ImgSz { get; set; } = 1280;
        public bool Letterbox { get; set; } = true;
        public double ConfThres { get; set; } = 0.25;
        public double IouThres { get; set; } = 0.45;
        public int MaxDet { get; set; } = 300;
    }

    public class AnomaInferSection
    {
        public string Mode { get; set; } = "crop"; // "crop" 고정 권장
        public int InputSize { get; set; } = 448;
        public double ScoreThres { get; set; } = 0.02454194;
        public int CropPaddingPx { get; set; } = 8;
    }

    public class MaskInferSection
    {
        public string ModelPath { get; set; } = "outputs/mask/coil_unetpp_effb4_scratch_v8/mask.onnx";
        public int InputSize { get; set; } = 512;
        public string ResizeMode { get; set; } = "letterbox";
        public double[] ImageMean { get; set; } = { 0.485, 0.456, 0.406 };
        public double[] ImageStd { get; set; } = { 0.229, 0.224, 0.225 };
        public double ConfidencePercentile { get; set; } = 99.5;
        public double ConfidenceThreshold { get; set; } = 0.5;
        public double MaskThreshold { get; set; } = 0.3;
        public int MinComponentArea { get; set; } = 64;
        public int MorphOpenKernel { get; set; }
        public int MorphCloseKernel { get; set; }
        public int OuterRecoverKernel { get; set; }
        public bool KeepLargestComponent { get; set; } = true;
        public bool PreserveInnerHoles { get; set; } = true;
        public int MinHoleArea { get; set; } = 64;
    }

    public class AutoReviewSection
    {
        public bool Enabled { get; set; } = true;
        public string PolicyVersion { get; set; } = "auto_review_v1";
        public double AnomaNormalThresholdMultiplier { get; set; } = 0.5;
        public double AnomaDefectThresholdMultiplier { get; set; } = 2.0;
        public double YoloBoxMinConfidence { get; set; } = 0.85;
        public double AuditSampleRate { get; set; } = 0.10;
    }

}
