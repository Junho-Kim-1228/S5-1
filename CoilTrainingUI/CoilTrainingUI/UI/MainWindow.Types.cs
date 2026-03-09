using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private sealed class CreatedBatchInfo
        {
            public string BatchPath { get; set; } = "";
            public int ItemCount { get; set; }
        }

        private sealed class SeedManifestItem
        {
            public string Id { get; set; } = "";
            public string ProcessedImage { get; set; } = "";
            public string? RawImage { get; set; }
        }

        private sealed class SeedManifestItemJson
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("processed_image")]
            public string ProcessedImage { get; set; } = "";

            [JsonPropertyName("raw_image")]
            public string? RawImage { get; set; }
        }

        private sealed class UiPreferencesDto
        {
            [JsonPropertyName("last_processed_folder")]
            public string LastProcessedFolder { get; set; } = "";

            [JsonPropertyName("last_raw_folder")]
            public string LastRawFolder { get; set; } = "";

            [JsonPropertyName("last_import_batch_folder")]
            public string LastImportBatchFolder { get; set; } = "";
        }

        private string FindProjectRoot(string targetFolderName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (dir != null)
            {
                if (dir.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private string GetTrainingInboxRoot()
        {
            string projectRoot = FindProjectRoot("capstone_design");
            string configuredRoot = ResolveBatchLibraryRootFromConfig(projectRoot);
            string inboxRoot = string.IsNullOrWhiteSpace(configuredRoot)
                ? IOPath.Combine(projectRoot, "training_inbox")
                : configuredRoot;

            Directory.CreateDirectory(inboxRoot);
            return inboxRoot;
        }

        private static string ResolveBatchLibraryRootFromConfig(string projectRoot)
        {
            string configDir = IOPath.Combine(projectRoot, "config");
            string localConfigPath = IOPath.Combine(configDir, "appsettings.local.json");
            string baseConfigPath = IOPath.Combine(configDir, "appsettings.json");

            string? configuredPath =
                TryReadBatchLibraryRootFromConfig(localConfigPath) ??
                TryReadBatchLibraryRootFromConfig(baseConfigPath);

            if (string.IsNullOrWhiteSpace(configuredPath))
                return "";

            string trimmed = configuredPath.Trim();
            string resolved = IOPath.IsPathRooted(trimmed)
                ? trimmed
                : IOPath.Combine(projectRoot, trimmed);

            return IOPath.GetFullPath(resolved);
        }

        private static string? TryReadBatchLibraryRootFromConfig(string configPath)
        {
            if (!File.Exists(configPath))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                if (root.TryGetProperty("BatchLibraryRoot", out var directValue) &&
                    directValue.ValueKind == JsonValueKind.String)
                {
                    return directValue.GetString();
                }

                if (root.TryGetProperty("Paths", out var pathsSection) &&
                    pathsSection.ValueKind == JsonValueKind.Object &&
                    pathsSection.TryGetProperty("BatchLibraryRoot", out var nestedValue) &&
                    nestedValue.ValueKind == JsonValueKind.String)
                {
                    return nestedValue.GetString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private UiPreferencesDto LoadUiPreferences()
        {
            string path = GetUiPreferencesPath();
            if (!File.Exists(path))
                return new UiPreferencesDto();

            try
            {
                var prefs = JsonSerializer.Deserialize<UiPreferencesDto>(File.ReadAllText(path));
                return prefs ?? new UiPreferencesDto();
            }
            catch
            {
                // 설정 파일이 깨져도 앱 기능은 유지
                return new UiPreferencesDto();
            }
        }

        private void SaveUiPreferences(Action<UiPreferencesDto> update)
        {
            var prefs = LoadUiPreferences();
            update(prefs);

            string path = GetUiPreferencesPath();
            string json = JsonSerializer.Serialize(
                prefs,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private string GetUiPreferencesPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = IOPath.Combine(appData, "CoilTrainingUI");
            Directory.CreateDirectory(appDir);
            return IOPath.Combine(appDir, "ui_preferences.json");
        }

        private static string PickFirstExistingFolder(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private string GetInitialImportBatchFolder(string inboxRoot, string projectRoot)
        {
            var prefs = LoadUiPreferences();
            return PickFirstExistingFolder(
                prefs.LastImportBatchFolder,
                inboxRoot,
                projectRoot
            );
        }

        private string GetInitialProcessedFolder(string inboxRoot, string projectRoot)
        {
            var prefs = LoadUiPreferences();
            return PickFirstExistingFolder(
                prefs.LastProcessedFolder,
                inboxRoot,
                projectRoot
            );
        }

        private string GetInitialRawFolder(string selectedProcessedFolder, string inboxRoot, string projectRoot)
        {
            var prefs = LoadUiPreferences();
            return PickFirstExistingFolder(
                prefs.LastRawFolder,
                selectedProcessedFolder,
                IOPath.GetDirectoryName(selectedProcessedFolder),
                inboxRoot,
                projectRoot
            );
        }

        private void RememberImportBatchFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            SaveUiPreferences(prefs => prefs.LastImportBatchFolder = folderPath);
        }

        private void RememberProcessedFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            SaveUiPreferences(prefs => prefs.LastProcessedFolder = folderPath);
        }

        private void RememberRawFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            SaveUiPreferences(prefs => prefs.LastRawFolder = folderPath);
        }
    }
}
