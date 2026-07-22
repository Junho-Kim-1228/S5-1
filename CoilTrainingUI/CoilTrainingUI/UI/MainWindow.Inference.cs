using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private ReviewStateLoadResult LoadReviewForExplicitEdit(string imagePath)
        {
            ReviewStateLoadResult load = _reviewRepository.Load(imagePath);
            if (load.IsLegacyProjection)
            {
                ReviewMigrationReport report = _reviewMigrationService.Migrate(new[] { imagePath });
                if (report.Converted != 1 && report.AlreadyMigrated != 1)
                    throw new InvalidOperationException("기존 state.json을 안전하게 마이그레이션하지 못했습니다.");
                load = _reviewRepository.Load(imagePath);
            }

            if (load.ParseFailed)
                throw new InvalidDataException("검수 상태 파일을 읽을 수 없습니다: " + load.Message);
            return load;
        }

        private void AcceptAiDecision_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedReviewContext(out var item, out var prediction))
                return;
            if (!prediction.HasAnomaDecision || prediction.ParseFailed)
            {
                MessageBox.Show("수락할 Anoma 판정이 없습니다.", "AI 판정 수락");
                return;
            }

            try
            {
                ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                ReviewState next = _reviewWorkflow.AcceptAiDecision(current, prediction);
                _reviewRepository.Save(item.ProcessedPath, next);
                ReloadAfterExplicitReviewChange(item, reloadCanvas: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "AI 판정 수락", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AcceptPredictionBoxes_Click(object sender, RoutedEventArgs e)
            => AcceptPredictionBoxesForCurrentImage();

        private void AcceptPredictionBoxesForCurrentImage()
        {
            if (!TryGetSelectedReviewContext(out var item, out var prediction))
                return;
            if (!prediction.HasFile || prediction.ParseFailed)
            {
                MessageBox.Show("수락할 YOLO 예측 결과가 없습니다.", "AI 박스 수락");
                return;
            }

            var confirm = MessageBox.Show(
                $"AI 예측 박스 {prediction.YoloDetectionCount}개로 현재 확정 박스를 교체할까요?\n" +
                "이 작업은 사용자 동작으로 기록되며, 이후 박스를 수정할 수 있습니다.",
                "AI 박스 전체 수락",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                ReviewState next = _reviewWorkflow.AcceptPredictionBoxes(current, prediction);
                _reviewRepository.Save(item.ProcessedPath, next);
                ReloadAfterExplicitReviewChange(item, reloadCanvas: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "AI 박스 수락", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ConfirmBoxes_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            try
            {
                _bboxManager.ForceUpdateAll(ImageCanvas.Width, ImageCanvas.Height);
                var boxes = _imageStateManager.GetLabels(item.ProcessedPath)
                    .Select(box => new ReviewBox
                    {
                        ClassName = box.ClassName,
                        X = box.X,
                        Y = box.Y,
                        Width = box.Width,
                        Height = box.Height,
                        Source = "manual"
                    })
                    .ToList();
                ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                ReviewState edited = _reviewWorkflow.ReplaceBoxesAfterEdit(current, boxes);
                ReviewState confirmed = _reviewWorkflow.ConfirmBoxes(edited);
                _reviewRepository.Save(item.ProcessedPath, confirmed);
                ShowPredictionCheckBox.IsChecked = false;
                ReloadAfterExplicitReviewChange(item, reloadCanvas: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "박스 검수 완료", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExcludeFromTraining_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;
            if (MessageBox.Show(
                    $"{item.FileName}을 모든 학습 데이터에서 제외할까요?",
                    "학습 제외",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                _reviewRepository.Save(item.ProcessedPath, _reviewWorkflow.Exclude(current, "사용자 학습 제외"));
                ReloadAfterExplicitReviewChange(item, reloadCanvas: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "학습 제외", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void YoloBackgroundCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingImage || ImageListBox.SelectedItem is not ImageItem item)
                return;

            try
            {
                ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                ReviewState next = _reviewWorkflow.SetYoloBackground(
                    current,
                    YoloBackgroundCheckBox.IsChecked == true);
                _reviewRepository.Save(item.ProcessedPath, next);
                ReloadAfterExplicitReviewChange(item, reloadCanvas: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "YOLO 정상 배경", MessageBoxButton.OK, MessageBoxImage.Warning);
                SyncAnomalyRadioFromSelectedItem();
            }
        }

        private void ApplyPredictionsToLabelsFilteredImages_Click(object sender, RoutedEventArgs e)
        {
            var targets = (_imageCollectionView?.Cast<object>() ?? _images.Cast<object>())
                .OfType<ImageItem>()
                .Where(item => _predictionByImagePath.TryGetValue(item.ProcessedPath, out var prediction) &&
                               prediction.HasFile && !prediction.ParseFailed &&
                               (prediction.AnomaIsDefect || item.IsReviewConfirmedDefect))
                .ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show("필터 결과에 사용 가능한 YOLO 예측이 없습니다.");
                return;
            }

            if (MessageBox.Show(
                    $"필터된 이미지 {targets.Count}개의 AI 예측 박스를 명시적으로 수락할까요?",
                    "필터된 AI 박스 수락",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            int saved = 0;
            var failures = new List<string>();
            foreach (var item in targets)
            {
                try
                {
                    var prediction = _predictionByImagePath[item.ProcessedPath];
                    ReviewState current = LoadReviewForExplicitEdit(item.ProcessedPath).State;
                    _reviewRepository.Save(item.ProcessedPath, _reviewWorkflow.AcceptPredictionBoxes(current, prediction));
                    saved++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.FileName}: {ex.Message}");
                }
            }

            string? selectedPath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
            RefreshAllImagesFromTrainingInbox(selectedPath, _currentBatchRoot);
            MessageBox.Show($"저장 {saved}개 / 실패 {failures.Count}개" +
                            (failures.Count > 0 ? "\n" + string.Join("\n", failures.Take(10)) : ""));
        }

        private void PreLabelBatchFromPredictions_Click(object sender, RoutedEventArgs e)
            => ApplyPredictionsToLabelsFilteredImages_Click(sender, e);

        private void ShowReviewQueue_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilterRefresh = true;
            try
            {
                ReviewUnreviewedCheckBox.IsChecked = true;
                ReviewReviewingCheckBox.IsChecked = true;
                ReviewConfirmedNormalCheckBox.IsChecked = false;
                ReviewConfirmedDefectCheckBox.IsChecked = false;
                ReviewExcludedCheckBox.IsChecked = false;
            }
            finally
            {
                _suppressFilterRefresh = false;
            }
            ApplyImageFilters();
            EnsureSelectedImageVisible();
        }

        private bool TryGetSelectedReviewContext(out ImageItem item, out PredictionSnapshot prediction)
        {
            item = null!;
            prediction = new PredictionSnapshot();
            if (ImageListBox.SelectedItem is not ImageItem selected)
                return false;
            item = selected;
            _predictionByImagePath.TryGetValue(item.ProcessedPath, out prediction!);
            prediction ??= new PredictionSnapshot();
            return true;
        }

        private void ReloadAfterExplicitReviewChange(ImageItem item, bool reloadCanvas)
        {
            UpdateGtSummaryForImageItem(item, item.ProcessedPath);
            if (reloadCanvas && string.Equals(_currentImagePath, item.ProcessedPath, StringComparison.OrdinalIgnoreCase))
                LoadImage(item.ProcessedPath);
            else
                SyncAnomalyRadioFromSelectedItem();
            ApplyImageFilters();
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
                if (dialog == null) return null;
                folderDialogType.GetProperty("Description")?.SetValue(dialog, description);
                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                    folderDialogType.GetProperty("SelectedPath")?.SetValue(dialog, initialPath);
                var showResult = folderDialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
                return Equals(showResult?.ToString(), "OK")
                    ? folderDialogType.GetProperty("SelectedPath")?.GetValue(dialog) as string
                    : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더 선택 실패: {ex.Message}");
                return null;
            }
            finally
            {
                if (dialog is IDisposable disposable) disposable.Dispose();
            }
        }

        private void UpdateDataSourceUiState() => UpdatePredictionFeatureUiState();

        private void TryRestoreLastLoadedBatch()
            => RefreshAllImagesFromTrainingInbox(preferredImagePath: null, preferredBatchRoot: null);

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            string fullPath = IOPath.GetFullPath(path)
                .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar) + IOPath.DirectorySeparatorChar;
            string fullRoot = IOPath.GetFullPath(rootPath)
                .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar) + IOPath.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
