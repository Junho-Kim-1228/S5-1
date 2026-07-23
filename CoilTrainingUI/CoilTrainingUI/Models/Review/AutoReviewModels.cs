using System;
using System.Text.Json.Serialization;

namespace CoilTrainingUI.Models.Review;

public sealed class AutoReviewPolicy
{
    public bool Enabled { get; init; } = true;
    public string PolicyVersion { get; init; } = "auto_review_v1";
    public double AnomaNormalThresholdMultiplier { get; init; } = 0.5;
    public double AnomaDefectThresholdMultiplier { get; init; } = 2.0;
    public double YoloBoxMinConfidence { get; init; } = 0.85;
    public double AuditSampleRate { get; init; } = 0.10;

    public static AutoReviewPolicy Disabled { get; } = new() { Enabled = false };
}

public sealed class AutoReviewMetadata
{
    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; set; } = "";

    [JsonPropertyName("inference_context_id")]
    public string InferenceContextId { get; set; } = "";

    [JsonPropertyName("anoma_score")]
    public double AnomaScore { get; set; }

    [JsonPropertyName("anoma_score_threshold")]
    public double AnomaScoreThreshold { get; set; }

    [JsonPropertyName("normal_auto_max_score")]
    public double NormalAutoMaxScore { get; set; }

    [JsonPropertyName("defect_auto_min_score")]
    public double DefectAutoMinScore { get; set; }

    [JsonPropertyName("yolo_box_min_confidence")]
    public double YoloBoxMinConfidence { get; set; }

    [JsonPropertyName("audit_sample_rate")]
    public double AuditSampleRate { get; set; }

    [JsonPropertyName("held_for_audit")]
    public bool HeldForAudit { get; set; }

    [JsonPropertyName("candidate_decision")]
    public string CandidateDecision { get; set; } = "";

    [JsonPropertyName("decision_auto_accepted")]
    public bool DecisionAutoAccepted { get; set; }

    [JsonPropertyName("boxes_auto_accepted")]
    public bool BoxesAutoAccepted { get; set; }

    [JsonPropertyName("applied_at_utc")]
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;

    public AutoReviewMetadata Clone() => new()
    {
        PolicyVersion = PolicyVersion,
        InferenceContextId = InferenceContextId,
        AnomaScore = AnomaScore,
        AnomaScoreThreshold = AnomaScoreThreshold,
        NormalAutoMaxScore = NormalAutoMaxScore,
        DefectAutoMinScore = DefectAutoMinScore,
        YoloBoxMinConfidence = YoloBoxMinConfidence,
        AuditSampleRate = AuditSampleRate,
        HeldForAudit = HeldForAudit,
        CandidateDecision = CandidateDecision,
        DecisionAutoAccepted = DecisionAutoAccepted,
        BoxesAutoAccepted = BoxesAutoAccepted,
        AppliedAtUtc = AppliedAtUtc
    };
}
