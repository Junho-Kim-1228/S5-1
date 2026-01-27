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
            public bool UseRoiProcessedImages { get; set; } = true;
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
            // capstone_design/config/appsettings.json 고정
            string path = Path.Combine(projectRoot, "config", "appsettings.json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing appsettings.json: {path}");

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings == null)
                throw new InvalidOperationException("Invalid appsettings.json (deserialize returned null)");

            if (string.IsNullOrWhiteSpace(settings.PythonExe))
                throw new InvalidOperationException("PythonExe is empty in appsettings.json");

            return settings;
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
    }

}
