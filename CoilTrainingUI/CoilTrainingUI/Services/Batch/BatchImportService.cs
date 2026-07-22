using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services;

public sealed class BatchImageRecord
{
    public string BatchId { get; init; } = "";
    public string BatchKey { get; init; } = "";
    public string BatchRoot { get; init; } = "";
    public string ImageId { get; init; } = "";
    public string ProcessedPath { get; init; } = "";
    public string? RawPath { get; init; }
    public string InferJsonPath { get; init; } = "";
    public bool RequiresInfer { get; init; }
}

public sealed class BatchImportLoadResult
{
    public List<BatchImageRecord> Images { get; } = new();
    public List<string> Skipped { get; } = new();
}

/// <summary>
/// Reads already-imported batch manifests without changing inference or review files.
/// Copying an external batch into the library remains the responsibility of
/// InferenceBatchImportService.
/// </summary>
public sealed class BatchImportService
{
    private readonly BatchLibraryService _libraryService;

    public BatchImportService(BatchLibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    public BatchImportLoadResult LoadLibrary(string inboxRoot, bool includeHidden = false)
    {
        var result = new BatchImportLoadResult();
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scan = _libraryService.Scan(inboxRoot, includeHidden);
        result.Skipped.AddRange(scan.Skipped);

        foreach (var batch in scan.Batches)
        {
            try
            {
                string manifestPath = Path.Combine(batch.BatchRoot, "meta", "manifest.json");
                ManifestDto manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
                string batchId = !string.IsNullOrWhiteSpace(manifest.BatchId)
                    ? manifest.BatchId.Trim()
                    : batch.BatchId;

                foreach (ManifestItemDto item in manifest.Items)
                {
                    try
                    {
                        string processedPath = InferenceBatchPathResolver.ResolveBatchProcessedImagePath(batch.BatchRoot, item);
                        if (!seenImages.Add(processedPath))
                            continue;

                        result.Images.Add(new BatchImageRecord
                        {
                            BatchId = batchId,
                            BatchKey = batch.BatchKey,
                            BatchRoot = batch.BatchRoot,
                            ImageId = item.Id,
                            ProcessedPath = processedPath,
                            RawPath = InferenceBatchPathResolver.ResolveBatchRawImagePath(batch.BatchRoot, item),
                            InferJsonPath = InferenceBatchPathResolver.ResolveBatchInferJsonPath(batch.BatchRoot, item),
                            RequiresInfer = InferenceBatchPathResolver.DetermineItemRequiresInfer(batch.BatchRoot, manifest, item)
                        });
                    }
                    catch (Exception ex)
                    {
                        result.Skipped.Add($"{batchId}/{item.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Skipped.Add($"{batch.BatchId}: manifest 로드 실패 ({ex.Message})");
            }
        }

        return result;
    }
}
