using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoilTrainingUI.Services.Automation;

public sealed class ArchivedBatchTrashResult
{
    public bool Moved { get; init; }
    public string SourcePath { get; init; } = "";
    public string TrashPath { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class ArchivedBatchTrashService
{
    private readonly string _archiveRoot;
    private readonly string _trashRoot;

    public ArchivedBatchTrashService(string archiveRoot)
    {
        _archiveRoot = string.IsNullOrWhiteSpace(archiveRoot) ? "" : Path.GetFullPath(archiveRoot);
        _trashRoot = string.IsNullOrWhiteSpace(_archiveRoot) ? "" : Path.Combine(_archiveRoot, "_trash");
    }

    public ArchivedBatchTrashResult MoveMatchingBatchToTrash(string batchKey, string batchId)
    {
        if (string.IsNullOrWhiteSpace(_archiveRoot) || !Directory.Exists(_archiveRoot))
            return new ArchivedBatchTrashResult { Message = "archive 폴더가 없어 통계 원본 이동을 건너뛰었습니다." };

        List<string> candidates = FindCandidates(batchKey, batchId);
        if (candidates.Count == 0)
            return new ArchivedBatchTrashResult { Message = "대응하는 archive 통계 원본이 없습니다." };
        if (candidates.Count > 1)
            throw new InvalidOperationException(
                $"archive에서 같은 배치로 보이는 폴더가 {candidates.Count}개 발견되어 자동 이동하지 않았습니다.");

        string source = ValidateDirectCompletedBatch(candidates[0]);
        Directory.CreateDirectory(_trashRoot);
        string destination = UniqueDirectory(Path.Combine(_trashRoot, Path.GetFileName(source)));
        Directory.Move(source, destination);
        return new ArchivedBatchTrashResult
        {
            Moved = true,
            SourcePath = source,
            TrashPath = destination,
            Message = "archive 통계 원본을 휴지통으로 이동했습니다."
        };
    }

    private List<string> FindCandidates(string batchKey, string batchId)
    {
        string normalizedKey = (batchKey ?? "").Trim();
        string normalizedId = (batchId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey))
        {
            string exact = Path.Combine(_archiveRoot, normalizedKey);
            if (IsCompletedBatch(exact))
                return new List<string> { exact };
        }

        return Directory.GetDirectories(_archiveRoot, "export_batch_*")
            .Where(IsCompletedBatch)
            .Where(directory =>
                string.Equals(ReadBatchId(directory), normalizedId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ReadBatchId(directory), normalizedKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string ValidateDirectCompletedBatch(string directory)
    {
        string source = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetDirectoryName(source) ?? "";
        string archive = _archiveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(parent, archive, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("archive 폴더 밖의 경로는 이동할 수 없습니다.");
        if (!IsCompletedBatch(source))
            throw new InvalidDataException("완료된 추론 배치가 아닙니다.");
        return source;
    }

    private static bool IsCompletedBatch(string directory) =>
        Directory.Exists(directory) &&
        Path.GetFileName(directory).StartsWith("export_batch_", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(Path.Combine(directory, "meta", "DONE.flag"));

    private static string ReadBatchId(string directory)
    {
        string manifestPath = Path.Combine(directory, "meta", "manifest.json");
        if (!File.Exists(manifestPath))
            return "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return document.RootElement.TryGetProperty("batch_id", out JsonElement value)
                ? value.GetString()?.Trim() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string UniqueDirectory(string requestedPath)
    {
        if (!Directory.Exists(requestedPath))
            return requestedPath;
        string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string candidate = requestedPath + "-" + suffix;
        int index = 2;
        while (Directory.Exists(candidate))
            candidate = requestedPath + "-" + suffix + "-" + index++;
        return candidate;
    }
}
