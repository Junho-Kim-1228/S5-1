using Newtonsoft.Json;
using System;
using System.IO;

namespace CoilInspectionApp.Configuration
{
    public sealed class RuntimePathSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string InputDirectory { get; set; } = "";
        public string InferencePackageDirectory { get; set; } = "";
        public string ExportBaseDirectory { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    public sealed class RuntimePathSettingsStore
    {
        private readonly string _settingsPath;

        public RuntimePathSettingsStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CoilInspectionApp",
                "runtime_paths.json"))
        {
        }

        internal RuntimePathSettingsStore(string settingsPath)
        {
            _settingsPath = settingsPath;
        }

        public string SettingsPath => _settingsPath;

        public RuntimePathSettings Load()
        {
            if (!File.Exists(_settingsPath))
                return new RuntimePathSettings();

            try
            {
                RuntimePathSettings settings = JsonConvert.DeserializeObject<RuntimePathSettings>(
                    File.ReadAllText(_settingsPath));
                return settings ?? new RuntimePathSettings();
            }
            catch
            {
                // 손상된 사용자 설정은 원본을 건드리지 않고 기본 설정으로 시작한다.
                return new RuntimePathSettings();
            }
        }

        public void Save(RuntimePathSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            string directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            settings.SchemaVersion = 1;
            settings.UpdatedAt = DateTime.Now.ToString("O");
            File.WriteAllText(
                _settingsPath,
                JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
    }
}
