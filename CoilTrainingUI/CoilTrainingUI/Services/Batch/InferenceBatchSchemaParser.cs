using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

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

    public static bool RunSchemaSmokeTest(string manifestPath, string inferResultPath)
    {
        var manifest = ParseManifest(manifestPath);
        var infer = ParseInferResult(inferResultPath);

        string message =
            "Batch schema parse OK\n" +
            $"- manifest: {Path.GetFileName(manifestPath)} ({manifest.Items.Count} items)\n" +
            $"- infer: {Path.GetFileName(inferResultPath)} ({infer.Yolo.Detections.Count} detections)";

        Trace.WriteLine(message);
        MessageBox.Show(message, "Inference Batch Schema", MessageBoxButton.OK, MessageBoxImage.Information);

        return true;
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

        _ = GetRequiredProperty(root, "manifest", "schema_version", path);
        _ = GetRequiredProperty(root, "manifest", "created_at", path);

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

        _ = GetRequiredProperty(root, "infer", "schema_version", path);
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
        _ = GetRequiredProperty(final, "infer.final", "is_defect", path);
    }
}
