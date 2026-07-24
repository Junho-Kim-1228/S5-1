using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.IO;

namespace CoilTrainingUI.Services;

public static class InferenceResultValidationService
{
    public static void Validate(
        InferResultDto infer,
        string? expectedImageId = null,
        string? sourcePath = null)
    {
        if (infer == null)
            throw new ArgumentNullException(nameof(infer));

        string source = string.IsNullOrWhiteSpace(sourcePath)
            ? "infer.json"
            : Path.GetFileName(sourcePath);
        string expectedId = (expectedImageId ?? "").Trim();
        string actualId = (infer.ImageId ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(expectedId) &&
            !string.Equals(expectedId, actualId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Inference image_id mismatch in {source}: expected '{expectedId}', actual '{actualId}'.");
        }

        InferAnomaDto? anoma = infer.Anoma;
        string decision = (anoma?.Decision ?? "").Trim().ToLowerInvariant();
        bool hasDecision = decision is "normal" or "anomaly";
        bool anomaExecuted = anoma?.Executed == true;

        if (!string.IsNullOrWhiteSpace(decision) && !hasDecision)
        {
            throw new InvalidDataException(
                $"Unsupported Anoma decision in {source}: '{decision}'.");
        }

        if (anomaExecuted && !hasDecision)
        {
            throw new InvalidDataException(
                $"Anoma executed without a normal/anomaly decision in {source}.");
        }

        if (!anomaExecuted && hasDecision)
        {
            throw new InvalidDataException(
                $"Anoma decision exists although Anoma was not executed in {source}.");
        }

        if (anomaExecuted && anoma != null)
        {
            double score = anoma.Score;
            if (!IsFinite(score))
                throw new InvalidDataException($"Invalid Anoma score in {source}.");

            if (infer.SchemaVersion >= 2 || anoma.ScoreThreshold.HasValue)
            {
                double? thresholdValue = anoma.ScoreThreshold;
                if (!thresholdValue.HasValue ||
                    !IsFinite(thresholdValue.Value) ||
                    thresholdValue.Value <= 0)
                {
                    throw new InvalidDataException($"Invalid Anoma score threshold in {source}.");
                }

                bool scoreIsDefect = score >= thresholdValue.Value;
                bool decisionIsDefect = decision == "anomaly";
                if (scoreIsDefect != decisionIsDefect)
                {
                    throw new InvalidDataException(
                        $"Anoma score/decision mismatch in {source}: " +
                        $"score={score:R}, threshold={thresholdValue.Value:R}, decision='{decision}'.");
                }

                if (infer.SchemaVersion >= 2 && infer.Final.IsDefect != decisionIsDefect)
                {
                    throw new InvalidDataException(
                        $"Anoma/final decision mismatch in {source}: " +
                        $"anoma='{decision}', final.is_defect={infer.Final.IsDefect}.");
                }
            }
        }

        int detectionCount = infer.Yolo?.Detections?.Count ?? 0;
        if (infer.Yolo?.Executed != true && detectionCount > 0)
        {
            throw new InvalidDataException(
                $"YOLO detections exist although YOLO was not executed in {source}.");
        }
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
