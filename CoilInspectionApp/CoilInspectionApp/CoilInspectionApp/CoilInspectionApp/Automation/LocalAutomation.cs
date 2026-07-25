using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CoilInspectionApp.Automation
{
    public sealed class AutomationSettings
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("exchange_root")] public string ExchangeRoot { get; set; } = "";
        [JsonProperty("auto_import_batches")] public bool AutoImportBatches { get; set; } = true;
        [JsonProperty("auto_publish_models")] public bool AutoPublishModels { get; set; } = true;
        [JsonProperty("auto_apply_approved_models")] public bool AutoApplyApprovedModels { get; set; } = true;
        [JsonProperty("reconcile_interval_seconds")] public int ReconcileIntervalSeconds { get; set; } = 10;
    }

    public sealed class AutomationSettingsStore
    {
        private readonly string _settingsPath;

        public AutomationSettingsStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CoilInspectionApp",
                "automation_settings.json"))
        {
        }

        internal AutomationSettingsStore(string settingsPath)
        {
            _settingsPath = settingsPath;
        }

        public string SettingsPath { get { return _settingsPath; } }

        public AutomationSettings Load()
        {
            AutomationSettings settings = null;
            if (File.Exists(_settingsPath))
            {
                try { settings = JsonConvert.DeserializeObject<AutomationSettings>(File.ReadAllText(_settingsPath)); }
                catch { }
            }
            settings = settings ?? new AutomationSettings();
            settings.SchemaVersion = 1;
            settings.ExchangeRoot = AutomationPaths.NormalizeExchangeRoot(settings.ExchangeRoot);
            settings.ReconcileIntervalSeconds = Math.Max(2, Math.Min(3600, settings.ReconcileIntervalSeconds));
            return settings;
        }

        public void Save(AutomationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.SchemaVersion = 1;
            settings.ExchangeRoot = AutomationPaths.NormalizeExchangeRoot(settings.ExchangeRoot);
            settings.ReconcileIntervalSeconds = Math.Max(2, Math.Min(3600, settings.ReconcileIntervalSeconds));
            AtomicJson.Write(_settingsPath, settings);
        }

        public string ReadTrainingExchangeRoot()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CoilTrainingUI",
                "automation_settings.json");
            if (!File.Exists(path)) return "";
            try
            {
                AutomationSettings settings = JsonConvert.DeserializeObject<AutomationSettings>(File.ReadAllText(path));
                return settings == null ? "" : AutomationPaths.NormalizeExchangeRoot(settings.ExchangeRoot);
            }
            catch { return ""; }
        }
    }

    public static class AutomationPaths
    {
        public static string DefaultExchangeRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CoilInspectionAutomation");
            }
        }

        public static string NormalizeExchangeRoot(string root)
        {
            string value = string.IsNullOrWhiteSpace(root) ? DefaultExchangeRoot : Environment.ExpandEnvironmentVariables(root.Trim());
            return Path.GetFullPath(value);
        }

        public static string Outbox(string root) { return Path.Combine(NormalizeExchangeRoot(root), "batches", "outbox"); }
        public static string Archive(string root) { return Path.Combine(NormalizeExchangeRoot(root), "batches", "archive"); }
        public static string Receipts(string root) { return Path.Combine(NormalizeExchangeRoot(root), "batches", "receipts"); }
        public static string Releases(string root) { return Path.Combine(NormalizeExchangeRoot(root), "models", "releases"); }
        public static string Control(string root) { return Path.Combine(NormalizeExchangeRoot(root), "models", "control"); }
        public static string Request(string root) { return Path.Combine(Control(root), "activation_request.json"); }
        public static string Result(string root) { return Path.Combine(Control(root), "activation_result.json"); }
        public static string Locks(string root) { return Path.Combine(Control(root), "locks"); }

        public static void EnsureLayout(string root)
        {
            Directory.CreateDirectory(Outbox(root));
            Directory.CreateDirectory(Archive(root));
            Directory.CreateDirectory(Receipts(root));
            Directory.CreateDirectory(Releases(root));
            Directory.CreateDirectory(Control(root));
            Directory.CreateDirectory(Locks(root));
        }

        public static string ResolveReleasePackage(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("package_relative_path must be relative.");
            if (relativePath.Replace('\\', '/').Split('/').Any(segment => segment == ".."))
                throw new InvalidDataException("package_relative_path cannot contain '..'.");
            string releases = Path.GetFullPath(Releases(root)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(releases, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(releases + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("package_relative_path escaped the releases root.");
            return candidate;
        }
    }

    public sealed class ActivationRequest
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("request_id")] public string RequestId { get; set; }
        [JsonProperty("model_id")] public string ModelId { get; set; }
        [JsonProperty("package_relative_path")] public string PackageRelativePath { get; set; }
        [JsonProperty("package_hash")] public string PackageHash { get; set; }
        [JsonProperty("requested_at_utc")] public DateTime RequestedAtUtc { get; set; }
    }

    public sealed class ActivationResult
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("request_id")] public string RequestId { get; set; }
        [JsonProperty("model_id")] public string ModelId { get; set; }
        [JsonProperty("package_hash")] public string PackageHash { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("applied_at_utc")] public DateTime? AppliedAtUtc { get; set; }
        [JsonProperty("failed_at_utc")] public DateTime? FailedAtUtc { get; set; }
        [JsonProperty("previous_model_path")] public string PreviousModelPath { get; set; }
        [JsonProperty("active_model_path")] public string ActiveModelPath { get; set; }
    }

    public sealed class ReleaseManifest
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("model_id")] public string ModelId { get; set; }
        [JsonProperty("package_hash")] public string PackageHash { get; set; }
        [JsonProperty("pipeline_mode")] public string PipelineMode { get; set; }
    }

    public sealed class PackageActivationOutcome
    {
        public string PreviousModelPath { get; set; }
        public string ActiveModelPath { get; set; }
    }

    public sealed class InterprocessFileLock : IDisposable
    {
        private readonly string _path;
        private readonly FileStream _stream;

        private InterprocessFileLock(string path, FileStream stream)
        {
            _path = path;
            _stream = stream;
        }

        public static InterprocessFileLock Acquire(string path, TimeSpan timeout)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (true)
            {
                try
                {
                    FileStream stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    stream.SetLength(0);
                    byte[] text = Encoding.UTF8.GetBytes("pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "; acquired=" + DateTime.UtcNow.ToString("O"));
                    stream.Write(text, 0, text.Length);
                    stream.Flush(true);
                    return new InterprocessFileLock(fullPath, stream);
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow >= deadline) throw;
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

    public static class AtomicJson
    {
        public static void Write(string path, object value)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(fullPath))
                    File.Replace(temporary, fullPath, null);
                else
                    File.Move(temporary, fullPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public static class PackageHash
    {
        public static string Compute(string packageRoot)
        {
            string root = Path.GetFullPath(packageRoot);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => Relative(root, path), StringComparer.Ordinal)
                .ToArray();
            using (SHA256 sha = SHA256.Create())
            {
                foreach (string file in files)
                {
                    string relative = Relative(root, file);
                    byte[] pathBytes = Encoding.UTF8.GetBytes(relative);
                    Append(sha, BitConverter.GetBytes(pathBytes.Length));
                    Append(sha, pathBytes);
                    Append(sha, BitConverter.GetBytes(new FileInfo(file).Length));
                    using (FileStream stream = File.OpenRead(file))
                    {
                        byte[] buffer = new byte[128 * 1024];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            sha.TransformBlock(buffer, 0, read, buffer, 0);
                    }
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return string.Concat(sha.Hash.Select(value => value.ToString("x2")));
            }
        }

        private static string Relative(string root, string path)
        {
            Uri rootUri = new Uri(AppendSeparator(root));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(path)).ToString()).Replace('\\', '/');
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static void Append(HashAlgorithm hash, byte[] data)
        {
            hash.TransformBlock(data, 0, data.Length, data, 0);
        }
    }

    public sealed class ModelActivationReconciler
    {
        private readonly string _exchangeRoot;

        public ModelActivationReconciler(string exchangeRoot)
        {
            _exchangeRoot = AutomationPaths.NormalizeExchangeRoot(exchangeRoot);
        }

        public ActivationResult Reconcile(bool isBusy, Func<string, PackageActivationOutcome> applyPackage)
        {
            return Reconcile(isBusy, "", applyPackage);
        }

        public ActivationResult Reconcile(
            bool isBusy,
            string currentModelPath,
            Func<string, PackageActivationOutcome> applyPackage)
        {
            if (applyPackage == null) throw new ArgumentNullException(nameof(applyPackage));
            AutomationPaths.EnsureLayout(_exchangeRoot);
            string requestPath = AutomationPaths.Request(_exchangeRoot);
            if (!File.Exists(requestPath)) return null;

            using (InterprocessFileLock activationLock = InterprocessFileLock.Acquire(
                Path.Combine(AutomationPaths.Locks(_exchangeRoot), "model-activation.lock"),
                TimeSpan.FromSeconds(2)))
            {
                ActivationRequest request;
                try
                {
                    request = JsonConvert.DeserializeObject<ActivationRequest>(File.ReadAllText(requestPath));
                    if (request == null || request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.RequestId) ||
                        string.IsNullOrWhiteSpace(request.ModelId) || string.IsNullOrWhiteSpace(request.PackageHash))
                        throw new InvalidDataException("activation_request.json is invalid.");
                }
                catch (Exception ex)
                {
                    return WriteFailure(new ActivationRequest(), ex.Message, currentModelPath, currentModelPath);
                }

                ActivationResult existing = ReadResult();
                if (existing != null && string.Equals(existing.RequestId, request.RequestId, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(existing.Status, "applied", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(existing.Status, "failed", StringComparison.OrdinalIgnoreCase)))
                    return existing;

                if (isBusy)
                {
                    ActivationResult pending = BuildResult(
                        request,
                        "pending",
                        "현재 배치 마감 후 적용 예정",
                        currentModelPath,
                        currentModelPath);
                    AtomicJson.Write(AutomationPaths.Result(_exchangeRoot), pending);
                    return pending;
                }

                string packagePath = "";
                try
                {
                    packagePath = AutomationPaths.ResolveReleasePackage(_exchangeRoot, request.PackageRelativePath);
                    if (!Directory.Exists(packagePath)) throw new DirectoryNotFoundException(packagePath);
                    string releasePath = Path.Combine(Path.GetDirectoryName(packagePath), "release.json");
                    if (!File.Exists(releasePath)) throw new FileNotFoundException("release.json not found outside InferencePackage.", releasePath);
                    ReleaseManifest release = JsonConvert.DeserializeObject<ReleaseManifest>(File.ReadAllText(releasePath));
                    if (release == null || !string.Equals(release.ModelId, request.ModelId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(release.PackageHash, request.PackageHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("release.json does not match the activation request.");
                    string actualHash = PackageHash.Compute(packagePath);
                    if (!string.Equals(actualHash, request.PackageHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("InferencePackage hash does not match the activation request.");

                    PackageActivationOutcome outcome = applyPackage(packagePath);
                    ActivationResult applied = BuildResult(
                        request,
                        "applied",
                        "모델 적용 완료",
                        outcome.PreviousModelPath,
                        outcome.ActiveModelPath);
                    applied.AppliedAtUtc = DateTime.UtcNow;
                    AtomicJson.Write(AutomationPaths.Result(_exchangeRoot), applied);
                    return applied;
                }
                catch (Exception ex)
                {
                    return WriteFailure(request, ex.Message, currentModelPath, currentModelPath);
                }
            }
        }

        private ActivationResult WriteFailure(ActivationRequest request, string message, string previous, string active)
        {
            ActivationResult failed = BuildResult(request, "failed", message, previous, active);
            failed.FailedAtUtc = DateTime.UtcNow;
            AtomicJson.Write(AutomationPaths.Result(_exchangeRoot), failed);
            return failed;
        }

        private ActivationResult ReadResult()
        {
            string path = AutomationPaths.Result(_exchangeRoot);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<ActivationResult>(File.ReadAllText(path)); }
            catch { return null; }
        }

        private static ActivationResult BuildResult(
            ActivationRequest request,
            string status,
            string message,
            string previous,
            string active)
        {
            return new ActivationResult
            {
                RequestId = request.RequestId ?? "",
                ModelId = request.ModelId ?? "",
                PackageHash = request.PackageHash ?? "",
                Status = status,
                Message = message,
                PreviousModelPath = previous ?? "",
                ActiveModelPath = active ?? ""
            };
        }
    }
}
