using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Models.InferenceBatch;

public class ManifestDto
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("batch_type")]
    public string BatchType { get; set; } = "";

    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("inference_context")]
    public InferenceContextDto? InferenceContext { get; set; }

    [JsonPropertyName("items")]
    public List<ManifestItemDto> Items { get; set; } = new();
}

public class ManifestItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("processed_image")]
    public string ProcessedImage { get; set; } = "";

    [JsonPropertyName("infer_json")]
    public string InferJson { get; set; } = "";

    [JsonPropertyName("raw_image")]
    public string RawImage { get; set; } = "";
}

public class InferResultDto
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("image_id")]
    public string ImageId { get; set; } = "";

    [JsonPropertyName("inference_context_id")]
    public string InferenceContextId { get; set; } = "";

    [JsonPropertyName("image_size")]
    public ImageSizeDto ImageSize { get; set; } = new();

    [JsonPropertyName("yolo")]
    public InferYoloDto Yolo { get; set; } = new();

    [JsonPropertyName("anoma")]
    public InferAnomaDto Anoma { get; set; } = new();

    [JsonPropertyName("final")]
    public InferFinalDto Final { get; set; } = new();
}

public class ImageSizeDto
{
    [JsonPropertyName("w")]
    public int W { get; set; }

    [JsonPropertyName("h")]
    public int H { get; set; }
}

public class InferYoloDto
{
    [JsonPropertyName("executed")]
    public bool Executed { get; set; }

    [JsonPropertyName("confidence_threshold")]
    public double? ConfidenceThreshold { get; set; }

    [JsonPropertyName("model_sha256")]
    public string ModelSha256 { get; set; } = "";

    [JsonPropertyName("detections")]
    public List<DetectionDto> Detections { get; set; } = new();
}

public class DetectionDto
{
    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("conf")]
    public double Conf { get; set; }

    [JsonPropertyName("bbox_xywh_norm")]
    public double[] BboxXywhNorm { get; set; } = Array.Empty<double>();
}

public class InferAnomaDto
{
    [JsonPropertyName("executed")]
    public bool Executed { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("score_threshold")]
    public double? ScoreThreshold { get; set; }

    [JsonPropertyName("model_sha256")]
    public string ModelSha256 { get; set; } = "";

    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "";
}

public class InferFinalDto
{
    [JsonPropertyName("is_defect")]
    public bool IsDefect { get; set; }

    [JsonPropertyName("reason")]
    public List<string> Reason { get; set; } = new();
}

public class InferenceContextDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("context_id")]
    public string ContextId { get; set; } = "";

    [JsonPropertyName("captured_at_utc")]
    public string CapturedAtUtc { get; set; } = "";

    [JsonPropertyName("pipeline_mode")]
    public string PipelineMode { get; set; } = "";

    [JsonPropertyName("package_fingerprint")]
    public string PackageFingerprint { get; set; } = "";

    [JsonPropertyName("pipeline_sha256")]
    public string PipelineSha256 { get; set; } = "";

    [JsonPropertyName("mask")]
    public MaskInferenceContextDto? Mask { get; set; }

    [JsonPropertyName("anoma")]
    public AnomaInferenceContextDto? Anoma { get; set; }

    [JsonPropertyName("yolo")]
    public YoloInferenceContextDto? Yolo { get; set; }
}

public class MaskInferenceContextDto
{
    [JsonPropertyName("model_file")]
    public string ModelFile { get; set; } = "";

    [JsonPropertyName("model_sha256")]
    public string ModelSha256 { get; set; } = "";

    [JsonPropertyName("confidence_threshold")]
    public double ConfidenceThreshold { get; set; }

    [JsonPropertyName("mask_threshold")]
    public double MaskThreshold { get; set; }

    [JsonPropertyName("input_size")]
    public int InputSize { get; set; }

    [JsonPropertyName("resize_mode")]
    public string ResizeMode { get; set; } = "";
}

public class AnomaInferenceContextDto
{
    [JsonPropertyName("model_file")]
    public string ModelFile { get; set; } = "";

    [JsonPropertyName("model_sha256")]
    public string ModelSha256 { get; set; } = "";

    [JsonPropertyName("score_threshold")]
    public double ScoreThreshold { get; set; }

    [JsonPropertyName("input_size")]
    public int InputSize { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";
}

public class YoloInferenceContextDto
{
    [JsonPropertyName("model_file")]
    public string ModelFile { get; set; } = "";

    [JsonPropertyName("model_sha256")]
    public string ModelSha256 { get; set; } = "";

    [JsonPropertyName("confidence_threshold")]
    public double ConfidenceThreshold { get; set; }

    [JsonPropertyName("iou_threshold")]
    public double IouThreshold { get; set; }

    [JsonPropertyName("input_size")]
    public int InputSize { get; set; }
}
