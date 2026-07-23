using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Models.Review;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services.Review;

public sealed class PredictionSnapshot
{
    public bool HasFile { get; init; }
    public bool ParseFailed { get; init; }
    public string Error { get; init; } = "";
    public bool HasAnomaDecision { get; init; }
    public bool AnomaIsDefect { get; init; }
    public double AnomaScore { get; init; }
    public double? AnomaScoreThreshold { get; init; }
    public string InferenceContextId { get; init; } = "";
    public IReadOnlyList<ReviewBox> YoloBoxes { get; init; } = Array.Empty<ReviewBox>();

    public int YoloDetectionCount => YoloBoxes.Count;
    public string AnomaDecisionText => !HasAnomaDecision
        ? "판정 없음"
        : (AnomaIsDefect ? "불량" : "정상");
}

public sealed class PredictionReader
{
    public PredictionSnapshot Read(string inferJsonPath, string? expectedInferenceContextId = null)
    {
        if (string.IsNullOrWhiteSpace(inferJsonPath) || !File.Exists(inferJsonPath))
            return new PredictionSnapshot();

        try
        {
            InferResultDto infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
            InferenceContextValidationService.ValidateInferContext(
                infer,
                expectedInferenceContextId,
                inferJsonPath);
            string decision = (infer.Anoma?.Decision ?? "").Trim().ToLowerInvariant();
            bool hasDecision = decision is "normal" or "anomaly";
            var boxes = (infer.Yolo?.Detections ?? new List<DetectionDto>())
                .Select(TryConvertBox)
                .Where(box => box != null)
                .Cast<ReviewBox>()
                .ToList();

            return new PredictionSnapshot
            {
                HasFile = true,
                HasAnomaDecision = hasDecision,
                AnomaIsDefect = decision == "anomaly",
                AnomaScore = infer.Anoma?.Score ?? 0,
                AnomaScoreThreshold = infer.Anoma?.ScoreThreshold,
                InferenceContextId = infer.InferenceContextId ?? "",
                YoloBoxes = boxes
            };
        }
        catch (Exception ex)
        {
            return new PredictionSnapshot
            {
                HasFile = true,
                ParseFailed = true,
                Error = ex.Message
            };
        }
    }

    private static ReviewBox? TryConvertBox(DetectionDto detection)
    {
        if (detection.BboxXywhNorm == null || detection.BboxXywhNorm.Length != 4)
            return null;

        double cx = detection.BboxXywhNorm[0];
        double cy = detection.BboxXywhNorm[1];
        double width = detection.BboxXywhNorm[2];
        double height = detection.BboxXywhNorm[3];
        if (!IsFinite(cx) || !IsFinite(cy) || !IsFinite(width) || !IsFinite(height) ||
            width <= 0 || height <= 0)
        {
            return null;
        }

        double left = Math.Clamp(cx - width / 2.0, 0, 1);
        double right = Math.Clamp(cx + width / 2.0, 0, 1);
        double top = Math.Clamp(cy - height / 2.0, 0, 1);
        double bottom = Math.Clamp(cy + height / 2.0, 0, 1);
        width = right - left;
        height = bottom - top;
        if (width <= 0 || height <= 0)
            return null;

        string className = (detection.ClassName ?? "").Trim().ToLowerInvariant();
        if (className is not ("dent" or "loose"))
            return null;

        return new ReviewBox
        {
            ClassName = className,
            X = left + width / 2.0,
            Y = top + height / 2.0,
            Width = width,
            Height = height,
            Source = "ai_prediction",
            PredictionConfidence = IsFinite(detection.Conf) ? detection.Conf : null
        };
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
