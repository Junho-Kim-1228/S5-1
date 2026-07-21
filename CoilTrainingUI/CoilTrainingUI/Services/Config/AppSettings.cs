using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services
{
    public class AppSettings
    {
        public string PythonExe { get; set; } = "";
        public string BatchLibraryRoot { get; set; } = "";
        public string AiProjectRoot { get; set; } = "coil-ai-runtime";
        public WorkspaceSection Workspace { get; set; } = new();
        public FusionSection Fusion { get; set; } = new();
        public YoloInferSection YoloInfer { get; set; } = new();
        public AnomaInferSection AnomaInfer { get; set; } = new();

        public class WorkspaceSection
        {
            public double TrainRatio { get; set; } = 0.8;
            public double ValRatio { get; set; } = 0.2;
            public int Seed { get; set; } = 42;
            public int YoloMaxBackground { get; set; } = 250;
            public int YoloEpochs { get; set; } = 150;
            public int YoloBatch { get; set; } = 4;
            public string YoloOversampleClass { get; set; } = "";
            public double YoloOversampleFactor { get; set; } = 1.0;
            public string YoloAugmentClass { get; set; } = "all";
            public double YoloAugmentFactor { get; set; } = 2.0;
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

                    if (!string.IsNullOrWhiteSpace(local.BatchLibraryRoot))
                        baseSettings.BatchLibraryRoot = local.BatchLibraryRoot;

                    if (!string.IsNullOrWhiteSpace(local.AiProjectRoot))
                        baseSettings.AiProjectRoot = local.AiProjectRoot;
                }
            }

            string? resolvedPythonExe = ResolvePythonExePath(baseSettings.PythonExe, settingsRoot, projectRoot, appBaseDir);
            if (string.IsNullOrWhiteSpace(resolvedPythonExe))
            {
                throw new InvalidOperationException(
                    "Python 실행 파일을 찾을 수 없습니다. " +
                    "config/appsettings.local.json의 PythonExe를 설정하거나, " +
                    "앱 폴더 아래 python_env 폴더를 함께 배포하세요.");
            }

            baseSettings.PythonExe = resolvedPythonExe;
            baseSettings.AiProjectRoot = ResolveAiProjectRootOrThrow(baseSettings.AiProjectRoot, settingsRoot, projectRoot, appBaseDir);

            return baseSettings;
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

            throw new InvalidOperationException(
                "AI 학습 프로젝트 폴더를 찾을 수 없습니다. " +
                "config/appsettings.local.json의 AiProjectRoot를 설정하거나, " +
                "앱 폴더 또는 프로젝트 루트 아래에 coil-ai 폴더를 배치하세요.");
        }

        private static string? ResolvePythonExePath(string configuredPythonExe, string settingsRoot, string projectRoot, string appBaseDir)
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

                return null;
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
        public int ImgSz { get; set; } = 1024;
        public bool Letterbox { get; set; } = true;
        public double ConfThres { get; set; } = 0.25;
        public double IouThres { get; set; } = 0.45;
        public int MaxDet { get; set; } = 300;
    }

    public class AnomaInferSection
    {
        public string Mode { get; set; } = "crop"; // "crop" 고정 권장
        public int InputSize { get; set; } = 640;
        public double ScoreThres { get; set; } = 0.5;
        public int CropPaddingPx { get; set; } = 8;
    }

}
