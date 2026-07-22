using CoilTrainingUI.Models;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void InitializeImageCollectionView()
        {
            _imageCollectionView = CollectionViewSource.GetDefaultView(_images);
            _imageCollectionView.Filter = FilterImageItem;
            ImageListBox.ItemsSource = _imageCollectionView;
        }

        private void ImageFilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _suppressFilterRefresh)
                return;
            ApplyImageFilters();
        }

        private void BatchFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _suppressFilterRefresh)
                return;
            ApplyImageFilters();
        }

        private void ImageNameFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _suppressFilterRefresh)
                return;
            ApplyImageFilters();
        }

        private void Images_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var oldItem in e.OldItems.OfType<ImageItem>())
                    oldItem.PropertyChanged -= ImageItem_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (var newItem in e.NewItems.OfType<ImageItem>())
                    newItem.PropertyChanged += ImageItem_PropertyChanged;
            }

            if (!_suppressFilterRefresh)
                ApplyImageFilters();
        }

        private void ImageItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_suppressFilterRefresh)
                ApplyImageFilters();
        }

        private void ApplyImageFilters()
        {
            _imageCollectionView?.Refresh();
            RefreshSummaryCounts();
        }

        private bool IsVisibleInCurrentFilter(ImageItem item)
        {
            if (_imageCollectionView == null)
                return true;

            return _imageCollectionView.Cast<object>()
                .OfType<ImageItem>()
                .Any(candidate => ReferenceEquals(candidate, item));
        }

        private bool FilterImageItem(object itemObj)
        {
            if (itemObj is not ImageItem item)
                return false;

            return PassImageNameFilter(item)
                && PassBatchFilter(item)
                && PassStatusFilter(item)
                && PassDefectTypeFilter(item)
                && PassReviewStateFilter(item)
                && PassDataQualityFilter(item);
        }

        private bool PassImageNameFilter(ImageItem item)
        {
            string keyword = (ImageNameFilterTextBox.Text ?? "").Trim();
            return string.IsNullOrWhiteSpace(keyword) ||
                   (item.FileName ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private bool PassBatchFilter(ImageItem item)
        {
            string? selectedBatchName = BatchFilterComboBox.SelectedItem as string;
            return string.IsNullOrWhiteSpace(selectedBatchName) ||
                   string.Equals(selectedBatchName, AllBatchFilterLabel, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.BatchName, selectedBatchName, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshBatchFilterOptions()
        {
            string? previousSelection = BatchFilterComboBox.SelectedItem as string;
            var batchNames = _images
                .Select(item => item.BatchName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool previousSuppress = _suppressFilterRefresh;
            _suppressFilterRefresh = true;
            try
            {
                _batchFilterOptions.Clear();
                _batchFilterOptions.Add(AllBatchFilterLabel);
                foreach (string batchName in batchNames)
                    _batchFilterOptions.Add(batchName);

                BatchFilterComboBox.SelectedItem = !string.IsNullOrWhiteSpace(previousSelection) &&
                                                   _batchFilterOptions.Any(option => string.Equals(
                                                       option,
                                                       previousSelection,
                                                       StringComparison.OrdinalIgnoreCase))
                    ? previousSelection
                    : AllBatchFilterLabel;
            }
            finally
            {
                _suppressFilterRefresh = previousSuppress;
            }
        }

        private bool PassStatusFilter(ImageItem item)
        {
            bool includeConfirmedNormal = IsChecked(StatusConfirmedNormalCheckBox);
            bool includeConfirmedDefect = IsChecked(StatusConfirmedDefectCheckBox);
            bool includeAiNormal = IsChecked(StatusAiNormalCheckBox);
            bool includeAiDefect = IsChecked(StatusAiDefectCheckBox);
            if (!(includeConfirmedNormal || includeConfirmedDefect || includeAiNormal || includeAiDefect))
                return true;

            if (includeConfirmedNormal && item.IsReviewConfirmedNormal)
                return true;
            if (includeConfirmedDefect && item.IsReviewConfirmedDefect)
                return true;

            bool lacksFinalDecision = !item.IsReviewConfirmedNormal && !item.IsReviewConfirmedDefect;
            if (includeAiNormal && lacksFinalDecision && item.HasAiInfer && !item.AiAnomaDefect)
                return true;
            if (includeAiDefect && lacksFinalDecision && item.HasAiInfer && item.AiAnomaDefect)
                return true;
            return false;
        }

        private bool PassDefectTypeFilter(ImageItem item)
        {
            bool includeNormal = IsChecked(DefectTypeNormalCheckBox);
            bool includeDent = IsChecked(DefectTypeDentCheckBox);
            bool includeLoose = IsChecked(DefectTypeLooseCheckBox);
            bool includeNoLabel = IsChecked(DefectTypeNoLabelCheckBox);
            if (!(includeNormal || includeDent || includeLoose || includeNoLabel))
                return true;

            int totalBoxes = item.GtDentCount + item.GtLooseCount + item.GtOtherCount;
            if (includeNormal && item.IsReviewConfirmedNormal)
                return true;
            if (includeDent && item.GtDentCount > 0)
                return true;
            if (includeLoose && item.GtLooseCount > 0)
                return true;
            if (includeNoLabel && item.IsReviewConfirmedDefect && totalBoxes == 0)
                return true;
            return false;
        }

        private bool PassReviewStateFilter(ImageItem item)
        {
            bool includeUnreviewed = IsChecked(ReviewUnreviewedCheckBox);
            bool includeReviewing = IsChecked(ReviewReviewingCheckBox);
            bool includeConfirmedNormal = IsChecked(ReviewConfirmedNormalCheckBox);
            bool includeConfirmedDefect = IsChecked(ReviewConfirmedDefectCheckBox);
            bool includeExcluded = IsChecked(ReviewExcludedCheckBox);
            if (!(includeUnreviewed || includeReviewing || includeConfirmedNormal ||
                  includeConfirmedDefect || includeExcluded))
            {
                return true;
            }

            return (includeUnreviewed && item.IsReviewUnreviewed) ||
                   (includeReviewing && item.IsReviewing) ||
                   (includeConfirmedNormal && item.IsReviewConfirmedNormal) ||
                   (includeConfirmedDefect && item.IsReviewConfirmedDefect) ||
                   (includeExcluded && item.IsReviewExcluded);
        }

        private bool PassDataQualityFilter(ImageItem item)
        {
            bool includeHealthy = IsChecked(QualityHealthyCheckBox);
            bool includeMissingInfer = IsChecked(QualityMissingInferCheckBox);
            bool includeInferParseFailed = IsChecked(QualityInferParseFailedCheckBox);
            bool includeMissingState = IsChecked(QualityMissingStateCheckBox);
            bool includeMissingRaw = IsChecked(QualityMissingRawCheckBox);
            if (!(includeHealthy || includeMissingInfer || includeInferParseFailed ||
                  includeMissingState || includeMissingRaw))
            {
                return true;
            }

            return (includeHealthy && IsDataQualityHealthy(item)) ||
                   (includeMissingInfer && item.RequiresInfer && !item.HasInferFile) ||
                   (includeInferParseFailed && item.InferParseFailed) ||
                   (includeMissingState && !item.HasStateFile) ||
                   (includeMissingRaw && !item.HasRawFile);
        }

        private static bool IsDataQualityHealthy(ImageItem item)
        {
            if (!item.HasStateFile || item.NeedsLegacyMigration)
                return false;
            if (item.RequiresInfer && !item.HasInferFile)
                return false;
            return !item.InferParseFailed;
        }

        private static bool IsChecked(CheckBox checkBox) => checkBox.IsChecked == true;

        private void RefreshSummaryCounts()
        {
            var uniqueImages = _images
                .Where(item => !string.IsNullOrWhiteSpace(item.ProcessedPath))
                .GroupBy(item => item.ProcessedPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int total = uniqueImages.Count;
            int visible = _imageCollectionView?.Cast<object>().OfType<ImageItem>().Count() ?? total;
            TotalCountText.Text = $"전체 {total}";
            VisibleCountText.Text = $"필터 후 {visible}";
            UnreviewedCountText.Text = $"미검수 {uniqueImages.Count(item => item.IsReviewUnreviewed)}";
            ReviewingCountText.Text = $"검수 중 {uniqueImages.Count(item => item.IsReviewing)}";
            ConfirmedNormalCountText.Text = $"정상 확정 {uniqueImages.Count(item => item.IsReviewConfirmedNormal)}";
            ConfirmedDefectCountText.Text = $"불량 확정 {uniqueImages.Count(item => item.IsReviewConfirmedDefect)}";
            ExcludedCountText.Text = $"학습 제외 {uniqueImages.Count(item => item.IsReviewExcluded)}";
            AnomaTrainEligibleCountText.Text = $"Anoma 학습 {uniqueImages.Count(item => item.AnomaTrainingEligible)}";
            AnomaEvalEligibleCountText.Text = $"Anoma 평가 {uniqueImages.Count(item => item.AnomaEvaluationEligible)}";
            YoloPositiveEligibleCountText.Text = $"YOLO 양성 {uniqueImages.Count(item => item.YoloPositiveEligible)}";
            YoloExcludedNoBoxCountText.Text =
                $"YOLO 박스 없는 불량 제외 {uniqueImages.Count(item => item.YoloExcludedNoBoxDefect)}";
        }

        private void MarkFilteredAbnormal_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = (_imageCollectionView?.Cast<object>() ?? _images.Cast<object>())
                .OfType<ImageItem>()
                .Where(item => !string.IsNullOrWhiteSpace(item.ProcessedPath))
                .ToList();
            if (visibleItems.Count == 0)
            {
                MessageBox.Show("현재 필터 결과에 해당하는 이미지가 없습니다.", "Filtered -> Abnormal");
                return;
            }

            if (MessageBox.Show(
                    $"현재 필터 결과 {visibleItems.Count}개 이미지를 모두 불량으로 확정할까요?",
                    "Filtered -> Abnormal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _suppressFilterRefresh = true;
            try
            {
                foreach (ImageItem item in visibleItems)
                    ApplyAnomalyDecisionToItem(item, isNormal: false, refreshSummary: false);
            }
            finally
            {
                _suppressFilterRefresh = false;
            }

            ApplyImageFilters();
            EnsureSelectedImageVisible();
            SyncAnomalyRadioFromSelectedItem();
            MessageBox.Show($"{visibleItems.Count}개 이미지를 불량으로 확정했습니다.", "Filtered -> Abnormal");
        }
    }
}
