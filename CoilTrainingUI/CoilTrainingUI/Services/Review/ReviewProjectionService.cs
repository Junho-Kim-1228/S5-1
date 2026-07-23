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
    public string StatusColorMeaningText { get; init; } = "";
    public bool HasPersistentReview { get; init; }
    public bool NeedsMigration { get; init; }
    public bool IsUnreviewed { get; init; }
    public bool IsReviewing { get; init; }
    public bool IsConfirmedNormal { get; init; }
    public bool IsConfirmedDefect { get; init; }
    public bool IsBoxReviewConfirmed { get; init; }
    public bool IsExcluded { get; init; }
    public bool IsAutoAccepted { get; init; }
    public bool IsAutoReviewAudit { get; init; }
}

public sealed class ReviewProjectionService
{
    public ImageReviewProjection Create(
        ReviewStateLoadResult review,
        PredictionSnapshot prediction,
        TrainingEligibility eligibility)
    {
        ReviewState state = review.State;
        bool isAuditPending = state.Decision == ImageReviewDecision.Unreviewed &&
                              state.AutoReview?.HeldForAudit == true;
        string exclusion = eligibility.ExclusionReason;
        if (review.ParseFailed)
            exclusion = "검수 상태 파일 해석 실패";
        else if (review.IsLegacyProjection)
            exclusion = string.IsNullOrWhiteSpace(review.Message)
                ? "기존 state.json 마이그레이션 필요"
                : $"기존 상태 마이그레이션 필요 ({review.Message})";
        else if (isAuditPending)
            exclusion = "고신뢰 AI 자동수락 표본 검수 대상";

        return new ImageReviewProjection
        {
            DecisionText = isAuditPending
                ? "표본 검수"
                : GetDecisionText(state.Decision),
            DecisionStatusKey = state.Decision.ToString(),
            DecisionSourceText = GetDecisionSourceText(state.DecisionSource),
            BoxStatusText = GetBoxStatusText(
                state.BoxReview,
                state.BoxReviewSource,
                GetBoxDisplayCount(state, prediction)),
            AiAnomaText = prediction.ParseFailed
                ? "Anoma 결과 오류"
                : FormatAnomaPrediction(prediction),
            AiYoloText = prediction.HasAnomaDecision && !prediction.AnomaIsDefect
                ? "YOLO 미실행"
                : $"YOLO {prediction.YoloDetectionCount}개",
            TrainingEligibilityText = eligibility.SummaryText,
            ExclusionReasonText = exclusion,
            StatusColorMeaningText = GetStatusColorMeaningText(state, isAuditPending),
            HasPersistentReview = review.HasReviewFile,
            NeedsMigration = review.IsLegacyProjection,
            IsUnreviewed = state.Decision == ImageReviewDecision.Unreviewed,
            IsReviewing = state.Decision == ImageReviewDecision.Reviewing,
            IsConfirmedNormal = state.Decision == ImageReviewDecision.ConfirmedNormal,
            IsConfirmedDefect = state.Decision == ImageReviewDecision.ConfirmedDefect,
            IsBoxReviewConfirmed = state.BoxReview == BoxReviewDecision.Confirmed,
            IsExcluded = state.Decision == ImageReviewDecision.Excluded,
            IsAutoAccepted = state.DecisionSource == ReviewDecisionSource.AutoAcceptedAiPrediction,
            IsAutoReviewAudit = isAuditPending
        };
    }

    private static string GetStatusColorMeaningText(ReviewState state, bool isAuditPending)
    {
        if (state.Decision == ImageReviewDecision.Excluded)
            return "회색: 학습에서 제외한 이미지입니다.";
        if (isAuditPending)
            return "보라색: 고신뢰 AI 자동수락 후보 중 표본 검수 대상으로 보류된 이미지입니다.";
        if (state.Decision == ImageReviewDecision.ConfirmedDefect &&
            state.BoxReview != BoxReviewDecision.Confirmed)
        {
            return "주황색: 불량 판정은 확정됐지만 YOLO 박스 검수가 필요합니다.";
        }
        if (state.Decision == ImageReviewDecision.ConfirmedDefect)
            return "빨간색: 불량 판정과 박스 검수가 완료된 이미지입니다.";
        if (state.Decision == ImageReviewDecision.ConfirmedNormal)
            return "초록색: 정상 판정이 확정된 이미지입니다.";
        if (state.Decision == ImageReviewDecision.Reviewing)
            return "파란색: 현재 검수를 진행 중인 이미지입니다.";
        return "노란색: 아직 검수하지 않은 이미지입니다.";
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

    private static string GetBoxStatusText(
        BoxReviewDecision status,
        BoxReviewSource source,
        int count) => status switch
    {
        BoxReviewDecision.NotApplicable => "해당 없음",
        BoxReviewDecision.Predicted => $"AI 예측 ({count}개)",
        BoxReviewDecision.Edited when source == BoxReviewSource.AcceptedAiPrediction
            => $"AI 예측 수락 ({count}개, 확정 필요)",
        BoxReviewDecision.Edited => $"편집됨 ({count}개)",
        BoxReviewDecision.Confirmed when source == BoxReviewSource.AutoAcceptedAiPrediction
            => $"AI 자동 확정 ({count}개)",
        BoxReviewDecision.Confirmed => $"확정 ({count}개)",
        _ => status.ToString()
    };

    private static int GetBoxDisplayCount(ReviewState state, PredictionSnapshot prediction)
    {
        if (ReviewBoxLayerPolicy.IsPredictionOnly(state) &&
            prediction.HasFile &&
            !prediction.ParseFailed)
        {
            return prediction.YoloDetectionCount;
        }

        return state.Boxes.Count;
    }

    private static string GetDecisionSourceText(ReviewDecisionSource source) => source switch
    {
        ReviewDecisionSource.Manual => "사용자 확정",
        ReviewDecisionSource.AcceptedAiPrediction => "AI 판정 수락",
        ReviewDecisionSource.AutoAcceptedAiPrediction => "고신뢰 AI 자동수락",
        ReviewDecisionSource.LegacyManual => "기존 사용자 확정",
        ReviewDecisionSource.LegacyAuto => "기존 자동 판정",
        ReviewDecisionSource.LegacyUnknown => "기존 판정",
        _ => "-"
    };
}
