using CoilTrainingUI.Models;
using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Services;

public sealed class BatchMergeResult
{
    public string MergedBatchKey { get; set; } = "";
    public string MergedBatchPath { get; set; } = "";
    public int ItemCount { get; set; }
    public List<string> SourceBatchKeys { get; set; } = new();
}

public sealed class BatchMergeProgressInfo
{
    public int Percent { get; set; }
    public string Status { get; set; } = "";
    public string LogLine { get; set; } = "";
}

public sealed class BatchMergeService
{
    public BatchMergeResult MergeSelectedBatches(
        string inboxRoot,
        IReadOnlyList<BatchLibraryItem> sourceBatches,
        IProgress<BatchMergeProgressInfo>? progress = null)
    {
        if (sourceBatches == null || sourceBatches.Count < 2)
            throw new InvalidOperationException("병합할 배치를 2개 이상 선택하세요.");

        Directory.CreateDirectory(inboxRoot);

        var orderedSources = sourceBatches
            .Where(batch => batch != null && !string.IsNullOrWhiteSpace(batch.BatchRoot))
            .OrderBy(batch => batch.BatchId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedSources.Count < 2)
            throw new InvalidOperationException("유효한 배치를 2개 이상 선택하세요.");

        var sourceManifests = orderedSources
            .Select(batch => new SourceBatchManifest
            {
                Batch = batch,
                Manifest = InferenceBatchSchemaParser.ParseManifest(Path.Combine(batch.BatchRoot, "meta", "manifest.json"))
            })
            .ToList();

        foreach (SourceBatchManifest source in sourceManifests)
        {
            source.RequiresInfer = InferenceBatchPathResolver.DetermineBatchRequiresInfer(
                source.Batch.BatchRoot,
                source.Manifest);
            source.ExpectedContextId = InferenceContextValidationService.GetExpectedContextId(source.Manifest);
        }

        List<SourceBatchManifest> inferenceSources = sourceManifests
            .Where(source => source.RequiresInfer)
            .ToList();
        List<string> recordedContextIds = inferenceSources
            .Select(source => source.ExpectedContextId)
            .Where(contextId => !string.IsNullOrWhiteSpace(contextId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool hasUnknownInferenceContext = inferenceSources.Any(source =>
            string.IsNullOrWhiteSpace(source.ExpectedContextId));
        if (recordedContextIds.Count > 1 ||
            (recordedContextIds.Count == 1 && hasUnknownInferenceContext))
        {
            throw new InvalidOperationException(
                "추론 컨텍스트가 서로 다르거나 컨텍스트가 누락된 추론 배치는 병합할 수 없습니다.");
        }

        InferenceContextDto? mergedInferenceContext = recordedContextIds.Count == 1
            ? inferenceSources.First(source => string.Equals(
                source.ExpectedContextId,
                recordedContextIds[0],
                StringComparison.OrdinalIgnoreCase)).Manifest.InferenceContext
            : null;

        int totalItems = sourceManifests.Sum(item => item.Manifest.Items.Count);
        int processedItems = 0;

        progress?.Report(new BatchMergeProgressInfo
        {
            Percent = 0,
            Status = "병합 준비 중...",
            LogLine = $"원본 배치 {sourceManifests.Count}개 / 이미지 {totalItems}개"
        });

        string baseBatchKey = $"merged_{DateTime.Now:yyyyMMdd_HHmmss}";
        string batchRoot = GetUniqueBatchFolderPath(inboxRoot, baseBatchKey, out string mergedBatchKey);

        string imagesDir = Path.Combine(batchRoot, "images");
        string rawDir = Path.Combine(batchRoot, "raw");
        string inferenceDir = Path.Combine(batchRoot, "inference");
        string metaDir = Path.Combine(batchRoot, "meta");

        var usedProcessedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedRawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedInferNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestItems = new List<MergedManifestItem>();

        try
        {
            Directory.CreateDirectory(imagesDir);
            Directory.CreateDirectory(rawDir);
            Directory.CreateDirectory(inferenceDir);
            Directory.CreateDirectory(metaDir);

            foreach (var sourceInfo in sourceManifests)
            {
                var sourceBatch = sourceInfo.Batch;
                var manifest = sourceInfo.Manifest;

                foreach (var item in manifest.Items)
                {
                    string processedSourcePath = InferenceBatchPathResolver.ResolveBatchProcessedImagePath(sourceBatch.BatchRoot, item);
                    string originalProcessedFileName = Path.GetFileName(processedSourcePath);
                    string uniqueProcessedFileName = GetUniqueFileName(
                        $"{sourceBatch.BatchKey}__{originalProcessedFileName}",
                        usedProcessedNames);
                    string processedDestinationPath = Path.Combine(imagesDir, uniqueProcessedFileName);
                    File.Copy(processedSourcePath, processedDestinationPath, overwrite: false);

                    string fallbackId = Path.GetFileNameWithoutExtension(originalProcessedFileName);
                    string originalItemId = string.IsNullOrWhiteSpace(item.Id) ? fallbackId : item.Id.Trim();
                    string mergedItemId = $"{sourceBatch.BatchKey}__{originalItemId}";

                    string? rawManifestPath = null;
                    string? rawSourcePath = InferenceBatchPathResolver.ResolveBatchRawImagePath(sourceBatch.BatchRoot, item);
                    if (!string.IsNullOrWhiteSpace(rawSourcePath) && File.Exists(rawSourcePath))
                    {
                        string uniqueRawFileName = GetUniqueFileName(
                            $"{sourceBatch.BatchKey}__{Path.GetFileName(rawSourcePath)}",
                            usedRawNames);
                        string rawDestinationPath = Path.Combine(rawDir, uniqueRawFileName);
                        File.Copy(rawSourcePath, rawDestinationPath, overwrite: false);
                        rawManifestPath = $"raw/{uniqueRawFileName}";
                    }

                    string? inferManifestPath = null;
                    string inferSourcePath = InferenceBatchPathResolver.ResolveBatchInferJsonPath(sourceBatch.BatchRoot, item);
                    bool itemRequiresInfer = InferenceBatchPathResolver.DetermineItemRequiresInfer(
                        sourceBatch.BatchRoot,
                        manifest,
                        item);
                    if (itemRequiresInfer)
                    {
                        if (!File.Exists(inferSourcePath))
                            throw new FileNotFoundException("필수 infer.json을 찾을 수 없습니다.", inferSourcePath);

                        string uniqueInferFileName = GetUniqueFileName(
                            $"{sourceBatch.BatchKey}__{originalItemId}.infer.json",
                            usedInferNames);
                        string inferDestinationPath = Path.Combine(inferenceDir, uniqueInferFileName);
                        CopyInferJsonWithNewImageId(
                            inferSourcePath,
                            inferDestinationPath,
                            mergedItemId,
                            sourceInfo.ExpectedContextId);
                        inferManifestPath = $"inference/{uniqueInferFileName}";
                    }

                    CopyReviewFilesIfExists(processedSourcePath, processedDestinationPath);

                    manifestItems.Add(new MergedManifestItem
                    {
                        Id = mergedItemId,
                        ProcessedImage = $"images/{uniqueProcessedFileName}",
                        RawImage = rawManifestPath,
                        InferJson = inferManifestPath,
                        SourceBatchId = sourceBatch.BatchId,
                        SourceItemId = originalItemId
                    });

                    processedItems++;
                    int percent = totalItems <= 0
                        ? 90
                        : Math.Min(90, (int)Math.Round((processedItems * 90.0) / totalItems));
                    progress?.Report(new BatchMergeProgressInfo
                    {
                        Percent = percent,
                        Status = $"병합 중... ({processedItems}/{totalItems})",
                        LogLine = $"{sourceBatch.BatchId} / {originalItemId}"
                    });
                }
            }

            progress?.Report(new BatchMergeProgressInfo
            {
                Percent = 94,
                Status = "manifest 작성 중..."
            });

            string manifestOutputPath = Path.Combine(metaDir, "manifest.json");
            WriteMergedManifest(
                manifestOutputPath,
                mergedBatchKey,
                orderedSources.Select(batch => batch.BatchId).ToList(),
                manifestItems,
                mergedInferenceContext
            );

            progress?.Report(new BatchMergeProgressInfo
            {
                Percent = 97,
                Status = "배치 검증 중..."
            });

            string doneFlagPath = Path.Combine(metaDir, "DONE.flag");
            File.WriteAllText(doneFlagPath, "done", Encoding.UTF8);

            var validation = BatchFolderValidationService.Validate(batchRoot);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Message);

            progress?.Report(new BatchMergeProgressInfo
            {
                Percent = 100,
                Status = "병합 완료",
                LogLine = mergedBatchKey
            });

            return new BatchMergeResult
            {
                MergedBatchKey = mergedBatchKey,
                MergedBatchPath = batchRoot,
                ItemCount = manifestItems.Count,
                SourceBatchKeys = orderedSources.Select(batch => batch.BatchKey).ToList()
            };
        }
        catch
        {
            TryDeleteDirectory(batchRoot);
            throw;
        }
    }

    private static void CopyInferJsonWithNewImageId(
        string sourcePath,
        string destinationPath,
        string imageId,
        string expectedContextId)
    {
        InferResultDto infer = InferenceBatchSchemaParser.ParseInferResult(sourcePath);
        InferenceContextValidationService.ValidateInferContext(infer, expectedContextId, sourcePath);
        infer.ImageId = imageId;

        string json = JsonSerializer.Serialize(
            infer,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

        File.WriteAllText(destinationPath, json, Encoding.UTF8);
    }

    private static void CopyReviewFilesIfExists(string processedSourcePath, string processedDestinationPath)
    {
        string sourceStatePath = ImageStateService.GetStatePath(processedSourcePath);
        if (File.Exists(sourceStatePath))
        {
            string destinationStatePath = ImageStateService.GetStatePath(processedDestinationPath);
            File.Copy(sourceStatePath, destinationStatePath, overwrite: false);
        }

        string sourceReviewPath = ReviewRepository.GetReviewPath(processedSourcePath);
        if (File.Exists(sourceReviewPath))
        {
            string destinationReviewPath = ReviewRepository.GetReviewPath(processedDestinationPath);
            File.Copy(sourceReviewPath, destinationReviewPath, overwrite: false);
        }
    }

    private static void WriteMergedManifest(
        string manifestPath,
        string mergedBatchKey,
        IReadOnlyList<string> sourceBatchIds,
        IReadOnlyList<MergedManifestItem> items,
        InferenceContextDto? inferenceContext)
    {
        var manifest = new
        {
            schema_version = inferenceContext == null ? 2 : 3,
            batch_type = "merged",
            batch_id = mergedBatchKey,
            created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            inference_context = inferenceContext,
            meta = new
            {
                merge = true,
                source_batches = sourceBatchIds
            },
            items = items.Select(item => new
            {
                id = item.Id,
                processed_image = item.ProcessedImage,
                raw_image = item.RawImage,
                infer_json = item.InferJson,
                source_batch_id = item.SourceBatchId,
                source_item_id = item.SourceItemId
            }).ToList()
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

        File.WriteAllText(manifestPath, json, Encoding.UTF8);
    }

    private static string GetUniqueBatchFolderPath(string inboxRoot, string baseBatchId, out string batchId)
    {
        batchId = baseBatchId;
        string candidate = Path.Combine(inboxRoot, batchId);
        int suffix = 2;

        while (Directory.Exists(candidate))
        {
            batchId = $"{baseBatchId}_{suffix}";
            candidate = Path.Combine(inboxRoot, batchId);
            suffix++;
        }

        return candidate;
    }

    private static string GetUniqueFileName(string originalFileName, ISet<string> usedFileNames)
    {
        string ext = Path.GetExtension(originalFileName);
        string baseName = Path.GetFileNameWithoutExtension(originalFileName);

        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bmp";
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "image";

        string candidate = $"{baseName}{ext}";
        int suffix = 2;

        while (usedFileNames.Contains(candidate))
        {
            candidate = $"{baseName}_{suffix}{ext}";
            suffix++;
        }

        usedFileNames.Add(candidate);
        return candidate;
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return;

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class MergedManifestItem
    {
        public string Id { get; set; } = "";
        public string ProcessedImage { get; set; } = "";
        public string? RawImage { get; set; }
        public string? InferJson { get; set; }
        public string SourceBatchId { get; set; } = "";
        public string SourceItemId { get; set; } = "";
    }

    private sealed class SourceBatchManifest
    {
        public BatchLibraryItem Batch { get; set; } = new();
        public ManifestDto Manifest { get; set; } = new();
        public bool RequiresInfer { get; set; }
        public string ExpectedContextId { get; set; } = "";
    }
}
