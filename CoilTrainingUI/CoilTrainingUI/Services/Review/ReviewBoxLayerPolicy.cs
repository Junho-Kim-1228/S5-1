using CoilTrainingUI.Models.Review;
using System;
using System.Collections.Generic;

namespace CoilTrainingUI.Services.Review;

/// <summary>
/// Keeps read-only AI predictions out of the editable review layer.
/// Prediction-only boxes are rendered from infer.json by the overlay instead.
/// </summary>
public static class ReviewBoxLayerPolicy
{
    public static bool IsPredictionOnly(ReviewState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return state.BoxReview == BoxReviewDecision.Predicted &&
               state.BoxReviewSource == BoxReviewSource.AiPrediction;
    }

    public static IReadOnlyList<ReviewBox> GetEditableBoxes(ReviewState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return IsPredictionOnly(state)
            ? Array.Empty<ReviewBox>()
            : state.Boxes;
    }

    public static bool CanSaveEditedBoxes(ReviewState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return state.Decision == ImageReviewDecision.ConfirmedDefect &&
               state.BoxReview == BoxReviewDecision.Edited;
    }

    public static bool CanAcceptPredictionBoxes(
        bool hasUsablePrediction,
        bool yoloExecuted,
        bool predictionIsDefect,
        bool isConfirmedNormal,
        bool isConfirmedDefect,
        bool isExcluded,
        bool isBoxConfirmed,
        bool isBoxEdited)
    {
        return hasUsablePrediction &&
               yoloExecuted &&
               !isConfirmedNormal &&
               !isExcluded &&
               (predictionIsDefect || isConfirmedDefect) &&
               !isBoxConfirmed &&
               !isBoxEdited;
    }
}
