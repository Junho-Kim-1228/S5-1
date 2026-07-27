using CoilTrainingUI.Models;
using CoilTrainingUI.Services.Automation;
using System;
using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services;

public sealed class ExistingPackageImportResult
{
    public ModelRegistryEntry Model { get; init; } = new();
    public string PackageHash { get; init; } = "";
    public bool AlreadyImported { get; init; }
}

public sealed class ExistingInferencePackageImportService
{
    private sealed class ImportManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string ModelId { get; set; } = "";
        public string PackageHash { get; set; } = "";
        public string SourcePackage { get; set; } = "";
        public DateTime ImportedAtUtc { get; set; }
    }

    private readonly string _managedRoot;
    private readonly ModelRegistryService _registry;
    private readonly InferencePackageDeploymentService _validator;

    public ExistingInferencePackageImportService(
        string managedRoot,
        ModelRegistryService registry,
        InferencePackageDeploymentService? validator = null)
    {
        _managedRoot = Path.GetFullPath(managedRoot ?? throw new ArgumentNullException(nameof(managedRoot)));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? new InferencePackageDeploymentService();
    }

    public ExistingPackageImportResult ImportCurrentOperationalPackage(string sourcePackageDirectory)
    {
        string source = Path.GetFullPath(
            sourcePackageDirectory ?? throw new ArgumentNullException(nameof(sourcePackageDirectory)));
        _validator.ValidatePackageOrThrow(source);

        string sourceHash = AutomationHash.PackageSha256(source);
        string modelId = "imported_operational_" + sourceHash[..12];
        string finalRunDirectory = Path.Combine(_managedRoot, modelId);
        string finalPackage = Path.Combine(finalRunDirectory, "InferencePackage");
        string importManifestPath = Path.Combine(finalRunDirectory, "import_manifest.json");
        bool alreadyImported = Directory.Exists(finalRunDirectory);

        if (alreadyImported)
        {
            ValidateExistingImport(finalPackage, importManifestPath, modelId, sourceHash);
        }
        else
        {
            ImportImmutableCopy(source, finalRunDirectory, modelId, sourceHash);
        }

        ModelRegistryEntry registered = _registry.Register(new ModelRegistrationContext
        {
            RunDirectory = finalRunDirectory,
            InferencePackageDirectory = finalPackage,
            PipelineMode = InferencePipelineConfigBuilder.AnomaThenYolo,
            TrainingMode = "imported",
            YoloModel = "기존 yolo.onnx",
            AnomaModel = "기존 anoma.onnx"
        });
        _registry.SetReference(registered.Id);
        _registry.SetActive(registered.Id, "existing-package-import");

        ModelRegistryEntry active = _registry.Find(registered.Id) ?? registered;
        return new ExistingPackageImportResult
        {
            Model = active,
            PackageHash = sourceHash,
            AlreadyImported = alreadyImported
        };
    }

    private void ImportImmutableCopy(
        string source,
        string finalRunDirectory,
        string modelId,
        string sourceHash)
    {
        Directory.CreateDirectory(_managedRoot);
        string stagingRun = Path.Combine(
            _managedRoot,
            ".importing-" + modelId + "-" + Guid.NewGuid().ToString("N"));
        string stagingPackage = Path.Combine(stagingRun, "InferencePackage");
        try
        {
            CopyDirectory(source, stagingPackage);
            _validator.ValidatePackageOrThrow(stagingPackage);
            string copiedHash = AutomationHash.PackageSha256(stagingPackage);
            if (!string.Equals(copiedHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("가져온 패키지의 해시가 원본과 다릅니다.");

            WriteImportManifest(
                Path.Combine(stagingRun, "import_manifest.json"),
                modelId,
                sourceHash,
                source);
            Directory.Move(stagingRun, finalRunDirectory);
        }
        catch
        {
            TryDeleteDirectory(stagingRun);
            throw;
        }
    }

    private void ValidateExistingImport(
        string packageDirectory,
        string manifestPath,
        string modelId,
        string sourceHash)
    {
        _validator.ValidatePackageOrThrow(packageDirectory);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"기존 가져오기 기록이 불완전합니다: {manifestPath}");

        ImportManifest manifest = JsonSerializer.Deserialize<ImportManifest>(File.ReadAllText(manifestPath))
                                  ?? throw new InvalidDataException("가져오기 기록이 비어 있습니다.");
        string existingHash = AutomationHash.PackageSha256(packageDirectory);
        if (!string.Equals(manifest.ModelId, modelId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.PackageHash, sourceHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existingHash, sourceHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("같은 ID로 가져온 기존 패키지가 현재 운영 패키지와 다릅니다.");
        }
    }

    private static void WriteImportManifest(
        string path,
        string modelId,
        string packageHash,
        string sourcePackage)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new ImportManifest
                {
                    ModelId = modelId,
                    PackageHash = packageHash,
                    SourcePackage = sourcePackage,
                    ImportedAtUtc = DateTime.UtcNow
                },
                new JsonSerializerOptions { WriteIndented = true }));
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
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
