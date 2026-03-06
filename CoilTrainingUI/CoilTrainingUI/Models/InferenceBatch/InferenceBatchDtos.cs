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
    [JsonPropertyName("score")]
    public double Score { get; set; }

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
