using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Services;

public static class BatchManifestService
{
    public static string GetManifestPath(string batchRoot)
        => Path.Combine(batchRoot, "meta", "manifest.json");

    public static void UpdateBatchId(string batchRoot, string newBatchId)
    {
        if (string.IsNullOrWhiteSpace(batchRoot))
            throw new ArgumentException("batchRoot is empty.", nameof(batchRoot));

        string normalizedBatchId = newBatchId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedBatchId))
            throw new InvalidOperationException("배치명은 비워둘 수 없습니다.");

        string manifestPath = GetManifestPath(batchRoot);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest.json을 찾을 수 없습니다.", manifestPath);

        string originalJson = File.ReadAllText(manifestPath, Encoding.UTF8);
        JsonObject manifestObject;

        try
        {
            manifestObject = JsonNode.Parse(originalJson) as JsonObject
                ?? throw new InvalidOperationException("manifest.json 형식이 올바르지 않습니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"manifest.json 파싱 실패: {ex.Message}", ex);
        }

        string previousBatchId = manifestObject["batch_id"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (string.Equals(previousBatchId, normalizedBatchId, StringComparison.Ordinal))
            return;

        manifestObject["batch_id"] = normalizedBatchId;

        string updatedJson = manifestObject.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        try
        {
            File.WriteAllText(manifestPath, updatedJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var validation = BatchFolderValidationService.Validate(batchRoot);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Message);
        }
        catch
        {
            File.WriteAllText(manifestPath, originalJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            throw;
        }
    }
}
