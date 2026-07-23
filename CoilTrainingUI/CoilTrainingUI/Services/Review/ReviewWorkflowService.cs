using CoilTrainingUI.Models.Review;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilTrainingUI.Services.Review;

public sealed class ReviewWorkflowService
{
    public ReviewState BeginReview(ReviewState current)
    {
        var next = Copy(current);
        if (next.Decision == ImageReviewDecision.Unreviewed)
            next.Decision = ImageReviewDecision.Reviewing;
        return Touch(next);
    }

    public ReviewState AcceptAiDecision(ReviewState current, PredictionSnapshot prediction)
    {
        if (prediction == null || !prediction.HasAnomaDecision || prediction.ParseFailed)
            throw new InvalidOperationException("수락할 Anoma 판정이 없습니다.");

        return prediction.AnomaIsDefect
            ? ConfirmDefect(current, ReviewDecisionSource.AcceptedAiPrediction)
            : ConfirmNormal(current, useAsYoloBackground: false, ReviewDecisionSource.AcceptedAiPrediction);
    }

    public ReviewState ConfirmNormal(
        ReviewState current,
        bool useAsYoloBackground,
        ReviewDecisionSource source = ReviewDecisionSource.Manual)
    {
        var next = Copy(current);
        next.Decision = ImageReviewDecision.ConfirmedNormal;
        next.DecisionSource = source;
        next.BoxReview = BoxReviewDecision.NotApplicable;
        next.BoxReviewSource = BoxReviewSource.None;
        next.Boxes.Clear();
        next.UseAsYoloBackground = useAsYoloBackground;
        next.ExclusionReason = "";
        next.DecisionConfirmedAtUtc = DateTime.UtcNow;
        next.BoxesConfirmedAtUtc = null;
        return Touch(next);
    }

    public ReviewState ConfirmDefect(
        ReviewState current,
        ReviewDecisionSource source = ReviewDecisionSource.Manual)
    {
        var next = Copy(current);
        next.Decision = ImageReviewDecision.ConfirmedDefect;
        next.DecisionSource = source;
        next.UseAsYoloBackground = false;
        next.ExclusionReason = "";
        next.DecisionConfirmedAtUtc = DateTime.UtcNow;
        if (next.BoxReview == BoxReviewDecision.NotApplicable)
            next.BoxReview = next.Boxes.Count > 0 ? BoxReviewDecision.Edited : BoxReviewDecision.Predicted;
        return Touch(next);
    }

    public ReviewState AcceptPredictionBoxes(ReviewState current, PredictionSnapshot prediction)
    {
        if (prediction == null || prediction.ParseFailed || !prediction.HasFile)
            throw new InvalidOperationException("수락할 YOLO 예측이 없습니다.");
        if (current.Decision != ImageReviewDecision.ConfirmedDefect &&
            (!prediction.HasAnomaDecision || !prediction.AnomaIsDefect))
        {
            throw new InvalidOperationException("YOLO 예측 박스는 Anoma 불량 이미지에서만 수락할 수 있습니다.");
        }

        var next = Copy(current);
        if (next.Decision == ImageReviewDecision.Unreviewed)
            next.Decision = ImageReviewDecision.Reviewing;
        next.Boxes = prediction.YoloBoxes.Select(box => box.Clone()).ToList();
        next.BoxReview = BoxReviewDecision.Edited;
        next.BoxReviewSource = BoxReviewSource.AcceptedAiPrediction;
        next.BoxesConfirmedAtUtc = null;
        return Touch(next);
    }

    public ReviewState ReplaceBoxesAfterEdit(ReviewState current, IEnumerable<ReviewBox> boxes)
    {
        var next = Copy(current);
        if (next.Decision == ImageReviewDecision.ConfirmedNormal)
            throw new InvalidOperationException("정상 확정 이미지는 박스를 저장할 수 없습니다.");
        if (next.Decision is ImageReviewDecision.Unreviewed or ImageReviewDecision.Excluded)
        {
            next.Decision = ImageReviewDecision.Reviewing;
            next.DecisionSource = ReviewDecisionSource.None;
            next.ExclusionReason = "";
        }

        next.Boxes = (boxes ?? Array.Empty<ReviewBox>()).Select(box => box.Clone()).ToList();
        next.BoxReview = BoxReviewDecision.Edited;
        next.BoxReviewSource = BoxReviewSource.Manual;
        next.BoxesConfirmedAtUtc = null;
        return Touch(next);
    }

    public ReviewState ConfirmBoxes(ReviewState current)
    {
        if (current.Decision != ImageReviewDecision.ConfirmedDefect)
            throw new InvalidOperationException("박스 확정은 불량 확정 이미지에서만 가능합니다.");

        var next = Copy(current);
        next.BoxReview = BoxReviewDecision.Confirmed;
        next.BoxReviewSource = BoxReviewSource.Manual;
        next.BoxesConfirmedAtUtc = DateTime.UtcNow;
        return Touch(next);
    }

    public ReviewState SetYoloBackground(ReviewState current, bool enabled)
    {
        if (current.Decision != ImageReviewDecision.ConfirmedNormal)
            throw new InvalidOperationException("YOLO 정상 배경은 정상 확정 이미지에서만 선택할 수 있습니다.");

        var next = Copy(current);
        next.UseAsYoloBackground = enabled;
        return Touch(next);
    }

    public ReviewState Exclude(ReviewState current, string reason)
    {
        var next = Copy(current);
        next.Decision = ImageReviewDecision.Excluded;
        next.DecisionSource = ReviewDecisionSource.Manual;
        next.UseAsYoloBackground = false;
        next.ExclusionReason = string.IsNullOrWhiteSpace(reason) ? "사용자 학습 제외" : reason.Trim();
        next.DecisionConfirmedAtUtc = DateTime.UtcNow;
        return Touch(next);
    }

    private static ReviewState Copy(ReviewState current)
        => current?.Clone() ?? new ReviewState();

    private static ReviewState Touch(ReviewState state)
    {
        state.SchemaVersion = ReviewState.CurrentSchemaVersion;
        state.UpdatedAtUtc = DateTime.UtcNow;
        return state;
    }
}
