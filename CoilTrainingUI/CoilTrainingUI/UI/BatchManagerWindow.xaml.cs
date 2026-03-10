using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CoilTrainingUI
{
    public enum BatchManagerRequestedAction
    {
        None,
        TrainSelected
    }

    public partial class BatchManagerWindow : Window
    {
        private readonly string _inboxRoot;
        private readonly BatchLibraryService _batchLibraryService = new();
        private readonly BatchMergeService _batchMergeService;
        private readonly ObservableCollection<BatchLibraryItem> _batches = new();
        private bool _isRefreshing;

        public bool HasLibraryChanges { get; private set; }
        public BatchManagerRequestedAction RequestedAction { get; private set; }
        public IReadOnlyList<BatchLibraryItem> RequestedBatches { get; private set; } = Array.Empty<BatchLibraryItem>();
        public string? PreferredBatchRoot { get; private set; }

        public BatchManagerWindow(string inboxRoot, BatchMergeService batchMergeService)
        {
            InitializeComponent();

            _inboxRoot = inboxRoot;
            _batchMergeService = batchMergeService;
            BatchGrid.ItemsSource = _batches;

            RefreshBatches();
        }

        private void RefreshBatches()
        {
            _isRefreshing = true;
            var scan = _batchLibraryService.Scan(_inboxRoot, includeHidden: true);

            _batches.Clear();
            foreach (var batch in scan.Batches)
                _batches.Add(batch);

            UpdateSummary(scan.Skipped.Count);
            _isRefreshing = false;
        }

        private List<BatchLibraryItem> GetSelectedBatches()
        {
            return _batches.Where(batch => batch.IsSelected).ToList();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var batch in _batches)
                batch.IsSelected = false;
        }

        private void HiddenCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
                return;

            if (sender is not FrameworkElement element || element.DataContext is not BatchLibraryItem batch)
                return;

            BatchRegistryService.SetHidden(
                _inboxRoot,
                new[] { batch.BatchKey },
                hidden: batch.IsHidden,
                reason: batch.IsHidden ? "manual" : "");

            batch.HiddenReason = batch.IsHidden ? "manual" : "";
            HasLibraryChanges = true;
            UpdateSummary();
        }

        private void TrainSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedBatches();
            if (selected.Count == 0)
            {
                MessageBox.Show("학습할 배치를 하나 이상 선택하세요.");
                return;
            }

            RequestedAction = BatchManagerRequestedAction.TrainSelected;
            RequestedBatches = selected;
            DialogResult = true;
            Close();
        }

        private async void MergeSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedBatches();
            if (selected.Count < 2)
            {
                MessageBox.Show("병합할 배치를 2개 이상 선택하세요.");
                return;
            }

            var confirm = MessageBox.Show(
                "선택한 배치들을 새 병합 배치로 만들고, 원본 배치는 자동으로 숨김 처리할까요?",
                "Batch Merge",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirm != MessageBoxResult.Yes)
                return;

            var progressWindow = new OperationProgressWindow("배치 병합 진행")
            {
                Owner = this
            };
            progressWindow.UpdateProgress(0, "병합 준비 중...");
            progressWindow.Show();

            try
            {
                await Task.Delay(50);

                var progress = new Progress<BatchMergeProgressInfo>(info =>
                {
                    progressWindow.UpdateProgress(info.Percent, info.Status);
                    if (!string.IsNullOrWhiteSpace(info.LogLine))
                        progressWindow.AppendLog(info.LogLine);
                });

                var merged = await Task.Run(() =>
                    _batchMergeService.MergeSelectedBatches(_inboxRoot, selected, progress));
                BatchRegistryService.MarkMergedBatch(_inboxRoot, merged.MergedBatchKey, merged.SourceBatchKeys);

                HasLibraryChanges = true;
                PreferredBatchRoot = merged.MergedBatchPath;

                RefreshBatches();
                progressWindow.UpdateProgress(100, "병합 완료");

                MessageBox.Show(
                    $"병합 배치 생성 완료\n- 배치: {merged.MergedBatchKey}\n- 이미지 수: {merged.ItemCount}\n- 원본 {merged.SourceBatchKeys.Count}개 배치는 숨김 처리되었습니다.",
                    "Batch Merge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"배치 병합 실패:\n{ex.Message}",
                    "Batch Merge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            finally
            {
                progressWindow.Close();
            }
        }

        private void HideSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedBatches();
            if (selected.Count == 0)
            {
                MessageBox.Show("숨길 배치를 하나 이상 선택하세요.");
                return;
            }

            BatchRegistryService.SetHidden(
                _inboxRoot,
                selected.Select(batch => batch.BatchKey),
                hidden: true,
                reason: "manual");

            foreach (var batch in selected)
            {
                batch.IsHidden = true;
                batch.HiddenReason = "manual";
            }

            HasLibraryChanges = true;
            UpdateSummary();
        }

        private void UpdateSummary(int skippedCount = 0)
        {
            SummaryTextBlock.Text =
                $"총 {_batches.Count}개 배치 / 숨김 {_batches.Count(batch => batch.IsHidden)}개 / 표시 {_batches.Count(batch => !batch.IsHidden)}개";

            if (skippedCount > 0)
                SummaryTextBlock.Text += $" / 스킵 {skippedCount}개";
        }
    }
}
