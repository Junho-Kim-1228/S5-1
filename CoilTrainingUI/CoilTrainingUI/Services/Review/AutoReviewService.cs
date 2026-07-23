using CoilTrainingUI.Models.Review;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CoilTrainingUI.Services.Review;

public enum AutoReviewDisposition
{
    NotApplied,
    AuditHeld,
    AcceptedNormal,
    AcceptedDefect,
    AcceptedDefectWithBoxes
}

public sealed class AutoReviewEvaluation
{
    public AutoReviewDisposition Disposition { get; init; }
    public ReviewState? StateToPersist { get; init; }
    public string Reason { get; init; } = "";
    public bool ShouldPersist => StateToPersist != null;
}

/// <summary>
/// Pure policy evaluator for automatic review. It never writes files and never
/// replaces an existing or legacy-projected review state.
/// </summary>
public sealed class AutoReviewService
{
    public AutoReviewEvaluation Evaluate(
        ReviewStateLoadResult existing,
        PredictionSnapshot prediction,
        AutoReviewPolicy policy,
        string stableSampleKey)
    {
        if (existing == null)
            throw new ArgumentNullException(nameof(existing));
        if (prediction == null)
            throw new ArgumentNullException(nameof(prediction));
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        if (!policy.Enabled)
            return NotApplied("auto review disabled");
        if (existing.HasReviewFile || existing.IsLegacyProjection || existing.ParseFailed)
            return NotApplied("existing review state is protected");
        if (!IsValidPolicy(policy))
            return NotApplied("invalid auto review policy");
        if (!prediction.HasFile || prediction.ParseFailed || !prediction.HasAnomaDecision)
            return NotApplied("valid Anoma prediction is required");
        if (string.IsNullOrWhiteSpace(prediction.InferenceContextId))
            return NotApplied("inference context is missing");
        if (!prediction.AnomaScoreThreshold.HasValue ||
            !IsFinite(prediction.AnomaScoreThreshold.Value) ||
            prediction.AnomaScoreThreshold.Value <= 0 ||
            !IsFinite(prediction.AnomaScore))
        {
            return NotApplied("Anoma score threshold is missing or invalid");
        }

        double scoreThreshold = prediction.AnomaScoreThreshold.Value;
        double normalMax = scoreThreshold * policy.AnomaNormalThresholdMultiplier;
        double defectMin = scoreThreshold * policy.AnomaDefectThresholdMultiplier;
        bool highConfidenceNormal = !prediction.AnomaIsDefect && prediction.AnomaScore <= normalMax;
        bool highConfidenceDefect = prediction.AnomaIsDefect && prediction.AnomaScore >= defectMin;
        if (!highConfidenceNormal && !highConfidenceDefect)
            return NotApplied("prediction is inside the manual-review gray zone");

        string candidateDecision = highConfidenceDefect ? "defect" : "normal";
        var metadata = new AutoReviewMetadata
        {
            PolicyVersion = policy.PolicyVersion.Trim(),
            InferenceContextId = prediction.InferenceContextId.Trim(),
            AnomaScore = prediction.AnomaScore,
            AnomaScoreThreshold = scoreThreshold,
            NormalAutoMaxScore = normalMax,
            DefectAutoMinScore = defectMin,
            YoloBoxMinConfidence = policy.YoloBoxMinConfidence,
            AuditSampleRate = policy.AuditSampleRate,
            CandidateDecision = candidateDecision,
            AppliedAtUtc = DateTime.UtcNow
        };

        if (IsAuditSample(stableSampleKey, prediction.InferenceContextId, policy.AuditSampleRate))
        {
            metadata.HeldForAudit = true;
            return new AutoReviewEvaluation
            {
                Disposition = AutoReviewDisposition.AuditHeld,
                StateToPersist = new ReviewState
                {
                    Decision = ImageReviewDecision.Unreviewed,
                    DecisionSource = ReviewDecisionSource.None,
                    BoxReview = BoxReviewDecision.NotApplicable,
                    BoxReviewSource = BoxReviewSource.None,
                    AutoReview = metadata
                },
                Reason = "high-confidence prediction held for audit"
            };
        }

        metadata.DecisionAutoAccepted = true;
        if (highConfidenceNormal)
        {
            return new AutoReviewEvaluation
            {
                Disposition = AutoReviewDisposition.AcceptedNormal,
                StateToPersist = new ReviewState
                {
                    Decision = ImageReviewDecision.ConfirmedNormal,
                    DecisionSource = ReviewDecisionSource.AutoAcceptedAiPrediction,
                    BoxReview = BoxReviewDecision.NotApplicable,
                    BoxReviewSource = BoxReviewSource.None,
                    DecisionConfirmedAtUtc = DateTime.UtcNow,
                    AutoReview = metadata
                },
                Reason = "high-confidence Anoma normal auto-accepted"
            };
        }

        var predictedBoxes = prediction.YoloBoxes.Select(box => box.Clone()).ToList();
        bool boxesHighConfidence = predictedBoxes.Count > 0 && predictedBoxes.All(box =>
            box.PredictionConfidence.HasValue &&
            IsFinite(box.PredictionConfidence.Value) &&
            box.PredictionConfidence.Value >= policy.YoloBoxMinConfidence);
        metadata.BoxesAutoAccepted = boxesHighConfidence;
        DateTime confirmedAt = DateTime.UtcNow;

        return new AutoReviewEvaluation
        {
            Disposition = boxesHighConfidence
                ? AutoReviewDisposition.AcceptedDefectWithBoxes
                : AutoReviewDisposition.AcceptedDefect,
            StateToPersist = new ReviewState
            {
                Decision = ImageReviewDecision.ConfirmedDefect,
                DecisionSource = ReviewDecisionSource.AutoAcceptedAiPrediction,
                BoxReview = boxesHighConfidence
                    ? BoxReviewDecision.Confirmed
                    : BoxReviewDecision.Predicted,
                BoxReviewSource = boxesHighConfidence
                    ? BoxReviewSource.AutoAcceptedAiPrediction
                    : BoxReviewSource.AiPrediction,
                Boxes = boxesHighConfidence
                    ? predictedBoxes
                    : new System.Collections.Generic.List<ReviewBox>(),
                DecisionConfirmedAtUtc = confirmedAt,
                BoxesConfirmedAtUtc = boxesHighConfidence ? confirmedAt : null,
                AutoReview = metadata
            },
            Reason = boxesHighConfidence
                ? "high-confidence Anoma defect and YOLO boxes auto-accepted"
                : "high-confidence Anoma defect auto-accepted; boxes require review"
        };
    }

