using CoilTrainingUI.Models;
using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services;

public sealed class BatchLibraryScanResult
{
    public List<BatchLibraryItem> Batches { get; } = new();
    public List<string> Skipped { get; } = new();
}

public sealed class BatchLibraryService
{
    public BatchLibraryScanResult Scan(string inboxRoot, bool includeHidden)
    {
        Directory.CreateDirectory(inboxRoot);

        var result = new BatchLibraryScanResult();
        var registry = BatchRegistryService.Load(inboxRoot);

        foreach (string candidateFolder in Directory.GetDirectories(inboxRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string folderName = Path.GetFileName(candidateFolder);
            if (string.Equals(folderName, "_train_runs", StringComparison.OrdinalIgnoreCase))
                continue;

            var validation = BatchFolderValidationService.Validate(candidateFolder);
            if (!validation.IsValid)
            {
                result.Skipped.Add($"{folderName}: {GetFirstLine(validation.Message)}");
                continue;
            }

            try
            {
                string manifestPath = Path.Combine(candidateFolder, "meta", "manifest.json");
                ManifestDto manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);

                string batchKey = GetBatchKey(candidateFolder);
                registry.Batches.TryGetValue(batchKey, out var registryEntry);

                var batch = new BatchLibraryItem
                {
                    BatchKey = batchKey,
                    BatchId = ResolveBatchId(candidateFolder, manifest),
                    BatchRoot = candidateFolder,
                    ItemCount = manifest.Items.Count,
                    CreatedAt = TryParseCreatedAt(manifest.CreatedAt),
                    BatchKind = ResolveBatchKind(manifest, registryEntry),
                    IsHidden = registryEntry?.Hidden == true,
                    HiddenReason = registryEntry?.HiddenReason ?? "",
                    RequiresInfer = InferenceBatchPathResolver.DetermineBatchRequiresInfer(candidateFolder, manifest),
                    HasAnyInfer = manifest.Items.Any(item =>
                        File.Exists(InferenceBatchPathResolver.ResolveBatchInferJsonPath(candidateFolder, item))),
                    ReviewStatus = registryEntry?.ReviewStatus ?? "pending",
                    SourceBatches = registryEntry?.SourceBatches?.ToList() ?? new List<string>()
                };

                if (includeHidden || !batch.IsHidden)
                    result.Batches.Add(batch);
            }
            catch (Exception ex)
            {
                result.Skipped.Add($"{Path.GetFileName(candidateFolder)}: manifest 로드 실패 ({ex.Message})");
            }
        }

        return result;
    }

    public static string GetBatchKey(string batchRoot)
    {
        return Path.GetFileName(batchRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string ResolveBatchId(string batchRoot, ManifestDto manifest)
    {
        return !string.IsNullOrWhiteSpace(manifest.BatchId)
            ? manifest.BatchId.Trim()
            : GetBatchKey(batchRoot);
    }

    private static DateTime? TryParseCreatedAt(string? createdAt)
    {
        if (string.IsNullOrWhiteSpace(createdAt))
            return null;

        if (DateTime.TryParse(
                createdAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ResolveBatchKind(ManifestDto manifest, BatchRegistryEntryDto? registryEntry)
    {
        if (!string.IsNullOrWhiteSpace(registryEntry?.BatchKind))
            return registryEntry.BatchKind;

        if (!string.IsNullOrWhiteSpace(manifest.BatchType))
            return manifest.BatchType;

        return "regular";
    }

    private static string GetFirstLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "(no details)";

        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0] : message;
    }
}
