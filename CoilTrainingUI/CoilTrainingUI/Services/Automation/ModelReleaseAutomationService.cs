using CoilTrainingUI.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoilTrainingUI.Services.Automation;

public sealed class ModelPublishResult
{
    public string ModelId { get; init; } = "";
    public string ReleaseDirectory { get; init; } = "";
    public string PackageDirectory { get; init; } = "";
    public string PackageHash { get; init; } = "";
    public bool AlreadyPublished { get; init; }
}

public sealed class ModelReleasePublisher
{
    private readonly string _exchangeRoot;
    private readonly InferencePackageDeploymentService _validator;

    public ModelReleasePublisher(
        string exchangeRoot,
        InferencePackageDeploymentService? validator = null)
    {
        _exchangeRoot = AutomationPaths.NormalizeExchangeRoot(exchangeRoot);
        _validator = validator ?? new InferencePackageDeploymentService();
    }

    public ModelPublishResult Publish(ModelRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!string.Equals(entry.PipelineMode, InferencePipelineConfigBuilder.AnomaThenYolo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("전체 Anoma → YOLO 파이프라인 모델만 자동 발행할 수 있습니다.");
        if (string.IsNullOrWhiteSpace(entry.Id))
            throw new InvalidOperationException("모델 ID가 없습니다.");

        AutomationPaths.EnsureLayout(_exchangeRoot);
        using InterprocessFileLock publishLock = InterprocessFileLock.Acquire(
            Path.Combine(AutomationPaths.Locks(_exchangeRoot), "model-publish.lock"),
            TimeSpan.FromSeconds(5));

        string sourcePackage = Path.GetFullPath(entry.InferencePackageDirectory);
        _validator.ValidatePackageOrThrow(sourcePackage);
        string sourceHash = AutomationHash.PackageSha256(sourcePackage);
        string releaseDirectory = Path.Combine(AutomationPaths.Releases(_exchangeRoot), entry.Id);
        string finalPackage = Path.Combine(releaseDirectory, "InferencePackage");
        string manifestPath = Path.Combine(releaseDirectory, "release.json");

        if (Directory.Exists(finalPackage) || File.Exists(manifestPath))
        {
            if (!Directory.Exists(finalPackage) || !File.Exists(manifestPath))
                throw new IOException($"불완전한 기존 릴리스가 있습니다: {releaseDirectory}");
            ModelReleaseManifest existing = ReadManifest(manifestPath);
            string existingHash = AutomationHash.PackageSha256(finalPackage);
            if (!string.Equals(existing.ModelId, entry.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.PackageHash, sourceHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existingHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"같은 model-id에 다른 패키지가 이미 발행되어 있습니다: {entry.Id}");
            }
            return Result(entry.Id, releaseDirectory, finalPackage, sourceHash, alreadyPublished: true);
        }

        Directory.CreateDirectory(releaseDirectory);
        string staging = Path.Combine(releaseDirectory, "InferencePackage.deploying-" + Guid.NewGuid().ToString("N"));
        string temporaryManifest = manifestPath + ".deploying-" + Guid.NewGuid().ToString("N");
        bool finalPackageMoved = false;
        try
        {
            CopyDirectory(sourcePackage, staging);
            _validator.ValidatePackageOrThrow(staging);
            string stagingHash = AutomationHash.PackageSha256(staging);
            if (!string.Equals(stagingHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("발행 staging 복사본의 패키지 해시가 원본과 다릅니다.");

            var manifest = new ModelReleaseManifest
            {
                ModelId = entry.Id,
                PackageHash = stagingHash,
                SourceRun = entry.RunDirectory,
                PipelineMode = entry.PipelineMode,
                CreatedAtUtc = DateTime.UtcNow
            };
            AtomicJsonFile.Write(temporaryManifest, manifest);
            Directory.Move(staging, finalPackage);
            finalPackageMoved = true;
            File.Move(temporaryManifest, manifestPath, overwrite: false);
            return Result(entry.Id, releaseDirectory, finalPackage, stagingHash, alreadyPublished: false);
        }
        catch
        {
            TryDeleteDirectory(staging);
            if (finalPackageMoved && !File.Exists(manifestPath))
                TryDeleteDirectory(finalPackage);
            if (File.Exists(temporaryManifest))
                File.Delete(temporaryManifest);
            throw;
        }
    }

    public ModelReleaseManifest GetRelease(string modelId)
    {
        string releaseDirectory = Path.Combine(AutomationPaths.Releases(_exchangeRoot), modelId);
        string manifestPath = Path.Combine(releaseDirectory, "release.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("발행된 release.json이 없습니다.", manifestPath);
        ModelReleaseManifest manifest = ReadManifest(manifestPath);
        string packagePath = Path.Combine(releaseDirectory, "InferencePackage");
        _validator.ValidatePackageOrThrow(packagePath);
        string hash = AutomationHash.PackageSha256(packagePath);
        if (!string.Equals(hash, manifest.PackageHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("발행 패키지 해시가 release.json과 다릅니다.");
        return manifest;
    }

    private static ModelPublishResult Result(
        string modelId,
        string releaseDirectory,
        string packageDirectory,
        string hash,
        bool alreadyPublished) => new()
    {
        ModelId = modelId,
        ReleaseDirectory = releaseDirectory,
        PackageDirectory = packageDirectory,
        PackageHash = hash,
        AlreadyPublished = alreadyPublished
    };

    private static ModelReleaseManifest ReadManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ModelReleaseManifest>(File.ReadAllText(path))
                   ?? throw new InvalidDataException("release.json이 비어 있습니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("release.json을 읽을 수 없습니다.", ex);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}

public sealed class PendingActivationRequestException : InvalidOperationException
{
    public PendingActivationRequestException(string message) : base(message) { }
}

public sealed class ActivationRequestService
{
    private readonly string _exchangeRoot;
    private readonly ModelReleasePublisher _publisher;

    public ActivationRequestService(string exchangeRoot, ModelReleasePublisher? publisher = null)
    {
        _exchangeRoot = AutomationPaths.NormalizeExchangeRoot(exchangeRoot);
        _publisher = publisher ?? new ModelReleasePublisher(_exchangeRoot);
    }

    public ActivationRequest Create(string modelId)
    {
        AutomationPaths.EnsureLayout(_exchangeRoot);
        using InterprocessFileLock requestLock = InterprocessFileLock.Acquire(
            Path.Combine(AutomationPaths.Locks(_exchangeRoot), "activation-request.lock"),
            TimeSpan.FromSeconds(5));

        ActivationRequest? existing = TryReadRequest();
        if (existing != null && IsPending(existing))
        {
            throw new PendingActivationRequestException(
                $"모델 {existing.ModelId}의 운영 적용 요청({existing.RequestId})이 아직 대기 중입니다. " +
                "기존 요청을 명시적으로 취소한 뒤 다시 요청하세요.");
        }

        ModelReleaseManifest release = _publisher.GetRelease(modelId);
        string relativePackage = Path.Combine(modelId, "InferencePackage").Replace('\\', '/');
        string packagePath = AutomationPaths.ResolveReleasePackagePath(_exchangeRoot, relativePackage);
        string hash = AutomationHash.PackageSha256(packagePath);
        if (!string.Equals(hash, release.PackageHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("운영 적용 요청 전 릴리스 패키지 해시 검증에 실패했습니다.");

        var request = new ActivationRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ModelId = modelId,
            PackageRelativePath = relativePackage,
            PackageHash = hash,
            RequestedAtUtc = DateTime.UtcNow
        };
        AtomicJsonFile.Write(AutomationPaths.ActivationRequest(_exchangeRoot), request);
        return request;
    }

    public bool CancelPending(out string message)
    {
        AutomationPaths.EnsureLayout(_exchangeRoot);
        using InterprocessFileLock requestLock = InterprocessFileLock.Acquire(
            Path.Combine(AutomationPaths.Locks(_exchangeRoot), "activation-request.lock"),
            TimeSpan.FromSeconds(5));
        ActivationRequest? existing = TryReadRequest();
        if (existing == null || !IsPending(existing))
        {
            message = "취소할 대기 요청이 없습니다.";
            return false;
        }

        string archiveDirectory = Path.Combine(AutomationPaths.Control(_exchangeRoot), "cancelled");
        Directory.CreateDirectory(archiveDirectory);
        string requestPath = AutomationPaths.ActivationRequest(_exchangeRoot);
        string archivePath = Path.Combine(archiveDirectory, existing.RequestId + ".json");
        File.Move(requestPath, archivePath, overwrite: false);
        message = $"운영 적용 요청을 취소했습니다: {existing.ModelId}";
        return true;
    }

    public ActivationRequest? TryReadRequest()
    {
        string path = AutomationPaths.ActivationRequest(_exchangeRoot);
        if (!File.Exists(path))
            return null;
        try
        {
            ActivationRequest? request = JsonSerializer.Deserialize<ActivationRequest>(File.ReadAllText(path));
            if (request == null || request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.RequestId))
                throw new InvalidDataException("activation_request.json 형식이 올바르지 않습니다.");
            AutomationPaths.ResolveReleasePackagePath(_exchangeRoot, request.PackageRelativePath);
            return request;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("activation_request.json을 읽을 수 없습니다.", ex);
        }
    }

    public ActivationResult? TryReadResult()
    {
        string path = AutomationPaths.ActivationResult(_exchangeRoot);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ActivationResult>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("activation_result.json을 읽을 수 없습니다.", ex);
        }
    }

    private bool IsPending(ActivationRequest request)
    {
        ActivationResult? result = TryReadResult();
        if (result == null || !string.Equals(result.RequestId, request.RequestId, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(result.Status, "pending", StringComparison.OrdinalIgnoreCase) ||
               (!string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ActivationResultSynchronizer
{
    private readonly ActivationRequestService _requests;
    private readonly ModelRegistryService _registry;

    public ActivationResultSynchronizer(ActivationRequestService requests, ModelRegistryService registry)
    {
        _requests = requests;
        _registry = registry;
    }

    public ActivationResult? Reconcile()
    {
        ActivationRequest? request = _requests.TryReadRequest();
        ActivationResult? result = _requests.TryReadResult();
        if (request == null || result == null ||
            !string.Equals(request.RequestId, result.RequestId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.ModelId, result.ModelId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.PackageHash, result.PackageHash, StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        if (string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase))
        {
            ModelRegistryEntry model = _registry.Find(result.ModelId)
                ?? throw new InvalidOperationException($"적용 결과의 모델을 레지스트리에서 찾을 수 없습니다: {result.ModelId}");
            if (!string.Equals(model.Status, ModelLifecycleStatus.Reference, StringComparison.OrdinalIgnoreCase))
                _registry.SetReference(result.ModelId);
        }
        return result;
    }
}
