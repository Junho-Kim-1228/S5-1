using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Services.Automation;

public sealed class BatchImportRegistryDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("imports")] public List<BatchImportRegistryEntry> Imports { get; set; } = new();
    [JsonPropertyName("conflicts")] public List<BatchImportRegistryEntry> Conflicts { get; set; } = new();
}

public sealed class BatchImportRegistryEntry
{
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = "";
    [JsonPropertyName("manifest_sha256")] public string ManifestSha256 { get; set; } = "";
    [JsonPropertyName("source_path")] public string SourcePath { get; set; } = "";
    [JsonPropertyName("library_path")] public string LibraryPath { get; set; } = "";
    [JsonPropertyName("recorded_at_utc")] public DateTime RecordedAtUtc { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public sealed class BatchImportReceipt
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = "";
    [JsonPropertyName("manifest_sha256")] public string ManifestSha256 { get; set; } = "";
    [JsonPropertyName("source_path")] public string SourcePath { get; set; } = "";
    [JsonPropertyName("archive_path")] public string ArchivePath { get; set; } = "";
    [JsonPropertyName("library_path")] public string LibraryPath { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("recorded_at_utc")] public DateTime RecordedAtUtc { get; set; }
}

public sealed class BatchReconcileResult
{
    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public int ConflictCount { get; set; }
    public string LastMessage { get; set; } = "검색 완료";
    public List<BatchImportReceipt> Receipts { get; } = new();
}

public sealed class BatchInboxReconciler
{
    private readonly string _exchangeRoot;
    private readonly string _libraryRoot;
    private readonly Action<string, string> _copyFile;

    public BatchInboxReconciler(
        string exchangeRoot,
        string libraryRoot,
        Action<string, string>? copyFile = null)
    {
        _exchangeRoot = AutomationPaths.NormalizeExchangeRoot(exchangeRoot);
        _libraryRoot = Path.GetFullPath(libraryRoot);
        _copyFile = copyFile ?? new Action<string, string>(
            (source, destination) => File.Copy(source, destination, overwrite: false));
    }

    public BatchReconcileResult Reconcile()
    {
        AutomationPaths.EnsureLayout(_exchangeRoot);
        Directory.CreateDirectory(_libraryRoot);
        string automationDirectory = Path.Combine(_libraryRoot, ".automation");
        string registryPath = Path.Combine(automationDirectory, "import_registry.json");
        string stagingRoot = Path.Combine(_libraryRoot, "_importing");
        Directory.CreateDirectory(automationDirectory);
        Directory.CreateDirectory(stagingRoot);

        using InterprocessFileLock importLock = InterprocessFileLock.Acquire(
            Path.Combine(AutomationPaths.Locks(_exchangeRoot), "batch-import.lock"),
            TimeSpan.FromSeconds(2));

        BatchImportRegistryDocument registry = LoadRegistry(registryPath);
        var result = new BatchReconcileResult();
        foreach (string source in Directory.GetDirectories(AutomationPaths.Outbox(_exchangeRoot))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(source), "_working", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(source, "meta", "DONE.flag")))
            {
                continue;
            }

            BatchImportReceipt receipt = ProcessBatch(source, stagingRoot, registry);
            result.Receipts.Add(receipt);
            result.LastMessage = receipt.Message;
            switch (receipt.Status)
            {
                case "imported": result.ImportedCount++; break;
                case "duplicate": result.DuplicateCount++; break;
                case "conflict": result.ConflictCount++; break;
                default: result.FailedCount++; break;
            }
            if (receipt.Status is "imported" or "duplicate")
                ArchiveCompletedBatch(source, receipt);
            WriteReceipt(receipt);
        }

