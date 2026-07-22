using CoilTrainingUI.Models.Review;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilTrainingUI.Services.Review;

public sealed class LegacyReviewConversion
{
    public ReviewState State { get; init; } = new();
    public bool IsAmbiguous { get; init; }
    public List<string> Notes { get; init; } = new();
}

public static class LegacyReviewConverter
{
    public static LegacyReviewConversion Convert(ImageStateDto legacy, string sourcePath = "")
    {
        legacy ??= new ImageStateDto();
        var notes = new List<string>();
        bool ambiguous = false;
        bool confirmed = legacy.HasConfirmedAnomalyDecision;
        bool hasBoxes = legacy.Labels?.Count > 0;

        ImageReviewDecision decision;
        if (confirmed && legacy.IsNormal == true)
        {
            decision = ImageReviewDecision.ConfirmedNormal;
            if (hasBoxes)
            {
                decision = ImageReviewDecision.Reviewing;
                ambiguous = true;
                notes.Add("legacy_normal_with_boxes");
            }
        }
        else if (confirmed && legacy.IsNormal == false)
        {
            decision = ImageReviewDecision.ConfirmedDefect;
        }
        else if (legacy.HasManualYoloDecision || hasBoxes ||
                 string.Equals(legacy.ReviewStatus, ReviewStatus.ReviewNeeded, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(legacy.ReviewStatus, ReviewStatus.AutoCandidate, StringComparison.OrdinalIgnoreCase))
        {
            decision = ImageReviewDecision.Reviewing;
            ambiguous = true;
            notes.Add("legacy_decision_not_confirmed");
        }
        else
        {
            decision = ImageReviewDecision.Unreviewed;
        }

        var boxes = new List<ReviewBox>();
        foreach (LabelDto label in legacy.Labels ?? new List<LabelDto>())
        {
            string className = (label.ClassName ?? "").Trim().ToLowerInvariant();
            bool validClass = className is "dent" or "loose";
            bool validCoordinates = IsFinite01(label.X) && IsFinite01(label.Y) &&
                                    IsFinite01(label.Width) && IsFinite01(label.Height) &&
                                    label.Width > 0 && label.Height > 0;
            if (!validClass || !validCoordinates)
            {
                ambiguous = true;
                if (!notes.Contains("legacy_invalid_box", StringComparer.OrdinalIgnoreCase))
                    notes.Add("legacy_invalid_box");
                continue;
            }

            boxes.Add(new ReviewBox
            {
                ClassName = className,
                X = label.X,
                Y = label.Y,
                Width = label.Width,
                Height = label.Height,
                Source = string.Equals(label.Source, "auto_infer", StringComparison.OrdinalIgnoreCase)
                    ? "ai_prediction"
                    : "legacy",
                PredictionConfidence = label.InferConf
            });
        }

        if (ambiguous)
            decision = ImageReviewDecision.Reviewing;
        ReviewDecisionSource decisionSource = decision == ImageReviewDecision.Reviewing
            ? ReviewDecisionSource.None
            : ResolveDecisionSource(legacy, confirmed);

        BoxReviewDecision boxReview;
        if (decision == ImageReviewDecision.ConfirmedNormal)
        {
            boxReview = BoxReviewDecision.NotApplicable;
            boxes.Clear();
        }
        else if (boxes.Count == 0)
        {
            boxReview = legacy.HasManualYoloDecision
                ? BoxReviewDecision.Confirmed
                : BoxReviewDecision.Predicted;
        }
        else if (legacy.HasManualYoloDecision)
        {
            boxReview = decision == ImageReviewDecision.ConfirmedDefect
                ? BoxReviewDecision.Confirmed
                : BoxReviewDecision.Edited;
        }
        else
        {
            boxReview = BoxReviewDecision.Predicted;
        }

        if (decision == ImageReviewDecision.Unreviewed && boxes.Count == 0)
            boxReview = BoxReviewDecision.NotApplicable;

        var state = new ReviewState
        {
            Decision = decision,
            DecisionSource = decisionSource,
            BoxReview = boxReview,
            Boxes = boxes,
            // Legacy state has no explicit YOLO-background opt-in. Keep it disabled
            // until the user selects it in the new review UI.
            UseAsYoloBackground = false,
            DecisionConfirmedAtUtc = confirmed ? legacy.ReviewedAt : null,
            BoxesConfirmedAtUtc = boxReview == BoxReviewDecision.Confirmed ? legacy.UpdatedAt : null,
            CreatedAtUtc = legacy.UpdatedAt,
            UpdatedAtUtc = legacy.UpdatedAt,
            Migration = new ReviewMigrationMetadata
            {
                SourcePath = sourcePath,
                Ambiguous = ambiguous,
                Notes = new List<string>(notes)
            }
        };

        return new LegacyReviewConversion
        {
            State = state,
            IsAmbiguous = ambiguous,
            Notes = notes
        };
    }

    private static ReviewDecisionSource ResolveDecisionSource(ImageStateDto legacy, bool confirmed)
    {
        if (!confirmed)
            return ReviewDecisionSource.None;

        if (string.Equals(legacy.DecisionSource, "auto", StringComparison.OrdinalIgnoreCase))
            return ReviewDecisionSource.LegacyAuto;

        if (legacy.HasManualAnomalyDecision ||
            string.Equals(legacy.DecisionSource, "manual", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewDecisionSource.LegacyManual;
        }

        return ReviewDecisionSource.LegacyUnknown;
    }

    private static bool IsFinite01(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
}
