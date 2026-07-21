using System;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System.Collections.Generic;
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
            if (!IsLoaded)
                return;
            if (_suppressFilterRefresh)
                return;

            ApplyImageFilters();
        }

        private void BatchFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;
            if (_suppressFilterRefresh)
                return;

            ApplyImageFilters();
        }

        private void ImageNameFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded)
                return;
            if (_suppressFilterRefresh)
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
            if (_suppressFilterRefresh)
                return;

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
                && PassReviewPriorityFilter(item)
                && PassDataQualityFilter(item);
        }

        private bool PassImageNameFilter(ImageItem item)
        {
            string keyword = (ImageNameFilterTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(keyword))
                return true;

            string fileName = item.FileName ?? "";
            return fileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool PassBatchFilter(ImageItem item)
        {
            string? selectedBatchName = BatchFilterComboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedBatchName) ||
                string.Equals(selectedBatchName, AllBatchFilterLabel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(item.BatchName, selectedBatchName, StringComparison.OrdinalIgnoreCase);
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
                foreach (var batchName in batchNames)
                    _batchFilterOptions.Add(batchName);

                string selectionToUse = !string.IsNullOrWhiteSpace(previousSelection) &&
                                        _batchFilterOptions.Any(option =>
                                            string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase))
                    ? previousSelection
                    : AllBatchFilterLabel;

                BatchFilterComboBox.SelectedItem = selectionToUse;
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

            bool hasAnyFilter = includeConfirmedNormal || includeConfirmedDefect || includeAiNormal || includeAiDefect;
            if (!hasAnyFilter)
                return true;

            if (includeConfirmedNormal && item.IsConfirmedNormal)
                return true;
            if (includeConfirmedDefect && item.IsConfirmedDefect)
                return true;
            if (includeAiNormal && !item.IsConfirmedDefect && item.HasAiInfer && !item.AiIsDefect)
                return true;
            if (includeAiDefect && !item.IsConfirmedDefect && item.HasAiInfer && item.AiIsDefect)
                return true;

            return false;
        }

        private bool PassDefectTypeFilter(ImageItem item)
        {
            bool includeNormal = IsChecked(DefectTypeNormalCheckBox);
            bool includeDent = IsChecked(DefectTypeDentCheckBox);
            bool includeLoose = IsChecked(DefectTypeLooseCheckBox);
            bool includeNoLabel = IsChecked(DefectTypeNoLabelCheckBox);

            bool hasAnyFilter = includeNormal || includeDent || includeLoose || includeNoLabel;
            if (!hasAnyFilter)
                return true;

            int totalGtLabels = item.GtDentCount + item.GtLooseCount + item.GtOtherCount;
            bool isConfirmedNormal = item.IsConfirmedNormal;
            bool isDentOnly = item.GtDentCount > 0 && item.GtLooseCount == 0 && item.GtOtherCount == 0;
            bool isLooseOnly = item.GtLooseCount > 0 && item.GtDentCount == 0 && item.GtOtherCount == 0;
            bool isConfirmedDefectWithoutGtLabel = item.IsConfirmedDefect && totalGtLabels == 0;

            if (includeNormal && isConfirmedNormal)
                return true;
            if (includeDent && isDentOnly)
                return true;
            if (includeLoose && isLooseOnly)
                return true;
            if (includeNoLabel && isConfirmedDefectWithoutGtLabel)
                return true;

            return false;
        }

        private bool PassReviewPriorityFilter(ImageItem item)
        {
            bool includeNeedsReview = IsChecked(ReviewNeedsCheckBox);
            bool includeAutoCandidate = IsChecked(ReviewAutoCandidateCheckBox);
            bool includeDone = IsChecked(ReviewDoneCheckBox);

            bool hasAnyFilter = includeNeedsReview || includeAutoCandidate || includeDone;
            if (!hasAnyFilter)
                return true;

            if (includeNeedsReview && item.NeedsReview)
                return true;
            if (includeAutoCandidate && item.AutoApproveCandidate)
                return true;
            if (includeDone && item.ReviewDone)
                return true;

            return false;
        }

        private bool PassDataQualityFilter(ImageItem item)
        {
            bool includeHealthy = IsChecked(QualityHealthyCheckBox);
            bool includeMissingInfer = IsChecked(QualityMissingInferCheckBox);
            bool includeInferParseFailed = IsChecked(QualityInferParseFailedCheckBox);
            bool includeMissingState = IsChecked(QualityMissingStateCheckBox);
            bool includeMissingRaw = IsChecked(QualityMissingRawCheckBox);

            bool hasAnyFilter = includeHealthy || includeMissingInfer || includeInferParseFailed || includeMissingState || includeMissingRaw;
            if (!hasAnyFilter)
                return true;

            if (includeHealthy && IsDataQualityHealthy(item))
                return true;
            if (includeMissingInfer && item.RequiresInfer && !item.HasInferFile)
                return true;
            if (includeInferParseFailed && item.InferParseFailed)
                return true;
            if (includeMissingState && !item.HasStateFile)
                return true;
            if (includeMissingRaw && !item.HasRawFile)
                return true;

            return false;
        }

        private static bool IsDataQualityHealthy(ImageItem item)
        {
            if (!item.HasStateFile)
                return false;
            if (item.RequiresInfer && !item.HasInferFile)
                return false;
            if (item.InferParseFailed)
                return false;
            return true;
        }

        private static bool IsChecked(CheckBox checkBox)
            => checkBox.IsChecked == true;

        private void RefreshSummaryCounts()
        {
            var uniqueImages = _images
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ProcessedPath))
                .GroupBy(item => item.ProcessedPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int total = uniqueImages.Count;
            int visible = _imageCollectionView?.Cast<object>().OfType<ImageItem>().Count() ?? total;

            int defect = uniqueImages.Count(item => item.IsConfirmedDefect);
            int normal = uniqueImages.Count(item => item.IsConfirmedNormal);

            TotalCountText.Text = $"총 {total}개";
            VisibleCountText.Text = $"필터 후 {visible}개";
            NormalCountText.Text = $"정상 {normal}개";
            DefectCountText.Text = $"불량 이미지 {defect}개";
            UpdateBatchReviewStatuses(uniqueImages);
        }

        private void UpdateBatchReviewStatuses(IReadOnlyList<ImageItem> images)
        {
            if (images == null || images.Count == 0)
                return;

            string inboxRoot = GetTrainingInboxRoot();
            var statuses = images
                .Where(item => !string.IsNullOrWhiteSpace(item.BatchKey))
                .GroupBy(item => item.BatchKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.All(item => item.ReviewDone) ? "reviewed" : "review_needed",
                    StringComparer.OrdinalIgnoreCase);
            BatchRegistryService.SetReviewStatuses(inboxRoot, statuses);
        }

        private void MarkFilteredAbnormal_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = (_imageCollectionView?.Cast<object>() ?? _images.Cast<object>())
                .OfType<ImageItem>()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ProcessedPath))
                .ToList();

            if (visibleItems.Count == 0)
            {
                MessageBox.Show(
                    "현재 필터 결과에 해당하는 이미지가 없습니다.",
                    "Filtered -> Abnormal",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"현재 필터 결과 {visibleItems.Count}개 이미지를 모두 Abnormal로 확정할까요?",
                "Filtered -> Abnormal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            _suppressFilterRefresh = true;
            try
            {
                foreach (var item in visibleItems)
                    ApplyAnomalyDecisionToItem(item, isNormal: false, refreshSummary: false);
            }
            finally
            {
                _suppressFilterRefresh = false;
            }

            ApplyImageFilters();
            EnsureSelectedImageVisible();
            SyncAnomalyRadioFromSelectedItem();

            MessageBox.Show(
                $"{visibleItems.Count}개 이미지를 Abnormal로 확정했습니다.",
                "Filtered -> Abnormal",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
