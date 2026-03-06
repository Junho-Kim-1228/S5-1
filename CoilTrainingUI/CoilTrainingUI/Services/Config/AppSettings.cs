using System;
using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services
{
    public class AppSettings
    {
        public string PythonExe { get; set; } = "";
        public ScriptsSection Scripts { get; set; } = new();
        public WorkspaceSection Workspace { get; set; } = new();
        public FusionSection Fusion { get; set; } = new();
        public YoloInferSection YoloInfer { get; set; } = new();
        public AnomaInferSection AnomaInfer { get; set; } = new();

        public class ScriptsSection
        {
            public string YoloTrain { get; set; } = @"scripts\train_yolo.py";
            public string AnomaTrain { get; set; } = @"scripts\train_anoma.py";
        }

        public class WorkspaceSection
        {
            public double TrainRatio { get; set; } = 0.8;
            public double ValRatio { get; set; } = 0.2;
            public int Seed { get; set; } = 42;
        }

        public class FusionSection
        {
            public string Rule { get; set; } = "AND";
            public double YoloThreshold { get; set; } = 0.25;
            public double AnomaThreshold { get; set; } = 0.5;
        }
    }

    public static class AppSettingsLoader
    {
        public static AppSettings LoadOrThrow(string projectRoot)
        {
            string basePath = Path.Combine(projectRoot, "config", "appsettings.json");
            if (!File.Exists(basePath))
                throw new FileNotFoundException($"Missing appsettings.json: {basePath}");

            var baseSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(basePath))
                              ?? new AppSettings();

            // local 덮어쓰기
            string localPath = Path.Combine(projectRoot, "config", "appsettings.local.json");
            if (File.Exists(localPath))
            {
                var local = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(localPath));
                if (local != null)
                {
                    // 필요한 필드만 덮어쓰기 (특히 PythonExe)
                    if (!string.IsNullOrWhiteSpace(local.PythonExe))
                        baseSettings.PythonExe = local.PythonExe;
                }
            }

            if (string.IsNullOrWhiteSpace(baseSettings.PythonExe))
                throw new InvalidOperationException("PythonExe is empty. Set it in config/appsettings.local.json");

            return baseSettings;
        }

    }
    public class YoloInferSection
    {
        public int ImgSz { get; set; } = 1024;
        public bool Letterbox { get; set; } = true;
        public double ConfThres { get; set; } = 0.25;
        public double IouThres { get; set; } = 0.45;
        public int MaxDet { get; set; } = 300;
    }

    public class AnomaInferSection
    {
        public string Mode { get; set; } = "crop"; // "crop" 고정 권장
        public int InputSize { get; set; } = 256;
        public double ScoreThres { get; set; } = 0.5;
        public int CropPaddingPx { get; set; } = 8;
    }

}
