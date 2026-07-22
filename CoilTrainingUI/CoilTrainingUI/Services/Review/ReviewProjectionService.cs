using CoilTrainingUI.Models.Review;
using System;

namespace CoilTrainingUI.Services.Review;

public sealed class ImageReviewProjection
{
    public string DecisionText { get; init; } = "미검수";
    public string DecisionStatusKey { get; init; } = "Unreviewed";
    public string DecisionSourceText { get; init; } = "-";
    public string BoxStatusText { get; init; } = "해당 없음";
    public string AiAnomaText { get; init; } = "판정 없음";
    public string AiYoloText { get; init; } = "YOLO 0개";
    public string TrainingEligibilityText { get; init; } = "학습 제외";
    public string ExclusionReasonText { get; init; } = "";
    public bool HasPersistentReview { get; init; }
    public bool NeedsMigration { get; init; }
    public bool IsUnreviewed { get; init; }
    public bool IsReviewing { get; init; }
    public bool IsConfirmedNormal { get; init; }
    public bool IsConfirmedDefect { get; init; }
    public bool IsExcluded { get; init; }
}

public sealed class ReviewProjectionService
{
    public ImageReviewProjection Create(
        ReviewStateLoadResult review,
        PredictionSnapshot prediction,
        TrainingEligibility eligibility)
    {
        ReviewState state = review.State;
        string exclusion = eligibility.ExclusionReason;
        if (review.ParseFailed)
            exclusion = "검수 상태 파일 해석 실패";
        else if (review.IsLegacyProjection)
            exclusion = string.IsNullOrWhiteSpace(review.Message)
                ? "기존 state.json 마이그레이션 필요"
                : $"기존 상태 마이그레이션 필요 ({review.Message})";

        return new ImageReviewProjection
        {
            DecisionText = GetDecisionText(state.Decision),
            DecisionStatusKey = state.Decision.ToString(),
            DecisionSourceText = GetDecisionSourceText(state.DecisionSource),
            BoxStatusText = GetBoxStatusText(state.BoxReview, state.Boxes.Count),
            AiAnomaText = prediction.ParseFailed
                ? "Anoma 결과 오류"
                : FormatAnomaPrediction(prediction),
            AiYoloText = prediction.HasAnomaDecision && !prediction.AnomaIsDefect
                ? "YOLO 미실행"
                : $"YOLO {prediction.YoloDetectionCount}개",
            TrainingEligibilityText = eligibility.SummaryText,
            ExclusionReasonText = exclusion,
            HasPersistentReview = review.HasReviewFile,
            NeedsMigration = review.IsLegacyProjection,
            IsUnreviewed = state.Decision == ImageReviewDecision.Unreviewed,
            IsReviewing = state.Decision == ImageReviewDecision.Reviewing,
            IsConfirmedNormal = state.Decision == ImageReviewDecision.ConfirmedNormal,
            IsConfirmedDefect = state.Decision == ImageReviewDecision.ConfirmedDefect,
            IsExcluded = state.Decision == ImageReviewDecision.Excluded
        };
    }

    private static string GetDecisionText(ImageReviewDecision decision) => decision switch
    {
        ImageReviewDecision.Unreviewed => "미검수",
        ImageReviewDecision.Reviewing => "검수 중",
        ImageReviewDecision.ConfirmedNormal => "정상 확정",
        ImageReviewDecision.ConfirmedDefect => "불량 확정",
        ImageReviewDecision.Excluded => "학습 제외",
        _ => decision.ToString()
    };

    private static string FormatAnomaPrediction(PredictionSnapshot prediction)
    {
        string threshold = prediction.AnomaScoreThreshold.HasValue
            ? $" / 기준 {prediction.AnomaScoreThreshold.Value:0.000}"
            : " / 기준 미기록";
        return $"Anoma {prediction.AnomaDecisionText} / {prediction.AnomaScore:0.000}{threshold}";
    }

    private static string GetBoxStatusText(BoxReviewDecision status, int count) => status switch
    {
        BoxReviewDecision.NotApplicable => "해당 없음",
        BoxReviewDecision.Predicted => $"AI 예측 ({count}개)",
        BoxReviewDecision.Edited => $"편집됨 ({count}개)",
        BoxReviewDecision.Confirmed => $"확정 ({count}개)",
        _ => status.ToString()
    };

    private static string GetDecisionSourceText(ReviewDecisionSource source) => source switch
    {
        ReviewDecisionSource.Manual => "사용자 확정",
        ReviewDecisionSource.AcceptedAiPrediction => "AI 판정 수락",
        ReviewDecisionSource.LegacyManual => "기존 사용자 확정",
        ReviewDecisionSource.LegacyAuto => "기존 자동 판정",
        ReviewDecisionSource.LegacyUnknown => "기존 판정",
        _ => "-"
    };
}
