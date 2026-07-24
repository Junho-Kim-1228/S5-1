using System;
using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services;

public sealed class AnomaInferenceCalibration
{
    public int InputSize { get; init; }
    public double ScoreThreshold { get; init; }
    public string ResizeMode { get; init; } = "";
    public int? CropPaddingPx { get; init; }
}

public static class AnomaInferenceCalibrationReader
{
    public static AnomaInferenceCalibration? TryLoad(string anomaOutDirectory)
    {
        string configPath = Path.Combine(anomaOutDirectory, "inference_config.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("input_size", out JsonElement inputSizeElement)
                || !inputSizeElement.TryGetInt32(out int inputSize)
                || inputSize <= 0
                || !root.TryGetProperty("score_threshold", out JsonElement thresholdElement)
                || !thresholdElement.TryGetDouble(out double scoreThreshold)
                || double.IsNaN(scoreThreshold)
                || double.IsInfinity(scoreThreshold))
            {
                return null;
            }

            string resizeMode = "";
            if (root.TryGetProperty("preprocessing", out JsonElement preprocessingElement)
                && preprocessingElement.ValueKind == JsonValueKind.Object
                && preprocessingElement.TryGetProperty("resize", out JsonElement resizeElement)
                && resizeElement.ValueKind == JsonValueKind.String)
            {
                resizeMode = (resizeElement.GetString() ?? "").Trim().ToLowerInvariant();
            }

            return new AnomaInferenceCalibration
            {
                InputSize = inputSize,
                ScoreThreshold = scoreThreshold,
                ResizeMode = resizeMode,
                CropPaddingPx = string.Equals(resizeMode, "stretch", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : null
            };
        }
        catch
        {
            return null;
        }
    }
}
