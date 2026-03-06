using CoilTrainingUI.Models.InferenceBatch;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilTrainingUI.Services
{
    public sealed class PredictionConsensusEvaluation
    {
        public bool YoloDefect { get; init; }
        public bool AnomaDefect { get; init; }
        public double YoloMaxConf { get; init; }
        public double AnomaScore { get; init; }
        public bool IsAgreement => YoloDefect == AnomaDefect;
        public bool IsHighConfidence { get; init; }
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
        public bool RequiresReview => !IsAgreement || !IsHighConfidence;
    }

    public static class PredictionConsensusPolicy
    {
        // Defect agreement confidence gate
        public const double YoloDefectMinConf = 0.75;
        public const double AnomaAnomalyMinScore = 0.75;

        // Normal agreement confidence gate
        public const double AnomaNormalMaxScore = 0.25;

        public static PredictionConsensusEvaluation Evaluate(InferResultDto infer)
        {
            bool yoloDefect = false;
            double yoloMaxConf = 0.0;

            foreach (var detection in infer.Yolo?.Detections ?? Enumerable.Empty<DetectionDto>())
            {
                if (!IsUsableDetectionForDecision(detection))
                    continue;

                yoloDefect = true;
                if (IsFinite(detection.Conf))
                    yoloMaxConf = Math.Max(yoloMaxConf, detection.Conf);
            }

            string anomaDecision = infer.Anoma?.Decision ?? "";
            bool anomaDefect = string.Equals(anomaDecision, "anomaly", StringComparison.OrdinalIgnoreCase);
            double anomaScore = infer.Anoma?.Score ?? double.NaN;

            return Evaluate(yoloDefect, yoloMaxConf, anomaDefect, anomaScore, anomaDecision);
        }

        public static PredictionConsensusEvaluation Evaluate(
            bool yoloDefect,
            double yoloMaxConf,
            bool anomaDefect,
            double anomaScore,
            string? anomaDecision = null)
        {
            var reasons = new List<string>();

            if (!string.IsNullOrWhiteSpace(anomaDecision))
            {
                string normalized = anomaDecision.Trim().ToLowerInvariant();
                if (normalized != "normal" && normalized != "anomaly")
                    reasons.Add("anoma_decision_unknown");
            }

            if (yoloDefect != anomaDefect)
                reasons.Add("model_disagree");

            // YOLO는 정상 쪽 confidence를 직접 제공하지 않으므로
            // defect일 때만 YOLO confidence 게이트를 적용한다.
            if (yoloDefect)
            {
                if (!IsFinite(yoloMaxConf) || yoloMaxConf < YoloDefectMinConf)
                    reasons.Add("yolo_low_conf");
            }

            if (anomaDefect)
            {
                if (!IsFinite(anomaScore) || anomaScore < AnomaAnomalyMinScore)
                    reasons.Add("anoma_low_conf");
            }
            else
            {
                if (!IsFinite(anomaScore) || anomaScore > AnomaNormalMaxScore)
                    reasons.Add("anoma_low_conf");
            }

            return new PredictionConsensusEvaluation
            {
                YoloDefect = yoloDefect,
                AnomaDefect = anomaDefect,
                YoloMaxConf = yoloMaxConf,
                AnomaScore = anomaScore,
                IsHighConfidence = reasons.Count == 0,
                Reasons = reasons
            };
        }

        public static bool IsUsableDetectionForDecision(DetectionDto detection)
        {
            if (detection.BboxXywhNorm == null || detection.BboxXywhNorm.Length != 4)
                return false;

            double cx = detection.BboxXywhNorm[0];
            double cy = detection.BboxXywhNorm[1];
            double bw = detection.BboxXywhNorm[2];
            double bh = detection.BboxXywhNorm[3];

            if (!IsFinite(cx) || !IsFinite(cy) || !IsFinite(bw) || !IsFinite(bh))
                return false;

            if (bw <= 0 || bh <= 0)
                return false;

            double left = Math.Clamp(cx - (bw / 2.0), 0.0, 1.0);
            double right = Math.Clamp(cx + (bw / 2.0), 0.0, 1.0);
            double top = Math.Clamp(cy - (bh / 2.0), 0.0, 1.0);
            double bottom = Math.Clamp(cy + (bh / 2.0), 0.0, 1.0);

            return (right - left) > 0 && (bottom - top) > 0;
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