    private static bool IsValidPolicy(AutoReviewPolicy policy)
    {
        return !string.IsNullOrWhiteSpace(policy.PolicyVersion) &&
               IsFinite(policy.AnomaNormalThresholdMultiplier) &&
               policy.AnomaNormalThresholdMultiplier >= 0 &&
               policy.AnomaNormalThresholdMultiplier < 1 &&
               IsFinite(policy.AnomaDefectThresholdMultiplier) &&
               policy.AnomaDefectThresholdMultiplier > 1 &&
               IsFinite(policy.YoloBoxMinConfidence) &&
               policy.YoloBoxMinConfidence is >= 0 and <= 1 &&
               IsFinite(policy.AuditSampleRate) &&
               policy.AuditSampleRate is >= 0 and <= 1;
    }

    private static bool IsAuditSample(string stableSampleKey, string contextId, double sampleRate)
    {
        if (sampleRate <= 0)
            return false;
        if (sampleRate >= 1)
            return true;

        string material = $"{contextId}|{stableSampleKey}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        uint bucket = BitConverter.ToUInt32(hash, 0);
        double fraction = bucket / ((double)uint.MaxValue + 1.0);
        return fraction < sampleRate;
    }

    private static AutoReviewEvaluation NotApplied(string reason) => new()
    {
        Disposition = AutoReviewDisposition.NotApplied,
        Reason = reason
    };

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