        AtomicJsonFile.Write(registryPath, registry);
        TryDeleteIfEmpty(stagingRoot);
        return result;
    }

    private void ArchiveCompletedBatch(string source, BatchImportReceipt receipt)
    {
        try
        {
            if (!Directory.Exists(source))
                return;

            string archiveRoot = AutomationPaths.Archive(_exchangeRoot);
            Directory.CreateDirectory(archiveRoot);
            string folderName = Path.GetFileName(source.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            string destination = Path.Combine(archiveRoot, folderName);
            if (Directory.Exists(destination))
            {
                string identity = string.IsNullOrWhiteSpace(receipt.ManifestSha256)
                    ? Guid.NewGuid().ToString("N")[..8]
                    : receipt.ManifestSha256[..Math.Min(8, receipt.ManifestSha256.Length)];
                destination = Path.Combine(archiveRoot, $"{folderName}-{identity}");
                if (Directory.Exists(destination))
                    destination += "-" + Guid.NewGuid().ToString("N")[..8];
            }

            Directory.Move(source, destination);
            receipt.ArchivePath = destination;
            receipt.Message += " outbox 원본을 archive로 이동했습니다.";
        }
        catch (Exception ex)
        {
            // 가져온 라이브러리 복사본은 유효하므로, 원본은 다음 동기화에서 다시 정리한다.
            receipt.Message += " archive 이동 실패: " + ex.Message;
        }
    }

    private BatchImportReceipt ProcessBatch(
        string source,
        string stagingRoot,
        BatchImportRegistryDocument registry)
    {
        string manifestPath = Path.Combine(source, "meta", "manifest.json");
        string manifestHash = File.Exists(manifestPath) ? AutomationHash.FileSha256(manifestPath) : "";
        string sourcePath = Path.GetFullPath(source);
        string batchId = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            BatchFolderValidationResult sourceValidation = BatchFolderValidationService.Validate(sourcePath);
            if (!sourceValidation.IsValid)
                throw new InvalidDataException(sourceValidation.Message);

            ManifestDto manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
            if (!string.IsNullOrWhiteSpace(manifest.BatchId))
                batchId = manifest.BatchId.Trim();
            ValidateBatchId(batchId);

            BatchImportRegistryEntry? sameId = registry.Imports.FirstOrDefault(entry =>
                string.Equals(entry.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
            if (sameId != null)
            {
                if (string.Equals(sameId.ManifestSha256, manifestHash, StringComparison.OrdinalIgnoreCase))
                    return Receipt(batchId, manifestHash, sourcePath, sameId.LibraryPath, "duplicate", "이미 가져온 배치입니다.");
                return RecordConflict(registry, batchId, manifestHash, sourcePath,
                    "같은 batch_id에 다른 manifest가 있어 자동 가져오기를 중단했습니다.");
            }

            string finalPath = Path.Combine(_libraryRoot, batchId);
            if (Directory.Exists(finalPath))
            {
                string existingManifest = Path.Combine(finalPath, "meta", "manifest.json");
                if (File.Exists(existingManifest) &&
                    string.Equals(AutomationHash.FileSha256(existingManifest), manifestHash, StringComparison.OrdinalIgnoreCase))
                {
                    RecordImport(registry, batchId, manifestHash, sourcePath, finalPath, "기존 동일 배치를 등록했습니다.");
                    return Receipt(batchId, manifestHash, sourcePath, finalPath, "duplicate", "동일한 배치가 학습 라이브러리에 이미 있습니다.");
                }
                return RecordConflict(registry, batchId, manifestHash, sourcePath,
                    "학습 라이브러리에 같은 batch_id의 다른 배치가 있습니다.");
            }

            string stagingPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectory(sourcePath, stagingPath);
                BatchFolderValidationResult stagingValidation = BatchFolderValidationService.Validate(stagingPath);
                if (!stagingValidation.IsValid)
                    throw new InvalidDataException("staging 검증 실패: " + stagingValidation.Message);
                Directory.Move(stagingPath, finalPath);
            }
            catch
            {
                TryDeleteDirectory(stagingPath);
                throw;
            }

            RecordImport(registry, batchId, manifestHash, sourcePath, finalPath, "자동 가져오기 완료");
            return Receipt(batchId, manifestHash, sourcePath, finalPath, "imported", "새 배치를 가져왔습니다.");
        }
        catch (Exception ex)
        {
            return Receipt(batchId, manifestHash, sourcePath, "", "failed", ex.Message);
        }
    }

    private static void ValidateBatchId(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId) ||
            !string.Equals(Path.GetFileName(batchId), batchId, StringComparison.Ordinal) ||
            batchId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("batch_id가 안전한 폴더 이름이 아닙니다.");
        }
    }

    private BatchImportReceipt RecordConflict(
        BatchImportRegistryDocument registry,
        string batchId,
        string manifestHash,
        string source,
        string message)
    {
        if (!registry.Conflicts.Any(entry =>
                string.Equals(entry.BatchId, batchId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.ManifestSha256, manifestHash, StringComparison.OrdinalIgnoreCase)))
        {
            registry.Conflicts.Add(new BatchImportRegistryEntry
            {
                BatchId = batchId,
                ManifestSha256 = manifestHash,
                SourcePath = source,
                Message = message,
                RecordedAtUtc = DateTime.UtcNow
            });
        }
        return Receipt(batchId, manifestHash, source, "", "conflict", message);
    }

    private static void RecordImport(
        BatchImportRegistryDocument registry,
        string batchId,
        string manifestHash,
        string source,
        string libraryPath,
        string message)
    {
        registry.Imports.Add(new BatchImportRegistryEntry
        {
            BatchId = batchId,
            ManifestSha256 = manifestHash,
            SourcePath = source,
            LibraryPath = libraryPath,
            RecordedAtUtc = DateTime.UtcNow,
            Message = message
        });
    }

    private void WriteReceipt(BatchImportReceipt receipt)
    {
        string identity = string.IsNullOrWhiteSpace(receipt.ManifestSha256)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(receipt.SourcePath))).ToLowerInvariant()
            : receipt.ManifestSha256;
        string safeId = string.Concat(receipt.BatchId.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        AtomicJsonFile.Write(
            Path.Combine(AutomationPaths.Receipts(_exchangeRoot), $"{safeId}-{identity[..Math.Min(16, identity.Length)]}.json"),
            receipt);
    }

    private static BatchImportReceipt Receipt(
        string batchId,
        string manifestHash,
        string source,
        string libraryPath,
        string status,
        string message) => new()
    {
        BatchId = batchId,
        ManifestSha256 = manifestHash,
        SourcePath = source,
        LibraryPath = libraryPath,
        Status = status,
        Message = message,
        RecordedAtUtc = DateTime.UtcNow
    };

    private void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            _copyFile(file, target);
        }
    }

    private static BatchImportRegistryDocument LoadRegistry(string path)
    {
        if (!File.Exists(path))
            return new BatchImportRegistryDocument();
        try
        {
            return JsonSerializer.Deserialize<BatchImportRegistryDocument>(File.ReadAllText(path))
                   ?? new BatchImportRegistryDocument();
        }
        catch (JsonException ex)
        {
            string backupPath = path +
                                $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            try
            {
                File.Move(path, backupPath, overwrite: false);
            }
            catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException(
                    "자동 배치 가져오기 레지스트리가 손상되었고 백업 격리에도 실패했습니다.",
                    new AggregateException(ex, backupException));
            }

            return new BatchImportRegistryDocument();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static void TryDeleteIfEmpty(string path)
    {
        try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); } catch { }
    }
}
