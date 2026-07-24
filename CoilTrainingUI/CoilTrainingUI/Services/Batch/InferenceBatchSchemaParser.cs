using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services;

public static class InferenceBatchSchemaParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ManifestDto ParseManifest(string manifestPath)
    {
        EnsurePathExists(manifestPath, "manifest.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        ValidateManifestDocument(doc.RootElement, manifestPath);

        return JsonSerializer.Deserialize<ManifestDto>(doc.RootElement.GetRawText(), JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize manifest.json: {manifestPath}");
    }

    public static InferResultDto ParseInferResult(string inferResultPath)
    {
        EnsurePathExists(inferResultPath, "infer.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(inferResultPath));
        ValidateInferResultDocument(doc.RootElement, inferResultPath);

        return JsonSerializer.Deserialize<InferResultDto>(doc.RootElement.GetRawText(), JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize infer.json: {inferResultPath}");
    }

    private static void EnsurePathExists(string path, string fileType)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{fileType} path is empty.");

        if (!File.Exists(path))
            throw new FileNotFoundException($"{fileType} not found: {path}");
    }

    private static JsonElement GetRequiredProperty(JsonElement obj, string parentPath, string propertyName, string filePath)
    {
        if (!obj.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidDataException(
                $"Required field missing: '{parentPath}.{propertyName}' in {filePath}");
        }

        return prop;
    }

    private static void ValidateManifestDocument(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Invalid manifest root type: expected object in {path}.");

        var schemaVersionElement = GetRequiredProperty(root, "manifest", "schema_version", path);
        if (!schemaVersionElement.TryGetInt32(out int schemaVersion))
            throw new InvalidDataException($"Invalid type for 'manifest.schema_version' (integer expected): {path}");
        _ = GetRequiredProperty(root, "manifest", "created_at", path);

        if (schemaVersion >= 3)
            ValidateInferenceContext(root, path);

        var items = GetRequiredProperty(root, "manifest", "items", path);
        if (items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Invalid type for 'manifest.items' (array expected): {path}");

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Invalid item type in 'manifest.items' (object expected): {path}");

            _ = GetRequiredProperty(item, "manifest.items[]", "processed_image", path);
            _ = GetRequiredProperty(item, "manifest.items[]", "id", path);
        }
    }

    private static void ValidateInferResultDocument(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Invalid infer json root type: expected object in {path}.");

        var schemaVersionElement = GetRequiredProperty(root, "infer", "schema_version", path);
        if (!schemaVersionElement.TryGetInt32(out int schemaVersion))
            throw new InvalidDataException($"Invalid type for 'infer.schema_version' (integer expected): {path}");
        _ = GetRequiredProperty(root, "infer", "image_id", path);
        _ = GetRequiredProperty(root, "infer", "image_size", path);
        _ = GetRequiredProperty(root, "infer", "yolo", path);
        _ = GetRequiredProperty(root, "infer", "anoma", path);
        _ = GetRequiredProperty(root, "infer", "final", path);

        var imageSize = GetRequiredProperty(root.GetProperty("image_size"), "infer.image_size", "w", path);
        if (!imageSize.TryGetInt32(out _))
            throw new InvalidDataException($"Invalid type for 'infer.image_size.w' (integer expected): {path}");

        imageSize = GetRequiredProperty(root.GetProperty("image_size"), "infer.image_size", "h", path);
        if (!imageSize.TryGetInt32(out _))
            throw new InvalidDataException($"Invalid type for 'infer.image_size.h' (integer expected): {path}");

        var yolo = GetRequiredProperty(root, "infer", "yolo", path);
        var detections = GetRequiredProperty(yolo, "infer.yolo", "detections", path);
        if (detections.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Invalid type for 'infer.yolo.detections' (array expected): {path}");

        var index = 0;
        foreach (var detection in detections.EnumerateArray())
        {
            if (detection.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Invalid type for infer.yolo.detections[{index}] (object expected): {path}");

            _ = GetRequiredProperty(detection, $"infer.yolo.detections[{index}]", "class_name", path);
            _ = GetRequiredProperty(detection, $"infer.yolo.detections[{index}]", "conf", path);
            var bbox = GetRequiredProperty(detection, $"infer.yolo.detections[{index}]", "bbox_xywh_norm", path);

            if (bbox.ValueKind != JsonValueKind.Array || bbox.GetArrayLength() != 4)
            {
                throw new InvalidDataException(
                    $"Invalid field 'infer.yolo.detections[{index}].bbox_xywh_norm' (array length 4 expected): {path}");
            }

            index++;
        }

        var final = GetRequiredProperty(root, "infer", "final", path);
        var finalIsDefect = GetRequiredProperty(final, "infer.final", "is_defect", path);
        if (finalIsDefect.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Invalid type for 'infer.final.is_defect' (boolean expected): {path}");

        if (schemaVersion >= 2)
        {
            _ = GetRequiredProperty(root, "infer", "inference_context_id", path);
            _ = GetRequiredProperty(yolo, "infer.yolo", "confidence_threshold", path);
            _ = GetRequiredProperty(yolo, "infer.yolo", "model_sha256", path);
            var yoloExecuted = GetRequiredProperty(yolo, "infer.yolo", "executed", path);
            if (yoloExecuted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"Invalid type for 'infer.yolo.executed' (boolean expected): {path}");

            var anoma = GetRequiredProperty(root, "infer", "anoma", path);
            var anomaExecuted = GetRequiredProperty(anoma, "infer.anoma", "executed", path);
            if (anomaExecuted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"Invalid type for 'infer.anoma.executed' (boolean expected): {path}");
            var anomaScore = GetRequiredProperty(anoma, "infer.anoma", "score", path);
            if (!anomaScore.TryGetDouble(out _))
                throw new InvalidDataException($"Invalid type for 'infer.anoma.score' (number expected): {path}");
            var scoreThreshold = GetRequiredProperty(anoma, "infer.anoma", "score_threshold", path);
            if (!scoreThreshold.TryGetDouble(out _))
                throw new InvalidDataException($"Invalid type for 'infer.anoma.score_threshold' (number expected): {path}");
            var anomaDecision = GetRequiredProperty(anoma, "infer.anoma", "decision", path);
            if (anomaDecision.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Invalid type for 'infer.anoma.decision' (string expected): {path}");
            _ = GetRequiredProperty(anoma, "infer.anoma", "model_sha256", path);
        }
    }

    private static void ValidateInferenceContext(JsonElement root, string path)
    {
        var context = GetRequiredProperty(root, "manifest", "inference_context", path);
        if (context.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Invalid type for 'manifest.inference_context' (object expected): {path}");

        var statusElement = GetRequiredProperty(context, "manifest.inference_context", "status", path);
        string status = statusElement.GetString()?.Trim().ToLowerInvariant() ?? "";
        if (status != "recorded")
            return;

        var contextIdElement = GetRequiredProperty(context, "manifest.inference_context", "context_id", path);
        if (contextIdElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(contextIdElement.GetString()))
        {
            throw new InvalidDataException(
                $"Invalid 'manifest.inference_context.context_id' (non-empty string expected): {path}");
        }
        _ = GetRequiredProperty(context, "manifest.inference_context", "pipeline_mode", path);
        _ = GetRequiredProperty(context, "manifest.inference_context", "package_fingerprint", path);
        _ = GetRequiredProperty(context, "manifest.inference_context", "pipeline_sha256", path);
    }
}
