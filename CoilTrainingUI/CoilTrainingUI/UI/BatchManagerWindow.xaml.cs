using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

    public sealed class BatchLibraryChangedEventArgs : EventArgs
    {
        public string? PreferredBatchRoot { get; init; }
    }

    public partial class BatchManagerWindow : Window
    {
        private readonly string _inboxRoot;
        private readonly string _projectRoot;
        private readonly BatchLibraryService _batchLibraryService = new();
        private readonly BatchMergeService _batchMergeService;
        private readonly InferenceBatchImportService _batchImportService = new();
        private readonly ObservableCollection<BatchLibraryItem> _batches = new();
        private bool _isRefreshing;

        public bool HasLibraryChanges { get; private set; }
        public BatchManagerRequestedAction RequestedAction { get; private set; }
        public IReadOnlyList<BatchLibraryItem> RequestedBatches { get; private set; } = Array.Empty<BatchLibraryItem>();
        public string? PreferredBatchRoot { get; private set; }
        public event EventHandler<BatchLibraryChangedEventArgs>? LibraryChanged;

        public BatchManagerWindow(string inboxRoot, string projectRoot, BatchMergeService batchMergeService)
        {
            InitializeComponent();

            _inboxRoot = inboxRoot;
            _projectRoot = projectRoot;
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

        private void NotifyLibraryChanged(string? preferredBatchRoot = null)
        {
            HasLibraryChanges = true;
            PreferredBatchRoot = preferredBatchRoot;
            LibraryChanged?.Invoke(
                this,
                new BatchLibraryChangedEventArgs { PreferredBatchRoot = preferredBatchRoot });
        }

        private List<BatchLibraryItem> GetSelectedBatches()
        {
            return _batches.Where(batch => batch.IsSelected).ToList();
        }

        private void SelectBatchByRoot(string batchRoot)
        {
            if (string.IsNullOrWhiteSpace(batchRoot))
                return;

            string normalizedTarget = Path.GetFullPath(batchRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var target = _batches.FirstOrDefault(batch =>
                string.Equals(
                    Path.GetFullPath(batch.BatchRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return;

            target.IsSelected = true;
            BatchGrid.SelectedItem = target;
            BatchGrid.ScrollIntoView(target);
        }
        private async void ImportBatch_Click(object sender, RoutedEventArgs e)
        {
            string? selectedBatchFolder = TrySelectFolder("Import batch folder", _inboxRoot);
            if (string.IsNullOrWhiteSpace(selectedBatchFolder))
                return;

            var progressWindow = new OperationProgressWindow("배치 불러오기")
            {
                Owner = this
            };
            progressWindow.UpdateProgress(0, "배치 준비 중...");
            progressWindow.Show();

            try
            {
                await Task.Delay(50);

                string batchToLoad;
                int itemCount;

                if (IsPathUnderRoot(selectedBatchFolder, _inboxRoot))
                {
                    progressWindow.UpdateProgress(15, "기존 배치 검증 중...");
                    var validation = await Task.Run(() => BatchFolderValidationService.Validate(selectedBatchFolder));
                    if (!validation.IsValid)
                    {
                        MessageBox.Show(
                            validation.Message,
                            "Batch Import",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    batchToLoad = selectedBatchFolder;
                    itemCount = validation.TotalItemCount;
                    progressWindow.UpdateProgress(100, "배치 불러오기 완료");
                }
                else
                {
                    progressWindow.UpdateProgress(5, "원본 배치 검증 중...");
                    var validation = await Task.Run(() => BatchFolderValidationService.Validate(selectedBatchFolder));
                    if (!validation.IsValid)
                    {
                        MessageBox.Show(
                            validation.Message,
                            "Batch Import",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    var progress = new Progress<InferenceBatchImportProgressInfo>(info =>
                    {
                        progressWindow.UpdateProgress(info.Percent, info.Status);
                        if (!string.IsNullOrWhiteSpace(info.LogLine))
                            progressWindow.AppendLog(info.LogLine);
                    });

                    var imported = await Task.Run(() =>
                        _batchImportService.Import(selectedBatchFolder, _projectRoot, _inboxRoot, progress));
                    batchToLoad = imported.ImportedPath;
                    itemCount = imported.ItemCount;
                }

                RefreshBatches();
                SelectBatchByRoot(batchToLoad);
                NotifyLibraryChanged(batchToLoad);

                MessageBox.Show(
                    $"배치 불러오기 완료\n- 경로: {batchToLoad}\n- 이미지 수: {itemCount}",
                    "Batch Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"배치 불러오기 실패:\n{ex.Message}",
                    "Batch Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                progressWindow.Close();
            }
        }

        private void RenameSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedBatches();
            if (selected.Count != 1)
            {
                MessageBox.Show("이름을 바꿀 배치를 1개만 선택하세요.");
                return;
            }

            var target = selected[0];
            var dialog = new BatchRenameWindow(target.BatchId)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            string newBatchName = dialog.BatchName;
            if (string.Equals(target.BatchId, newBatchName, StringComparison.Ordinal))
                return;

            if (_batches.Any(batch =>
                    !ReferenceEquals(batch, target) &&
                    string.Equals(batch.BatchId, newBatchName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "같은 배치명이 이미 있습니다. 다른 이름을 입력하세요.",
                    "배치명 변경",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                BatchManifestService.UpdateBatchId(target.BatchRoot, newBatchName);
                RefreshBatches();
                SelectBatchByRoot(target.BatchRoot);
                NotifyLibraryChanged(target.BatchRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"배치명 변경 실패:\n{ex.Message}",
                    "배치명 변경",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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
            NotifyLibraryChanged(batch.BatchRoot);
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
                MessageBoxImage.Question);

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

                RefreshBatches();
                SelectBatchByRoot(merged.MergedBatchPath);
                NotifyLibraryChanged(merged.MergedBatchPath);
                progressWindow.UpdateProgress(100, "병합 완료");

                MessageBox.Show(
                    $"병합 배치 생성 완료\n- 배치: {merged.MergedBatchKey}\n- 이미지 수: {merged.ItemCount}\n- 원본 {merged.SourceBatchKeys.Count}개 배치는 숨김 처리되었습니다.",
                    "Batch Merge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"배치 병합 실패:\n{ex.Message}",
                    "Batch Merge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

            NotifyLibraryChanged(PreferredBatchRoot);
            UpdateSummary();
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedBatches();
            if (selected.Count == 0)
            {
                MessageBox.Show("삭제할 배치를 하나 이상 선택하세요.");
                return;
            }

            int mergedCount = selected.Count(batch => string.Equals(batch.BatchKind, "merged", StringComparison.OrdinalIgnoreCase));
            var confirm = MessageBox.Show(
                $"선택한 배치 {selected.Count}개를 삭제할까요?\n\n- 병합 배치: {mergedCount}개\n- 일반 배치: {selected.Count - mergedCount}개\n\n삭제된 배치는 복구되지 않습니다.",
                "Batch Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var deletedKeys = new List<string>();
            var deletedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failedMessages = new List<string>();

            foreach (var batch in selected)
            {
                try
                {
                    if (!IsPathUnderRoot(batch.BatchRoot, _inboxRoot))
                        throw new InvalidOperationException("training_inbox 외부 경로는 삭제할 수 없습니다.");

                    if (!Directory.Exists(batch.BatchRoot))
                    {
                        deletedKeys.Add(batch.BatchKey);
                        deletedRoots.Add(batch.BatchRoot);
                        continue;
                    }

                    Directory.Delete(batch.BatchRoot, recursive: true);
                    deletedKeys.Add(batch.BatchKey);
                    deletedRoots.Add(batch.BatchRoot);
                }
                catch (Exception ex)
                {
                    failedMessages.Add($"- {batch.BatchId}: {ex.Message}");
                }
            }

            if (deletedKeys.Count > 0)
            {
                BatchRegistryService.DeleteBatches(_inboxRoot, deletedKeys);
                if (!string.IsNullOrWhiteSpace(PreferredBatchRoot) && deletedRoots.Contains(PreferredBatchRoot))
                    PreferredBatchRoot = null;

                RefreshBatches();
                NotifyLibraryChanged(PreferredBatchRoot);
            }

            if (failedMessages.Count == 0)
            {
                MessageBox.Show(
                    $"배치 삭제 완료\n- 삭제: {deletedKeys.Count}개",
                    "Batch Delete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(
                $"배치 삭제 일부 완료\n- 삭제: {deletedKeys.Count}개\n- 실패: {failedMessages.Count}개\n\n{string.Join(Environment.NewLine, failedMessages.Take(10))}",
                "Batch Delete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private string? TrySelectFolder(string description, string? initialPath = null)
        {
            var folderDialogType = Type.GetType("System.Windows.Forms.FolderBrowserDialog, System.Windows.Forms");
            if (folderDialogType == null)
            {
                MessageBox.Show("폴더 선택 대화상자를 사용할 수 없습니다. (System.Windows.Forms 로드 실패)");
                return null;
            }

            object? dialog = null;
            try
            {
                dialog = Activator.CreateInstance(folderDialogType);
                if (dialog == null)
                    return null;

                folderDialogType.GetProperty("Description")?.SetValue(dialog, description);

                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                    folderDialogType.GetProperty("SelectedPath")?.SetValue(dialog, initialPath);

                var showMethod = folderDialogType.GetMethod("ShowDialog", Type.EmptyTypes);
                if (showMethod == null)
                {
                    MessageBox.Show("폴더 선택 대화상자 ShowDialog를 찾을 수 없습니다.");
                    return null;
                }

                var showResult = showMethod.Invoke(dialog, null);
                if (!Equals(showResult?.ToString(), "OK"))
                    return null;

                return folderDialogType.GetProperty("SelectedPath")?.GetValue(dialog) as string;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더 선택 실패: {ex.Message}");
                return null;
            }
            finally
            {
                if (dialog is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
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
