using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoilTrainingUI.Services.Automation;

public sealed class AutomationSettings
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("exchange_root")]
    public string ExchangeRoot { get; set; } = "";

    [JsonPropertyName("auto_import_batches")]
    public bool AutoImportBatches { get; set; } = true;

    [JsonPropertyName("auto_publish_models")]
    public bool AutoPublishModels { get; set; } = true;

    [JsonPropertyName("auto_apply_approved_models")]
    public bool AutoApplyApprovedModels { get; set; } = true;

    [JsonPropertyName("reconcile_interval_seconds")]
    public int ReconcileIntervalSeconds { get; set; } = 10;

    public AutomationSettings Normalize()
    {
        SchemaVersion = 1;
        ExchangeRoot = AutomationPaths.NormalizeExchangeRoot(ExchangeRoot);
        ReconcileIntervalSeconds = Math.Clamp(ReconcileIntervalSeconds, 2, 3600);
        return this;
    }
}

public static class AutomationPaths
{
    public static string DefaultExchangeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoilInspectionAutomation");

    public static string NormalizeExchangeRoot(string? configuredRoot) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? DefaultExchangeRoot
            : Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));

    public static string Outbox(string root) => Path.Combine(NormalizeExchangeRoot(root), "batches", "outbox");
    public static string Archive(string root) => Path.Combine(NormalizeExchangeRoot(root), "batches", "archive");
    public static string Receipts(string root) => Path.Combine(NormalizeExchangeRoot(root), "batches", "receipts");
    public static string Releases(string root) => Path.Combine(NormalizeExchangeRoot(root), "models", "releases");
    public static string Control(string root) => Path.Combine(NormalizeExchangeRoot(root), "models", "control");
    public static string ActivationRequest(string root) => Path.Combine(Control(root), "activation_request.json");
    public static string ActivationResult(string root) => Path.Combine(Control(root), "activation_result.json");
    public static string Locks(string root) => Path.Combine(Control(root), "locks");

    public static void EnsureLayout(string root)
    {
        Directory.CreateDirectory(Outbox(root));
        Directory.CreateDirectory(Archive(root));
        Directory.CreateDirectory(Receipts(root));
        Directory.CreateDirectory(Releases(root));
        Directory.CreateDirectory(Control(root));
        Directory.CreateDirectory(Locks(root));
    }

    public static string ResolveReleasePackagePath(string exchangeRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("릴리스 패키지 경로는 상대 경로여야 합니다.");
        if (relativePath.Replace('\\', '/').Split('/').Any(segment => segment == ".."))
            throw new InvalidDataException("릴리스 패키지 경로에 .. 구간을 사용할 수 없습니다.");

        string releasesRoot = Path.GetFullPath(Releases(exchangeRoot))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(
            releasesRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(releasesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("릴리스 패키지 경로가 releases 루트를 벗어납니다.");
        return candidate;
    }
}

public sealed class TrainingAutomationSettingsStore
{
    private readonly string _settingsPath;

    public TrainingAutomationSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoilTrainingUI",
            "automation_settings.json");
    }

    public string SettingsPath => _settingsPath;

    public AutomationSettings Load(AutomationSection? configured = null)
    {
        AutomationSettings settings = configured == null
            ? new AutomationSettings()
            : new AutomationSettings
            {
                Enabled = configured.Enabled,
                ExchangeRoot = configured.ExchangeRoot,
                AutoImportBatches = configured.AutoImportBatches,
                AutoPublishModels = configured.AutoPublishModels,
                AutoApplyApprovedModels = configured.AutoApplyApprovedModels,
                ReconcileIntervalSeconds = configured.ReconcileIntervalSeconds
            };

        if (File.Exists(_settingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AutomationSettings>(File.ReadAllText(_settingsPath))
                           ?? settings;
            }
            catch (JsonException)
            {
                // Preserve the corrupt file and use configured defaults.
            }
        }
        return settings.Normalize();
    }

    public void Save(AutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        AtomicJsonFile.Write(_settingsPath, settings);
    }

    public string? ReadInspectionExchangeRoot()
    {
        string counterpart = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoilInspectionApp",
            "automation_settings.json");
        if (!File.Exists(counterpart))
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(counterpart));
            return document.RootElement.TryGetProperty("exchange_root", out JsonElement value)
                ? AutomationPaths.NormalizeExchangeRoot(value.GetString())
                : null;
        }
        catch
        {
            return null;
        }
    }
}

public static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static void Write<T>(string path, T value)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

public sealed class InterprocessFileLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;

    private InterprocessFileLock(string path, FileStream stream)
    {
        _path = path;
        _stream = stream;
    }

    public static InterprocessFileLock Acquire(string path, TimeSpan timeout)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                stream.SetLength(0);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.Write($"pid={Environment.ProcessId}; acquired={DateTime.UtcNow:O}");
                writer.Flush();
                stream.Flush(flushToDisk: true);
                return new InterprocessFileLock(fullPath, stream);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        try { File.Delete(_path); } catch { }
    }
}

public static class AutomationHash
{
    public static string FileSha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static string PackageSha256(string packageRoot)
    {
        string root = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
            .ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            byte[] pathBytes = Encoding.UTF8.GetBytes(relative);
            hash.AppendData(BitConverter.GetBytes(pathBytes.Length));
            hash.AppendData(pathBytes);
            long length = new FileInfo(file).Length;
            hash.AppendData(BitConverter.GetBytes(length));
            using FileStream stream = File.OpenRead(file);
            byte[] buffer = new byte[1024 * 128];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

public sealed class ActivationRequest
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
    [JsonPropertyName("model_id")] public string ModelId { get; set; } = "";
    [JsonPropertyName("package_relative_path")] public string PackageRelativePath { get; set; } = "";
    [JsonPropertyName("package_hash")] public string PackageHash { get; set; } = "";
    [JsonPropertyName("requested_at_utc")] public DateTime RequestedAtUtc { get; set; }
}

public sealed class ActivationResult
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
    [JsonPropertyName("model_id")] public string ModelId { get; set; } = "";
    [JsonPropertyName("package_hash")] public string PackageHash { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("applied_at_utc")] public DateTime? AppliedAtUtc { get; set; }
    [JsonPropertyName("failed_at_utc")] public DateTime? FailedAtUtc { get; set; }
    [JsonPropertyName("previous_model_path")] public string PreviousModelPath { get; set; } = "";
    [JsonPropertyName("active_model_path")] public string ActiveModelPath { get; set; } = "";
}

public sealed class ModelReleaseManifest
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("model_id")] public string ModelId { get; set; } = "";
    [JsonPropertyName("package_hash")] public string PackageHash { get; set; } = "";
    [JsonPropertyName("source_run")] public string SourceRun { get; set; } = "";
    [JsonPropertyName("pipeline_mode")] public string PipelineMode { get; set; } = "";
    [JsonPropertyName("created_at_utc")] public DateTime CreatedAtUtc { get; set; }
}
