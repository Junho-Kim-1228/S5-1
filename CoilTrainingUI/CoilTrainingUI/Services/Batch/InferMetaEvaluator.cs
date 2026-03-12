using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services;

public sealed class InferMetaSummary
{
    public bool HasInferFile { get; set; }
    public bool HasAiInfer { get; set; }
    public bool InferParseFailed { get; set; }
    public bool HasYoloDefect { get; set; }
    public bool IsAnomaNormal { get; set; } = true;
    public bool IsConsensusHighConfidence { get; set; }
    public double YoloMaxConf { get; set; }
    public double AnomaScore { get; set; }
    public int DentCount { get; set; }
    public int LooseCount { get; set; }
    public int OtherCount { get; set; }
}

public static class InferMetaEvaluator
{
    public static InferMetaSummary Evaluate(string inferJsonPath)
    {
        var summary = new InferMetaSummary
        {
            HasInferFile = !string.IsNullOrWhiteSpace(inferJsonPath) && File.Exists(inferJsonPath)
        };

        if (!summary.HasInferFile)
            return summary;

        try
        {
            InferResultDto infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
            var evaluation = PredictionConsensusPolicy.Evaluate(infer);
            summary.HasAiInfer = true;
            summary.HasYoloDefect = evaluation.YoloDefect;
            summary.IsAnomaNormal = !evaluation.AnomaDefect;
            summary.IsConsensusHighConfidence = !evaluation.RequiresReview;
            summary.YoloMaxConf = evaluation.YoloMaxConf;
            summary.AnomaScore = evaluation.AnomaScore;

            foreach (var detection in infer.Yolo?.Detections ?? Enumerable.Empty<DetectionDto>())
            {
                if (!PredictionConsensusPolicy.IsUsableDetectionForDecision(detection))
                    continue;

                string className = (detection.ClassName ?? "").Trim().ToLowerInvariant();
                if (className == "dent")
                {
                    summary.DentCount++;
                    continue;
                }

                if (className == "loose")
                {
                    summary.LooseCount++;
                    continue;
                }

                summary.OtherCount++;
            }

            return summary;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"AI status parse failed: {inferJsonPath}, {ex.Message}");
            summary.InferParseFailed = true;
            return summary;
        }
    }
}
