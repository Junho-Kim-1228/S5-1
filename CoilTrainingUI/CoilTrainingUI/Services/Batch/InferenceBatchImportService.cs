using IOPath = System.IO.Path;
using System;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services;

public sealed class InferenceBatchImportProgressInfo
{
    public int Percent { get; set; }
    public string Status { get; set; } = "";
    public string LogLine { get; set; } = "";
}

public class InferenceBatchImportService
{
    private static readonly string DefaultInboxFolder = "training_inbox";

    public InferenceBatchImportResult Import(
        string sourceBatchFolder,
        string projectRoot,
        string? inboxRoot = null,
        IProgress<InferenceBatchImportProgressInfo>? progress = null)
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

        var resolvedInboxRoot = ResolveInboxRoot(projectRoot, inboxRoot);
        Directory.CreateDirectory(resolvedInboxRoot);

        progress?.Report(new InferenceBatchImportProgressInfo
        {
            Percent = 0,
            Status = "가져올 파일 수 계산 중...",
            LogLine = sourceBatchFolder
        });

        int totalFileCount = Directory.EnumerateFiles(sourceBatchFolder, "*", SearchOption.AllDirectories).Count();
        var destinationFolder = GetUniqueImportFolder(resolvedInboxRoot, batchId);
        int copiedFileCount = 0;

        try
        {
            progress?.Report(new InferenceBatchImportProgressInfo
            {
                Percent = 2,
                Status = $"배치 복사 준비 중... (총 {totalFileCount}개 파일)",
                LogLine = destinationFolder
            });

            CopyDirectoryRecursively(
                sourceBatchFolder,
                destinationFolder,
                sourceBatchFolder,
                totalFileCount,
                ref copiedFileCount,
                progress);

            progress?.Report(new InferenceBatchImportProgressInfo
            {
                Percent = 95,
                Status = "복사 완료, 배치 검증 중..."
            });

            var validation = BatchFolderValidationService.Validate(destinationFolder);
            if (!validation.IsValid)
                throw new InvalidOperationException($"복사 후 배치 검증 실패:\n{validation.Message}");

            progress?.Report(new InferenceBatchImportProgressInfo
            {
                Percent = 100,
                Status = "배치 불러오기 완료",
                LogLine = batchId
            });

            return new InferenceBatchImportResult
            {
                ImportedPath = destinationFolder,
                ItemCount = validation.TotalItemCount,
                BatchId = batchId
            };
        }
        catch
        {
            TryDeleteDirectory(destinationFolder);
            throw;
        }
    }

    private static string ResolveInboxRoot(string projectRoot, string? inboxRoot)
    {
        if (!string.IsNullOrWhiteSpace(inboxRoot))
            return IOPath.GetFullPath(inboxRoot);

        return IOPath.Combine(projectRoot, DefaultInboxFolder);
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

    private static void CopyDirectoryRecursively(
        string sourceDir,
        string destinationDir,
        string rootSourceDir,
        int totalFileCount,
        ref int copiedFileCount,
        IProgress<InferenceBatchImportProgressInfo>? progress)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var sourceSubDir in Directory.GetDirectories(sourceDir))
        {
            var destinationSubDir = IOPath.Combine(destinationDir, IOPath.GetFileName(sourceSubDir));
            CopyDirectoryRecursively(sourceSubDir, destinationSubDir, rootSourceDir, totalFileCount, ref copiedFileCount, progress);
        }

        foreach (var sourceFile in Directory.GetFiles(sourceDir))
        {
            var destinationFile = IOPath.Combine(destinationDir, IOPath.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);

            copiedFileCount++;
            int percent = totalFileCount <= 0
                ? 90
                : Math.Min(90, (int)Math.Round((copiedFileCount * 90.0) / totalFileCount));
            string relativePath = IOPath.GetRelativePath(rootSourceDir, sourceFile);

            progress?.Report(new InferenceBatchImportProgressInfo
            {
                Percent = percent,
                Status = $"배치 복사 중... ({copiedFileCount}/{Math.Max(totalFileCount, copiedFileCount)})",
                LogLine = relativePath
            });
        }
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
}

public class InferenceBatchImportResult
{
    public string ImportedPath { get; set; } = "";
    public int ItemCount { get; set; }
    public string BatchId { get; set; } = "";
}
