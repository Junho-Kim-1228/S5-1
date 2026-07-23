using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private static (int Dent, int Loose, int Other) CountDefectClasses(IEnumerable<string?> classNames)
        {
            int dent = 0;
            int loose = 0;
            int other = 0;
            foreach (var className in classNames)
            {
                string normalized = (className ?? "").Trim().ToLowerInvariant();
                if (normalized == "dent") dent++;
                else if (normalized == "loose") loose++;
                else if (!string.IsNullOrWhiteSpace(normalized)) other++;
            }
            return (dent, loose, other);
        }

        private void UpdateGtSummaryForImageItem(ImageItem item, string imagePath)
        {
            ReviewStateLoadResult review = _reviewRepository.Load(imagePath);
            UpdateGtSummaryForImageItem(item, imagePath, review);
        }

        private void UpdateGtSummaryForImageItem(
            ImageItem item,
            string imagePath,
            ReviewStateLoadResult review)
        {
            PredictionSnapshot prediction = _predictionByImagePath.TryGetValue(imagePath, out var cached)
                ? cached
                : _predictionReader.Read(
                    _inferJsonByImagePath.TryGetValue(imagePath, out var path) ? path : "",
                    _expectedInferenceContextByImagePath.TryGetValue(imagePath, out var expectedContextId)
                        ? expectedContextId
                        : "");
            TrainingEligibility eligibility = EvaluateTrainingEligibility(
                review,
                prediction,
                item.RequiresInfer);
            ImageReviewProjection projection = _reviewProjection.Create(review, prediction, eligibility);
            var counts = CountDefectClasses(
                ReviewBoxLayerPolicy.GetEditableBoxes(review.State).Select(box => box.ClassName));

            item.GtDentCount = counts.Dent;
            item.GtLooseCount = counts.Loose;
            item.GtOtherCount = counts.Other;
            item.HasStateFile = review.HasReviewFile;
            item.DecisionStatusKey = projection.DecisionStatusKey;
            item.UserDecisionText = projection.DecisionText;
            item.UserDecisionSourceText = projection.DecisionSourceText;
            item.BoxReviewStatusText = projection.BoxStatusText;
            item.AiAnomaSummaryText = projection.AiAnomaText;
            item.AiYoloSummaryText = projection.AiYoloText;
            item.TrainingEligibilityText = projection.TrainingEligibilityText;
            item.TrainingExclusionReasonText = projection.ExclusionReasonText;
            item.StatusColorMeaningText = projection.StatusColorMeaningText;
            item.NeedsLegacyMigration = projection.NeedsMigration;
            item.IsReviewUnreviewed = projection.IsUnreviewed;
            item.IsReviewing = projection.IsReviewing;
            item.IsReviewConfirmedNormal = projection.IsConfirmedNormal;
            item.IsReviewConfirmedDefect = projection.IsConfirmedDefect;
            item.IsBoxReviewConfirmed = projection.IsBoxReviewConfirmed;
            item.IsReviewExcluded = projection.IsExcluded;
            item.IsAutoAccepted = projection.IsAutoAccepted;
            item.IsAutoReviewAudit = projection.IsAutoReviewAudit;
            item.AnomaTrainingEligible = eligibility.AnomaTraining;
            item.AnomaEvaluationEligible = eligibility.AnomaEvaluation;
            item.YoloPositiveEligible = eligibility.YoloPositive;
            item.YoloBackgroundEligible = eligibility.YoloBackground;
            item.YoloExcludedNoBoxDefect = eligibility.YoloExcludedDefectWithoutBoxes;
        }

        private void SyncGtSummaryForImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return;
            var item = _images.FirstOrDefault(candidate =>
                string.Equals(candidate.ProcessedPath, imagePath, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                UpdateGtSummaryForImageItem(item, imagePath);
            RefreshSummaryCounts();
        }

        private TrainingEligibility EvaluateTrainingEligibility(
            ReviewStateLoadResult review,
            PredictionSnapshot prediction,
            bool requiresInfer)
        {
            if (requiresInfer && prediction.ParseFailed)
            {
                return new TrainingEligibility
                {
                    ExclusionReason = string.IsNullOrWhiteSpace(prediction.Error)
                        ? "infer.json 검증 실패"
                        : $"infer.json 검증 실패: {prediction.Error}"
                };
            }

            return _trainingDatasetSelector.Evaluate(review);
        }

        private void ApplyAnomalyDecisionToItem(ImageItem item, bool isNormal, bool refreshSummary = true)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ProcessedPath))
                return;

            ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
            ReviewState next = isNormal
                ? _reviewWorkflow.ConfirmNormal(current, useAsYoloBackground: false)
                : _reviewWorkflow.ConfirmDefect(current);
            _reviewRepository.Save(item.ProcessedPath, next);
            _currentReviewState = next;

            if (isNormal)
            {
                _imageStateManager.ClearLabels(item.ProcessedPath);
                if (string.Equals(_currentImagePath, item.ProcessedPath, StringComparison.OrdinalIgnoreCase))
                    _bboxManager.ClearAll();
            }

            UpdateGtSummaryForImageItem(item, item.ProcessedPath);
            if (string.Equals(_currentImagePath, item.ProcessedPath, StringComparison.OrdinalIgnoreCase))
                UpdateSelectedReviewControls(next);
            if (refreshSummary)
                ApplyImageFilters();
        }

        private void UpdateSelectedReviewControls(ReviewState state)
        {
            _isLoadingImage = true;
            try
            {
                NormalRadio.IsChecked = state.Decision == ImageReviewDecision.ConfirmedNormal;
                AbnormalRadio.IsChecked = state.Decision == ImageReviewDecision.ConfirmedDefect;
                YoloBackgroundCheckBox.IsChecked = state.UseAsYoloBackground;
                YoloBackgroundCheckBox.IsEnabled = state.Decision == ImageReviewDecision.ConfirmedNormal;
            }
            finally
            {
                _isLoadingImage = false;
            }
        }

        private void EnsureSelectedImageVisible()
        {
            if (ImageListBox.SelectedItem is ImageItem selectedItem && IsVisibleInCurrentFilter(selectedItem))
                return;

            var firstVisible = _imageCollectionView?.Cast<object>().OfType<ImageItem>().FirstOrDefault();
            if (firstVisible != null)
            {
                ImageListBox.SelectedItem = firstVisible;
                ImageListBox.ScrollIntoView(firstVisible);
                return;
            }

            ImageListBox.SelectedItem = null;
            ResetImageDisplay();
        }

        private void SyncAnomalyRadioFromSelectedItem()
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
            {
                UpdateSelectedReviewControls(new ReviewState());
                return;
            }
            UpdateSelectedReviewControls(_reviewRepository.Load(item.ProcessedPath).State);
        }
    }
}
