using CoilTrainingUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoilTrainingUI.Services;

public sealed class ModelRegistryService
{
    private sealed class RegistryDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<ModelRegistryEntry> Models { get; set; } = new();
    }

    private readonly object _sync = new();
    private readonly string _registryDirectory;
    private readonly string _registryPath;
    private readonly string _referencePath;

    public ModelRegistryService(string registryDirectory)
    {
        _registryDirectory = Path.GetFullPath(registryDirectory);
        _registryPath = Path.Combine(_registryDirectory, "models.json");
        _referencePath = Path.Combine(_registryDirectory, "reference.json");
    }

    public string RegistryPath => _registryPath;
    public string ReferencePointerPath => _referencePath;

    public IReadOnlyList<ModelRegistryEntry> Load()
    {
        lock (_sync)
        {
            return LoadDocument().Models
                .OrderByDescending(model => model.CreatedAtUtc)
                .ToList();
        }
    }

    public ModelRegistryEntry Register(ModelRegistrationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.RunDirectory))
            throw new ArgumentException("Run directory is required.", nameof(context));

        var entry = BuildEntry(context);
        lock (_sync)
        {
            RegistryDocument document = LoadDocument();
            document.Models.RemoveAll(model => string.Equals(model.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            document.Models.Add(entry);
            SaveDocument(document);
        }
        return entry;
    }

    public void SetReference(string modelId)
    {
        ModelRegistryEntry selected = Update(document =>
        {
            ModelRegistryEntry selectedModel = document.Models.FirstOrDefault(model =>
                string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Model not found: {modelId}");

            foreach (ModelRegistryEntry model in document.Models.Where(model =>
                         IsReferenceStatus(model.Status)))
            {
                model.Status = ModelLifecycleStatus.Candidate;
            }
            selectedModel.Status = ModelLifecycleStatus.Reference;
            return selectedModel;
        });
        WriteReferencePointer(selected);
    }

    public void Archive(string modelId)
    {
        bool archivedReference = Update(document =>
        {
            ModelRegistryEntry selected = document.Models.FirstOrDefault(model =>
                string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Model not found: {modelId}");
            bool wasReference = IsReferenceStatus(selected.Status);
            selected.Status = ModelLifecycleStatus.Archived;
            return wasReference;
        });
        if (archivedReference && File.Exists(_referencePath))
            File.Delete(_referencePath);
    }

    public ModelRegistryEntry? Find(string modelId) =>
        Load().FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));

    private T Update<T>(Func<RegistryDocument, T> update)
    {
        lock (_sync)
        {
            RegistryDocument document = LoadDocument();
            T result = update(document);
            SaveDocument(document);
            return result;
        }
    }

    private void WriteReferencePointer(ModelRegistryEntry selected)
    {
        Directory.CreateDirectory(_registryDirectory);
        string temporaryPath = _referencePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                new
                {
                    SchemaVersion = 1,
                    ModelId = selected.Id,
                    InferencePackageDirectory = selected.InferencePackageDirectory,
                    ActivatedAtUtc = DateTime.UtcNow
                },
                new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _referencePath, overwrite: true);
    }

    private ModelRegistryEntry BuildEntry(ModelRegistrationContext context)
    {
        string runDirectory = Path.GetFullPath(context.RunDirectory);
        string id = Path.GetFileName(runDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var entry = new ModelRegistryEntry
        {
            Id = id,
            CreatedAtUtc = Directory.Exists(runDirectory)
                ? Directory.GetCreationTimeUtc(runDirectory)
                : DateTime.UtcNow,
            Status = ModelLifecycleStatus.Candidate,
            PipelineMode = context.PipelineMode,
            RunDirectory = runDirectory,
            InferencePackageDirectory = FullPathOrEmpty(context.InferencePackageDirectory),
            ParentModelId = context.ParentModelId,
            ParentWeightsPath = FullPathOrEmpty(context.ParentWeightsPath),
            ParentWeightsSha256 = context.ParentWeightsSha256,
            TrainingMode = context.TrainingMode,
            SourceBatches = context.SourceBatches.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            TotalImages = context.TotalImages,
            NormalImages = context.NormalImages,
            YoloModel = context.YoloModel,
            AnomaModel = context.AnomaModel,
            YoloBestPtPath = FirstExistingArtifact(
                ArtifactPath(context.InferencePackageDirectory, "training", "yolo_best.pt"),
                ArtifactPath(context.YoloOutDirectory, "best.pt")),
            YoloOnnxPath = FirstExistingArtifact(
                ArtifactPath(context.InferencePackageDirectory, "models", "yolo.onnx"),
                ArtifactPath(context.YoloOutDirectory, "yolo.onnx")),
            AnomaOnnxPath = FirstExistingArtifact(
                ArtifactPath(context.InferencePackageDirectory, "models", "anoma.onnx"),
                ArtifactPath(context.AnomaOutDirectory, "anoma.onnx")),
            AnomaStatePath = FindAnomaState(context.AnomaOutDirectory)
        };

        ReadYoloMetrics(Path.Combine(context.YoloOutDirectory, "train_summary.json"), entry);
        ReadAnomaMetrics(Path.Combine(context.AnomaOutDirectory, "train_summary.json"), entry);
        return entry;
    }

    private RegistryDocument LoadDocument()
    {
        if (!File.Exists(_registryPath))
            return new RegistryDocument();

        try
        {
            RegistryDocument document = JsonSerializer.Deserialize<RegistryDocument>(File.ReadAllText(_registryPath))
                                        ?? new RegistryDocument();
            foreach (ModelRegistryEntry model in document.Models)
            {
                if (string.Equals(
                        model.Status,
                        ModelLifecycleStatus.LegacyProduction,
                        StringComparison.OrdinalIgnoreCase))
                {
                    model.Status = ModelLifecycleStatus.Reference;
                }

                model.YoloBestPtPath = FirstExistingArtifact(
                    ArtifactPath(model.InferencePackageDirectory, "training", "yolo_best.pt"),
                    model.YoloBestPtPath,
                    ArtifactPath(model.RunDirectory, "yolo_out", "best.pt"));
            }
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Model registry is invalid: {_registryPath}", ex);
        }
    }

    private void SaveDocument(RegistryDocument document)
    {
        Directory.CreateDirectory(_registryDirectory);
        string temporaryPath = _registryPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _registryPath, overwrite: true);
    }

    private static string FirstExistingArtifact(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            string path = Path.GetFullPath(candidate);
            if (File.Exists(path))
                return path;
        }
        return "";
    }

    private static string ArtifactPath(string? directory, params string[] relativeParts)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "";
        return Path.Combine(new[] { directory }.Concat(relativeParts).ToArray());
    }

    private static bool IsReferenceStatus(string status) =>
        string.Equals(status, ModelLifecycleStatus.Reference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, ModelLifecycleStatus.LegacyProduction, StringComparison.OrdinalIgnoreCase);

    private static string FindAnomaState(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return "";
        return Directory.GetFiles(directory, "*_state.pt", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? "";
    }

    private static string FullPathOrEmpty(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);

    private static void ReadYoloMetrics(string summaryPath, ModelRegistryEntry entry)
    {
        if (!File.Exists(summaryPath))
            return;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summaryPath));
        if (!document.RootElement.TryGetProperty("metrics", out JsonElement metrics))
            return;
        entry.YoloPrecision = ReadDouble(metrics, "precision");
        entry.YoloRecall = ReadDouble(metrics, "recall");
        entry.YoloMap50 = ReadDouble(metrics, "map50");
        entry.YoloMap5095 = ReadDouble(metrics, "map");
    }

    private static void ReadAnomaMetrics(string summaryPath, ModelRegistryEntry entry)
    {
        if (!File.Exists(summaryPath))
            return;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summaryPath));
        if (!document.RootElement.TryGetProperty("metrics", out JsonElement metrics))
            return;
        entry.AnomaAuroc = ReadDouble(metrics, "image_auroc");
        entry.AnomaAp = ReadDouble(metrics, "image_ap");
        entry.AnomaF1 = ReadDouble(metrics, "best_f1");
        entry.AnomaPrecision = ReadDouble(metrics, "best_precision");
        entry.AnomaRecall = ReadDouble(metrics, "best_recall");
    }

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double parsed)
            ? parsed
            : null;
}
