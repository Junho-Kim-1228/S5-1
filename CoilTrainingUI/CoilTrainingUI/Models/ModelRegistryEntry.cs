using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Models;

public sealed class ModelRegistryEntry
{
    public string Id { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string Status { get; set; } = ModelLifecycleStatus.Candidate;
    public string PipelineMode { get; set; } = "";
    public string RunDirectory { get; set; } = "";
    public string InferencePackageDirectory { get; set; } = "";
    public string ParentModelId { get; set; } = "";
    public string ParentWeightsPath { get; set; } = "";
    public string ParentWeightsSha256 { get; set; } = "";
    public string TrainingMode { get; set; } = "fresh";
    public List<string> SourceBatches { get; set; } = new();
    public int TotalImages { get; set; }
    public int NormalImages { get; set; }

    public string YoloModel { get; set; } = "";
    public string YoloBestPtPath { get; set; } = "";
    public string YoloOnnxPath { get; set; } = "";
    public double? YoloPrecision { get; set; }
    public double? YoloRecall { get; set; }
    public double? YoloMap50 { get; set; }
    public double? YoloMap5095 { get; set; }

    public string AnomaModel { get; set; } = "";
    public string AnomaOnnxPath { get; set; } = "";
    public string AnomaStatePath { get; set; } = "";
    public double? AnomaAuroc { get; set; }
    public double? AnomaAp { get; set; }
    public double? AnomaF1 { get; set; }
    public double? AnomaPrecision { get; set; }
    public double? AnomaRecall { get; set; }

    public string CreatedAtText => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string StatusText => Status switch
    {
        ModelLifecycleStatus.Reference => "대표",
        ModelLifecycleStatus.LegacyProduction => "대표",
        ModelLifecycleStatus.Archived => "보관",
        _ => "후보"
    };
    public string ModelsText => string.Join(" + ", new[]
    {
        string.IsNullOrWhiteSpace(AnomaModel) ? null : AnomaModel,
        string.IsNullOrWhiteSpace(YoloModel) ? null : YoloModel
    }.Where(value => value != null));
    public string YoloMetricsText => YoloMap50.HasValue
        ? $"mAP50 {YoloMap50:0.000} / mAP50-95 {YoloMap5095:0.000} / P {YoloPrecision:0.000} / R {YoloRecall:0.000}"
        : "-";
    public string AnomaMetricsText => AnomaAuroc.HasValue
        ? $"AUROC {AnomaAuroc:0.000} / AP {AnomaAp:0.000} / F1 {AnomaF1:0.000}"
        : "-";
    public string ParentText => string.IsNullOrWhiteSpace(ParentModelId)
        ? (string.Equals(TrainingMode, "fine_tune", StringComparison.OrdinalIgnoreCase) ? "외부 best.pt" : "-")
        : ParentModelId;
    public string SourceBatchesText => SourceBatches.Count == 0 ? "-" : string.Join(", ", SourceBatches);
    public bool HasYoloCheckpoint => !string.IsNullOrWhiteSpace(YoloBestPtPath) && File.Exists(YoloBestPtPath);
}

public static class ModelLifecycleStatus
{
    public const string Candidate = "candidate";
    public const string Reference = "reference";
    public const string LegacyProduction = "production";
    public const string Archived = "archived";
}

public sealed class ModelRegistrationContext
{
    public string RunDirectory { get; init; } = "";
    public string InferencePackageDirectory { get; init; } = "";
    public string PipelineMode { get; init; } = "";
    public string TrainingMode { get; init; } = "fresh";
    public string ParentModelId { get; init; } = "";
    public string ParentWeightsPath { get; init; } = "";
    public string ParentWeightsSha256 { get; init; } = "";
    public IReadOnlyList<string> SourceBatches { get; init; } = Array.Empty<string>();
    public int TotalImages { get; init; }
    public int NormalImages { get; init; }
    public string YoloModel { get; init; } = "";
    public string AnomaModel { get; init; } = "";
    public string YoloOutDirectory { get; init; } = "";
    public string AnomaOutDirectory { get; init; } = "";
}
