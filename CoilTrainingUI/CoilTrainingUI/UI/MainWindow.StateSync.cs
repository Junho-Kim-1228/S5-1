using System;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
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
                if (normalized == "dent")
                {
                    dent++;
                    continue;
                }

                if (normalized == "loose")
                {
                    loose++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(normalized))
                    other++;
            }

            return (dent, loose, other);
        }

        private void UpdateGtSummaryForImageItem(ImageItem item, string imagePath)
        {
            var boxes = _imageStateManager.GetLabels(imagePath);
            var counts = CountDefectClasses(boxes.Select(b => b.ClassName));
            var state = _stateService.Load(imagePath);
            item.GtDentCount = counts.Dent;
            item.GtLooseCount = counts.Loose;
            item.GtOtherCount = counts.Other;
            item.HasLabel = state.HasManualYoloDecision && boxes.Count > 0;
            item.HasStateFile = _stateService.HasState(imagePath);
            item.ReviewStatus = DeriveReviewStatusForItem(item, state);
            item.ReviewReasonText = state.ReviewReasons.Count > 0
                ? string.Join(", ", state.ReviewReasons.Take(3))
                : "";
        }

        private void SyncGtSummaryForImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            var item = _images.FirstOrDefault(i => i.ProcessedPath == imagePath);
            if (item == null)
                return;

            UpdateGtSummaryForImageItem(item, imagePath);
        }

        private static string DeriveReviewStatusForItem(ImageItem item, ImageStateDto state)
        {
            if (state.HasManualYoloDecision || state.HasManualAnomalyDecision)
                return ReviewStatus.ReviewDone;

            string normalized = (state.ReviewStatus ?? "").Trim().ToLowerInvariant();
            if (normalized == ReviewStatus.ReviewNeeded ||
                normalized == ReviewStatus.AutoCandidate ||
                normalized == ReviewStatus.ReviewDone)
            {
                return normalized;
            }

            if (item.InferParseFailed)
                return ReviewStatus.ReviewNeeded;

            if (item.RequiresInfer && !item.HasInferFile)
                return ReviewStatus.ReviewNeeded;

            if (item.HasAiInfer)
                return item.AiConsensusHighConfidence ? ReviewStatus.AutoCandidate : ReviewStatus.ReviewNeeded;

            return ReviewStatus.None;
        }

        private void ApplyAnomalyDecisionToItem(ImageItem item, bool isNormal, bool refreshSummary = true)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ProcessedPath))
                return;

            _imageStateManager.SetNormal(item.ProcessedPath, isNormal);
            _anomalyService.Save(item.ProcessedPath, isNormal);

            item.IsNormal = isNormal;
            item.HasStateFile = true;
            item.ReviewStatus = ReviewStatus.ReviewDone;
            item.ReviewReasonText = "";

            if (refreshSummary)
                RefreshSummaryCounts();
        }

        private void EnsureSelectedImageVisible()
        {
            if (ImageListBox.SelectedItem is ImageItem selectedItem && IsVisibleInCurrentFilter(selectedItem))
                return;

            var firstVisible = _imageCollectionView?.Cast<object>()
                .OfType<ImageItem>()
                .FirstOrDefault();

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
            _isLoadingImage = true;
            try
            {
                if (ImageListBox.SelectedItem is not ImageItem item)
                {
                    NormalRadio.IsChecked = false;
                    AbnormalRadio.IsChecked = false;
                    return;
                }

                NormalRadio.IsChecked = item.IsNormal;
                AbnormalRadio.IsChecked = !item.IsNormal;
            }
            finally
            {
                _isLoadingImage = false;
            }
        }
    }
}
