using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoilTrainingUI.Services;

public sealed class BatchFolderValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = "";
    public int TotalItemCount { get; init; }

    public static BatchFolderValidationResult Ok(string message, int totalItemCount)
        => new() { IsValid = true, Message = message, TotalItemCount = totalItemCount };

    public static BatchFolderValidationResult Fail(string message)
        => new() { IsValid = false, Message = message };
}

public static class BatchFolderValidationService
{
    public static BatchFolderValidationResult Validate(string batchFolder)
    {
        if (string.IsNullOrWhiteSpace(batchFolder) || !Directory.Exists(batchFolder))
            return BatchFolderValidationResult.Fail("배치 폴더가 존재하지 않습니다.");

        string metaDir = Path.Combine(batchFolder, "meta");
        if (!Directory.Exists(metaDir))
            return BatchFolderValidationResult.Fail("meta 폴더가 없습니다.");

        if (!File.Exists(Path.Combine(metaDir, "DONE.flag")))
            return BatchFolderValidationResult.Fail("완성되지 않은 배치입니다. DONE.flag가 없습니다.");

        string manifestPath = Path.Combine(metaDir, "manifest.json");
        if (!File.Exists(manifestPath))
            return BatchFolderValidationResult.Fail("manifest.json 파일이 없습니다.");

        ManifestDto manifest;
        try
        {
            manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
        }
        catch (Exception ex)
        {
            return BatchFolderValidationResult.Fail($"manifest.json 파싱 실패: {ex.Message}");
        }

        bool requiresInfer = InferenceBatchPathResolver.DetermineBatchRequiresInfer(batchFolder, manifest);
        var missingFiles = new List<string>();

        foreach (var item in manifest.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ProcessedImage))
            {
                missingFiles.Add($"[{item.Id}] processed_image가 비어 있음");
                continue;
            }

            string processedPath = InferenceBatchPathResolver.ResolveBatchRelativePath(batchFolder, item.ProcessedImage);
            if (!File.Exists(processedPath))
                missingFiles.Add(item.ProcessedImage);

            if (!string.IsNullOrWhiteSpace(item.RawImage))
            {
                string rawPath = InferenceBatchPathResolver.ResolveBatchRelativePath(batchFolder, item.RawImage);
                if (!File.Exists(rawPath))
                    missingFiles.Add(item.RawImage);
            }

            if (requiresInfer)
            {
                if (string.IsNullOrWhiteSpace(item.InferJson))
                {
                    missingFiles.Add($"[{item.Id}] infer_json가 비어 있음");
                    continue;
                }

                string inferPath = InferenceBatchPathResolver.ResolveBatchRelativePath(batchFolder, item.InferJson);
                if (!File.Exists(inferPath))
                    missingFiles.Add(item.InferJson);
            }
        }

        string previewIds = string.Join(", ", manifest.Items
            .Select(item => string.IsNullOrWhiteSpace(item.Id) ? "(no id)" : item.Id)
            .Take(3));

        string inferredBatchType = string.IsNullOrWhiteSpace(manifest.BatchType)
            ? (requiresInfer ? "inference" : "no_infer")
            : manifest.BatchType;

        if (missingFiles.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("배치 검증 실패");
            sb.AppendLine($"batch_type: {inferredBatchType}");
            sb.AppendLine($"총 item 수: {manifest.Items.Count}");
            sb.AppendLine($"누락 파일 개수: {missingFiles.Count}");
            sb.AppendLine("누락 파일 목록:");
            foreach (var item in missingFiles)
                sb.AppendLine($"- {item}");
            sb.AppendLine($"첫 3개 id: {previewIds}");

            return BatchFolderValidationResult.Fail(sb.ToString().TrimEnd());
        }

        return BatchFolderValidationResult.Ok(
            $"배치 검증 OK\nbatch_type: {inferredBatchType}\n총 item 수: {manifest.Items.Count}\n누락 파일 개수: 0\n첫 3개 id: {previewIds}",
            manifest.Items.Count);
    }
}
