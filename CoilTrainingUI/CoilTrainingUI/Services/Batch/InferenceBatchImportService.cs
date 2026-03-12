using IOPath = System.IO.Path;
using System;
using System.IO;

namespace CoilTrainingUI.Services;

public class InferenceBatchImportService
{
    private static readonly string DefaultInboxFolder = "training_inbox";

    public InferenceBatchImportResult Import(string sourceBatchFolder, string projectRoot, string? inboxRoot = null)
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

        var destinationFolder = GetUniqueImportFolder(resolvedInboxRoot, batchId);
        CopyDirectoryRecursively(sourceBatchFolder, destinationFolder);

        var validation = BatchFolderValidationService.Validate(destinationFolder);
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

}

public class InferenceBatchImportResult
{
    public string ImportedPath { get; set; } = "";
    public int ItemCount { get; set; }
    public string BatchId { get; set; } = "";
}
