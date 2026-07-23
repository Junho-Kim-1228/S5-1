using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoilTrainingUI.Services;

public sealed class InferencePackageDeploymentResult
{
    public string TargetDirectory { get; init; } = "";
    public string BackupDirectory { get; init; } = "";
}

public sealed class InferencePackageDeploymentService
{
    public InferencePackageDeploymentResult Deploy(string sourcePackageDirectory, string targetPackageDirectory)
    {
        string source = Path.GetFullPath(sourcePackageDirectory);
        string target = Path.GetFullPath(targetPackageDirectory);

        ValidatePackageOrThrow(source);
        ValidateTargetOrThrow(source, target);

        string? parent = Directory.GetParent(target)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException("배포 대상의 상위 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(parent);

        string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string staging = Path.Combine(parent, Path.GetFileName(target) + ".deploying-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(parent, Path.GetFileName(target) + ".backup-" + suffix);
        bool targetMovedToBackup = false;

        try
        {
            CopyDirectory(source, staging);
            ValidatePackageOrThrow(staging);

            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                targetMovedToBackup = true;
            }

            Directory.Move(staging, target);
            return new InferencePackageDeploymentResult
            {
                TargetDirectory = target,
                BackupDirectory = targetMovedToBackup ? backup : ""
            };
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (targetMovedToBackup && !Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }
    }

    public void ValidatePackageOrThrow(string packageDirectory)
    {
        string packageRoot = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(packageRoot))
            throw new DirectoryNotFoundException($"추론 패키지를 찾을 수 없습니다: {packageRoot}");

        string pipelinePath = Path.Combine(packageRoot, "config", "pipeline.json");
        if (!File.Exists(pipelinePath))
            throw new FileNotFoundException("추론 패키지에 config/pipeline.json이 없습니다.", pipelinePath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(pipelinePath));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("pipeline", out JsonElement pipeline)
            || !pipeline.TryGetProperty("required_models", out JsonElement requiredModels)
            || requiredModels.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("pipeline.json에 pipeline.required_models 배열이 없습니다.");
        }

        var models = new List<string>();
        foreach (JsonElement item in requiredModels.EnumerateArray())
        {
            string? model = item.GetString();
            if (!string.IsNullOrWhiteSpace(model))
                models.Add(model.Trim().ToLowerInvariant());
        }
        if (models.Count == 0)
            throw new InvalidDataException("pipeline.json에 필요한 모델이 지정되지 않았습니다.");

        foreach (string model in models.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!root.TryGetProperty(model, out JsonElement section)
                || !section.TryGetProperty("model", out JsonElement modelPathElement))
            {
                throw new InvalidDataException($"pipeline.json에 {model}.model이 없습니다.");
            }

            string relativeModelPath = modelPathElement.GetString() ?? "";
            string modelPath = ResolvePackageFile(packageRoot, relativeModelPath);
            if (!File.Exists(modelPath) || new FileInfo(modelPath).Length == 0)
                throw new FileNotFoundException($"필수 모델 파일이 없거나 비어 있습니다: {relativeModelPath}", modelPath);
        }

        if (models.Contains("yolo"))
        {
            JsonElement yolo = root.GetProperty("yolo");
            if (!yolo.TryGetProperty("imgsz", out JsonElement imageSize)
                || !imageSize.TryGetInt32(out int parsedSize)
                || parsedSize <= 0)
            {
                throw new InvalidDataException("pipeline.json의 yolo.imgsz가 올바르지 않습니다.");
            }
        }
    }

    private static void ValidateTargetOrThrow(string source, string target)
    {
        if (!Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Equals("InferencePackage", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("배포 대상으로 이름이 InferencePackage인 폴더를 선택하세요.");
        }
        if (File.Exists(target))
            throw new IOException($"배포 대상이 폴더가 아닌 파일입니다: {target}");
        if (IsSameOrNested(source, target) || IsSameOrNested(target, source))
            throw new InvalidOperationException("원본 패키지와 같거나 서로 포함된 폴더에는 배포할 수 없습니다.");
    }

    private static string ResolvePackageFile(string packageRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"패키지 모델 경로가 올바르지 않습니다: {relativePath}");

        string resolved = Path.GetFullPath(Path.Combine(
            packageRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrNested(packageRoot, resolved))
            throw new InvalidDataException($"패키지 외부를 가리키는 모델 경로입니다: {relativePath}");
        return resolved;
    }

    private static bool IsSameOrNested(string parent, string candidate)
    {
        string parentPath = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidatePath = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidatePath.Equals(parentPath, StringComparison.OrdinalIgnoreCase)
               || candidatePath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination)
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
            string destinationFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: false);
        }
    }
}
