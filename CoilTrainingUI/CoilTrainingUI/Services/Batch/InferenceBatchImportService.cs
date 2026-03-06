using CoilTrainingUI.Models.InferenceBatch;
using IOPath = System.IO.Path;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoilTrainingUI.Services;

public class InferenceBatchImportService
{
    private static readonly string DefaultInboxFolder = "training_inbox";

    public InferenceBatchImportResult Import(string sourceBatchFolder, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceBatchFolder))
            throw new ArgumentException("sourceBatchFolder is empty.", nameof(sourceBatchFolder));

        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("projectRoot is empty.", nameof(projectRoot));

        if (!Directory.Exists(sourceBatchFolder))
            throw new DirectoryNotFoundException($"sourceBatchFolder not found: {sourceBatchFolder}");

        var batchId = IOPath.GetFileName(
            sourceBatchFolder.TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException("batchId is empty.");

        var inboxRoot = IOPath.Combine(projectRoot, DefaultInboxFolder);
        Directory.CreateDirectory(inboxRoot);

        var destinationFolder = GetUniqueImportFolder(inboxRoot, batchId);
        CopyDirectoryRecursively(sourceBatchFolder, destinationFolder);

        var validation = ValidateImportedBatch(destinationFolder);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"복사 후 배치 검증 실패:\n{validation.Message}");
        }

        return new InferenceBatchImportResult
        {
            ImportedPath = destinationFolder,
            ItemCount = validation.TotalItemCount,
            BatchId = batchId
        };
    }

    private static string GetUniqueImportFolder(string inboxRoot, string batchId)
    {
        var destination = IOPath.Combine(inboxRoot, batchId);
        if (!Directory.Exists(destination))
            return destination;

        int suffix = 2;
        while (true)
        {
            var withSuffix = IOPath.Combine(inboxRoot, $"{batchId}_{suffix}");
            if (!Directory.Exists(withSuffix))
                return withSuffix;

            suffix++;
        }
    }

    private static void CopyDirectoryRecursively(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var sourceSubDir in Directory.GetDirectories(sourceDir))
        {
            var destinationSubDir = IOPath.Combine(destinationDir, IOPath.GetFileName(sourceSubDir));
            CopyDirectoryRecursively(sourceSubDir, destinationSubDir);
        }

        foreach (var sourceFile in Directory.GetFiles(sourceDir))
        {
            var destinationFile = IOPath.Combine(destinationDir, IOPath.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private static InferenceBatchValidationResult ValidateImportedBatch(string batchFolder)
    {
        if (string.IsNullOrWhiteSpace(batchFolder) || !Directory.Exists(batchFolder))
            return InferenceBatchValidationResult.Fail("배치 폴더가 존재하지 않습니다.");

        string metaDir = IOPath.Combine(batchFolder, "meta");
        if (!Directory.Exists(metaDir))
            return InferenceBatchValidationResult.Fail("meta 폴더가 없습니다.");

        if (!File.Exists(IOPath.Combine(metaDir, "DONE.flag")))
            return InferenceBatchValidationResult.Fail("완성되지 않은 배치입니다. DONE.flag가 없습니다.");

        string manifestPath = IOPath.Combine(metaDir, "manifest.json");
        if (!File.Exists(manifestPath))
            return InferenceBatchValidationResult.Fail("manifest.json 파일이 없습니다.");

        ManifestDto manifest;
        try
        {
            manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
        }
        catch (Exception ex)
        {
            return InferenceBatchValidationResult.Fail($"manifest.json 파싱 실패: {ex.Message}");
        }

        var missingFiles = new List<string>();
        bool requiresInferFiles = DetermineBatchRequiresInfer(batchFolder, manifest);

        foreach (var item in manifest.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ProcessedImage))
            {
                missingFiles.Add($"[{item.Id}] processed_image가 비어 있음");
                continue;
            }

            if (requiresInferFiles && string.IsNullOrWhiteSpace(item.InferJson))
            {
                missingFiles.Add($"[{item.Id}] infer_json가 비어 있음");
                continue;
            }

            var processedPath = ResolveBatchRelativePath(batchFolder, item.ProcessedImage);

            if (!File.Exists(processedPath))
                missingFiles.Add(item.ProcessedImage);

            if (!string.IsNullOrWhiteSpace(item.RawImage))
            {
                var rawPath = ResolveBatchRelativePath(batchFolder, item.RawImage);
                if (!File.Exists(rawPath))
                    missingFiles.Add(item.RawImage);
            }

            if (requiresInferFiles)
            {
                var inferPath = ResolveBatchRelativePath(batchFolder, item.InferJson);
                if (!File.Exists(inferPath))
                    missingFiles.Add(item.InferJson);
            }
        }

        if (missingFiles.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("배치 검증 실패");
            sb.AppendLine($"총 item 수: {manifest.Items.Count}");
            sb.AppendLine($"누락 파일 개수: {missingFiles.Count}");
            sb.AppendLine("누락 파일 목록:");
            foreach (var missing in missingFiles)
                sb.AppendLine($"- {missing}");

            return InferenceBatchValidationResult.Fail(sb.ToString().TrimEnd());
        }

        return InferenceBatchValidationResult.Ok(manifest.Items.Count);
    }

    private static bool DetermineBatchRequiresInfer(string batchFolder, ManifestDto manifest)
    {
        string batchType = (manifest.BatchType ?? "").Trim().ToLowerInvariant();
        if (batchType == "no_infer")
            return false;

        if (batchType == "inference")
            return true;

        bool hasInferReference = manifest.Items.Any(item => !string.IsNullOrWhiteSpace(item.InferJson));
        if (hasInferReference)
            return true;

        string inferenceDir = IOPath.Combine(batchFolder, "inference");
        if (Directory.Exists(inferenceDir) &&
            Directory.EnumerateFiles(inferenceDir, "*.json", SearchOption.TopDirectoryOnly).Any())
        {
            return true;
        }

        return false;
    }

    private static string ResolveBatchRelativePath(string batchFolder, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return string.Empty;

        if (IOPath.IsPathRooted(candidatePath))
            return candidatePath;

        return IOPath.Combine(batchFolder, candidatePath);
    }

    private sealed class InferenceBatchValidationResult
    {
        public bool IsValid { get; init; }
        public string Message { get; init; } = "";
        public int TotalItemCount { get; init; }

        public static InferenceBatchValidationResult Ok(int totalItemCount)
            => new() { IsValid = true, Message = "배치 검증 OK", TotalItemCount = totalItemCount };

        public static InferenceBatchValidationResult Fail(string message)
            => new() { IsValid = false, Message = message };
    }
}

public class InferenceBatchImportResult
{
    public string ImportedPath { get; set; } = "";
    public int ItemCount { get; set; }
    public string BatchId { get; set; } = "";
}
