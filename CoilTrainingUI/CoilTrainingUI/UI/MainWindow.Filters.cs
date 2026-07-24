using CoilTrainingUI.Models;
using System;
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
            ExcludedCountText.Text = $"학습 사용 OFF {uniqueImages.Count(item => item.IsReviewExcluded)}";
            AutoAcceptedCountText.Text = $"AI 자동수락 {uniqueImages.Count(item => item.IsAutoAccepted)}";
            AnomaTrainEligibleCountText.Text = $"Anoma 학습 {uniqueImages.Count(item => item.AnomaTrainingEligible)}";
            AnomaEvalEligibleCountText.Text = $"Anoma 평가 {uniqueImages.Count(item => item.AnomaEvaluationEligible)}";
            YoloPositiveEligibleCountText.Text = $"YOLO 양성 {uniqueImages.Count(item => item.YoloPositiveEligible)}";
            YoloBackgroundCandidateCountText.Text =
                $"YOLO 배경 후보 {uniqueImages.Count(item => item.YoloBackgroundEligible)}";
            YoloExcludedNoBoxCountText.Text =
                $"YOLO 박스 없는 불량 제외 {uniqueImages.Count(item => item.YoloExcludedNoBoxDefect)}";
            YoloLowConfidenceBoxReviewCountText.Text =
                $"YOLO 저신뢰 박스 검수 필요 {uniqueImages.Count(item => item.YoloLowConfidenceBoxReviewRequired)}";
        }

        private void AcceptFilteredAnomaDecisions_Click(object sender, RoutedEventArgs e)
        {
            var targets = (_imageCollectionView?.Cast<object>() ?? _images.Cast<object>())
                .OfType<ImageItem>()
                .Where(item => !string.IsNullOrWhiteSpace(item.ProcessedPath) &&
                               _predictionByImagePath.TryGetValue(item.ProcessedPath, out var prediction) &&
                               prediction.HasAnomaDecision &&
                               !prediction.ParseFailed)
                .GroupBy(item => item.ProcessedPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(
                    "현재 필터 결과에 수락 가능한 Anoma 판정이 없습니다.",
                    "필터 전체 Anoma 판정 수락");
                return;
            }

            int normalCount = targets.Count(item =>
                !_predictionByImagePath[item.ProcessedPath].AnomaIsDefect);
            int defectCount = targets.Count - normalCount;
            if (MessageBox.Show(
                    $"현재 필터 결과 중 {targets.Count}개의 Anoma 판정을 수락할까요?\n\n" +
                    $"정상 확정 예정: {normalCount}개\n" +
                    $"불량 확정 예정: {defectCount}개\n\n" +
                    "기존 사용자 판정이 있으면 Anoma 판정으로 변경됩니다.",
                    "필터 전체 Anoma 판정 수락",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            string? selectedPath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
            int saved = 0;
            var failures = new List<string>();
            _suppressFilterRefresh = true;
            try
            {
                foreach (ImageItem item in targets)
                {
                    try
                    {
                        var prediction = _predictionByImagePath[item.ProcessedPath];
                        var current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                        _reviewRepository.Save(
                            item.ProcessedPath,
                            _reviewWorkflow.AcceptAiDecision(current, prediction));
                        saved++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{item.FileName}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _suppressFilterRefresh = false;
            }

            RefreshAllImagesFromTrainingInbox(selectedPath, _currentBatchRoot);
            MessageBox.Show(
                $"Anoma 판정 수락: {saved}개 / 실패: {failures.Count}개" +
                (failures.Count > 0 ? "\n\n" + string.Join("\n", failures.Take(10)) : ""),
                "필터 전체 Anoma 판정 수락");
        }
    }
}
