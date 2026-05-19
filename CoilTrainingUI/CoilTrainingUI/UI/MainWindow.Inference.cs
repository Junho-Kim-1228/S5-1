using CoilTrainingUI.Models;
using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void PreLabelBatchFromPredictions_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0)
            {
                MessageBox.Show("처리할 이미지가 없습니다.", "Pre-label Batch", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "현재 로드된 이미지 전체에 대해 infer 예측을 기반으로 사전 라벨링을 수행할까요?\n기존 수동 GT는 기본적으로 유지됩니다.",
                "Pre-label Batch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirm != MessageBoxResult.Yes)
                return;

            var preferredImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
            var targets = BuildBatchPredictionTargets();
            var summary = _predictionReviewService.PreLabelBatch(targets, overwriteExistingLabels: false);

            RefreshAllImagesFromTrainingInbox(preferredImagePath, _currentBatchRoot);

            MessageBox.Show(
                BuildBatchReviewSummaryMessage("Batch Pre-label 완료", summary),
                "Pre-label Batch",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void AutoApproveSafeNormals_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0)
            {
                MessageBox.Show("처리할 이미지가 없습니다.", "Auto-Approve Safe Normals", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "YOLO/Anoma 판정이 일치하고 신뢰도 기준을 만족한 정상 이미지만 자동으로 정상 확정할까요?\n" +
                $"- YOLO Defect conf >= {PredictionConsensusPolicy.YoloDefectMinConf:0.00}\n" +
                $"- Anoma anomaly score >= {PredictionConsensusPolicy.AnomaAnomalyMinScore:0.00}\n" +
                $"- Anoma normal score <= {PredictionConsensusPolicy.AnomaNormalMaxScore:0.00}",
                "Auto-Approve Safe Normals",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirm != MessageBoxResult.Yes)
                return;

            var preferredImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
            var targets = BuildBatchPredictionTargets();
            var summary = _predictionReviewService.AutoApproveSafeNormals(targets);

            RefreshAllImagesFromTrainingInbox(preferredImagePath, _currentBatchRoot);

            MessageBox.Show(
                BuildBatchReviewSummaryMessage("Auto-Approve 완료", summary),
                "Auto-Approve Safe Normals",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void ShowReviewQueue_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilterRefresh = true;
            try
            {
                StatusConfirmedNormalCheckBox.IsChecked = false;
                StatusConfirmedDefectCheckBox.IsChecked = false;
                StatusAiNormalCheckBox.IsChecked = false;
                StatusAiDefectCheckBox.IsChecked = false;

                DefectTypeNormalCheckBox.IsChecked = false;
                DefectTypeDentCheckBox.IsChecked = false;
                DefectTypeLooseCheckBox.IsChecked = false;
                DefectTypeNoLabelCheckBox.IsChecked = false;

                QualityHealthyCheckBox.IsChecked = false;
                QualityMissingInferCheckBox.IsChecked = false;
                QualityInferParseFailedCheckBox.IsChecked = false;
                QualityMissingStateCheckBox.IsChecked = false;
                QualityMissingRawCheckBox.IsChecked = false;

                ReviewNeedsCheckBox.IsChecked = true;
                ReviewAutoCandidateCheckBox.IsChecked = false;
                ReviewDoneCheckBox.IsChecked = false;
            }
            finally
            {
                _suppressFilterRefresh = false;
            }

            ApplyImageFilters();

            var firstVisible = _imageCollectionView?.Cast<object>()
                .OfType<ImageItem>()
                .FirstOrDefault();
            if (firstVisible != null)
            {
                ImageListBox.SelectedItem = firstVisible;
                ImageListBox.ScrollIntoView(firstVisible);
            }
        }

        private void ApplyPredictionsToLabelsCurrentImage_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item || string.IsNullOrWhiteSpace(item.ProcessedPath))
            {
                MessageBox.Show(
                    "현재 선택된 이미지가 없습니다.",
                    "Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            string imagePath = item.ProcessedPath;

            if (!item.HasAiInfer)
            {
                MessageBox.Show(
                    "현재 이미지에는 사용할 예측(infer.json)이 없습니다.",
                    "Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (!string.Equals(_currentImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                LoadImage(imagePath);

            if (!_inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath))
            {
                MessageBox.Show(
                    "현재 이미지에 연결된 infer.json이 없습니다.",
                    "Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            InferResultDto infer;
            try
            {
                infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"infer.json 로드 실패:\n{inferJsonPath}\n{ex.Message}",
                    "Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            var predictionBoxes = ConvertDetectionsToGtBoxes(infer.Yolo?.Detections);
            if (predictionBoxes.Count == 0)
            {
                string anomaDecision = infer.Anoma?.Decision ?? "(none)";
                MessageBox.Show(
                    $"YOLO 예측 박스가 0개라서 GT로 복사할 수 없습니다.\nAnoma decision: {anomaDecision}",
                    "Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            int existingGtCount = _imageStateManager.GetLabels(imagePath).Count;
            if (existingGtCount > 0)
            {
                var overwrite = MessageBox.Show(
                    $"현재 이미지에 GT 라벨이 {existingGtCount}개 있습니다.\n기존 GT를 삭제하고 예측을 적용할까요?",
                    "Apply Predictions to Labels",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (overwrite != MessageBoxResult.Yes)
                    return;

                _imageStateManager.ClearLabels(imagePath);
                _bboxManager.ClearAll();
                RenderPredictionOverlays(imagePath);
            }

            var mutableLabels = _imageStateManager.GetMutableLabels(imagePath);
            foreach (var bbox in predictionBoxes)
            {
                mutableLabels.Add(bbox);
                _bboxManager.AddFromModel(bbox, ImageCanvas.Width, ImageCanvas.Height);
            }

            ApplyAnomalyDecisionToItem(item, isNormal: false, refreshSummary: false);
            UpdatePredictionOverlayVisibility(imagePath);

            SyncGtSummaryForImage(imagePath);
            RefreshSummaryCounts();

            SaveLabelsToStateJson(imagePath, markManualYoloDecision: true);
            RequestSaveLabelsDebounced(imagePath);

            MessageBox.Show(
                $"예측 박스 {predictionBoxes.Count}개를 GT 라벨로 적용했습니다.",
                "Apply Predictions to Labels",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void ApplyPredictionsToLabelsFilteredImages_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = (_imageCollectionView?.Cast<object>() ?? _images.Cast<object>())
                .OfType<ImageItem>()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ProcessedPath))
                .ToList();

            if (visibleItems.Count == 0)
            {
                MessageBox.Show(
                    "현재 필터 결과에 해당하는 이미지가 없습니다.",
                    "Batch Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"현재 필터 결과 {visibleItems.Count}개 이미지에 대해 AI YOLO 예측 박스를 GT Boxes로 일괄 적용할까요?\n" +
                "기존 GT Boxes가 있으면 예측 박스로 덮어쓰고, 박스가 적용된 이미지는 Abnormal로 확정됩니다.",
                "Batch Apply Predictions to Labels",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            string? selectedPath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
            var appliedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int appliedImages = 0;
            int appliedBoxes = 0;
            int overwrittenImages = 0;
            int skippedMissingInfer = 0;
            int skippedNoBoxes = 0;
            int parseFailed = 0;

            foreach (ImageItem item in visibleItems)
            {
                string imagePath = item.ProcessedPath;
                if (!_inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath)
                    || string.IsNullOrWhiteSpace(inferJsonPath)
                    || !File.Exists(inferJsonPath))
                {
                    skippedMissingInfer++;
                    continue;
                }

                InferResultDto infer;
                try
                {
                    infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
                }
                catch
                {
                    parseFailed++;
                    continue;
                }

                var predictionBoxes = ConvertDetectionsToGtBoxes(infer.Yolo?.Detections);
                if (predictionBoxes.Count == 0)
                {
                    skippedNoBoxes++;
                    continue;
                }

                ImageStateDto state = _stateService.Load(imagePath);
                state.Labels ??= new List<LabelDto>();
                if (state.Labels.Count > 0)
                    overwrittenImages++;

                state.Labels.Clear();
                foreach (DetectionDto detection in infer.Yolo?.Detections ?? new List<DetectionDto>())
                {
                    if (!TryConvertDetectionToBoundingBox(detection, out var bbox))
                        continue;

                    state.Labels.Add(new LabelDto
                    {
                        ClassName = bbox.ClassName,
                        X = bbox.X,
                        Y = bbox.Y,
                        Width = bbox.Width,
                        Height = bbox.Height,
                        Source = "auto_infer",
                        InferConf = detection.Conf
                    });
                }

                if (state.Labels.Count == 0)
                {
                    skippedNoBoxes++;
                    continue;
                }

                state.IsNormal = false;
                state.HasManualAnomalyDecision = true;
                state.HasManualYoloDecision = true;
                state.ReviewStatus = ReviewStatus.ReviewDone;
                state.ReviewReasons.Clear();
                state.ReviewedAt = DateTime.UtcNow;
                _stateService.Save(imagePath, state);

                _imageStateManager.SetNormal(imagePath, isNormal: false);
                _imageStateManager.ClearLabels(imagePath);
                var mutableLabels = _imageStateManager.GetMutableLabels(imagePath);
                foreach (BoundingBox bbox in predictionBoxes)
                    mutableLabels.Add(bbox);

                item.IsNormal = false;
                item.HasStateFile = true;
                UpdateGtSummaryForImageItem(item, imagePath);

                appliedImages++;
                appliedBoxes += state.Labels.Count;
                appliedPaths.Add(imagePath);
            }

            if (!string.IsNullOrWhiteSpace(selectedPath) && appliedPaths.Contains(selectedPath))
                LoadImage(selectedPath);

            ApplyImageFilters();
            EnsureSelectedImageVisible();
            RefreshSummaryCounts();

            MessageBox.Show(
                "필터된 이미지 예측 박스 일괄 적용 완료\n" +
                $"- 적용 이미지: {appliedImages}\n" +
                $"- 적용 박스: {appliedBoxes}\n" +
                $"- 기존 GT 덮어쓴 이미지: {overwrittenImages}\n" +
                $"- infer 없음 스킵: {skippedMissingInfer}\n" +
                $"- 예측 박스 없음 스킵: {skippedNoBoxes}\n" +
                $"- infer 파싱 실패: {parseFailed}",
                "Batch Apply Predictions to Labels",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private IReadOnlyList<BatchPredictionApplyTarget> BuildBatchPredictionTargets()
        {
            return _images
                .Select(item =>
                {
                    _inferJsonByImagePath.TryGetValue(item.ProcessedPath, out var inferJsonPath);
                    return new BatchPredictionApplyTarget
                    {
                        ImagePath = item.ProcessedPath,
                        InferJsonPath = inferJsonPath ?? "",
                        RequiresInfer = item.RequiresInfer
                    };
                })
                .ToList();
        }

        private static string BuildBatchReviewSummaryMessage(string title, BatchPredictionApplySummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"- 대상 이미지: {summary.TotalTargets}");
            sb.AppendLine($"- 사전 라벨링 적용: {summary.PreLabeled}");
            sb.AppendLine($"- 자동 정상 확정: {summary.AutoApprovedNormals}");
            sb.AppendLine($"- 검수 필요 표시: {summary.MarkedReviewNeeded}");
            sb.AppendLine($"- 자동 확정 후보 표시: {summary.MarkedAutoCandidate}");
            sb.AppendLine($"- 수동 확정이라 스킵: {summary.SkippedManual}");
            sb.AppendLine($"- infer 없음 스킵: {summary.SkippedMissingInfer}");
            sb.AppendLine($"- infer 파싱 실패: {summary.ParseFailed}");
            return sb.ToString().TrimEnd();
        }

        private List<BoundingBox> ConvertDetectionsToGtBoxes(IReadOnlyList<DetectionDto>? detections)
        {
            var boxes = new List<BoundingBox>();
            if (detections == null)
                return boxes;

            foreach (var detection in detections)
            {
                if (TryConvertDetectionToBoundingBox(detection, out var bbox))
                    boxes.Add(bbox);
            }

            return boxes;
        }

        private bool TryConvertDetectionToBoundingBox(DetectionDto detection, out BoundingBox bbox)
        {
            bbox = new BoundingBox();

            if (detection.BboxXywhNorm == null || detection.BboxXywhNorm.Length != 4)
                return false;

            double cx = detection.BboxXywhNorm[0];
            double cy = detection.BboxXywhNorm[1];
            double bw = detection.BboxXywhNorm[2];
            double bh = detection.BboxXywhNorm[3];

            if (!IsFinite(cx) || !IsFinite(cy) || !IsFinite(bw) || !IsFinite(bh))
                return false;

            if (bw <= 0 || bh <= 0)
                return false;

            double left = Math.Clamp(cx - (bw / 2.0), 0.0, 1.0);
            double right = Math.Clamp(cx + (bw / 2.0), 0.0, 1.0);
            double top = Math.Clamp(cy - (bh / 2.0), 0.0, 1.0);
            double bottom = Math.Clamp(cy + (bh / 2.0), 0.0, 1.0);

            double width = right - left;
            double height = bottom - top;
            if (width <= 0 || height <= 0)
                return false;

            string className = NormalizeClassName(detection.ClassName);

            bbox = new BoundingBox
            {
                ClassName = className,
                X = (left + right) / 2.0,
                Y = (top + bottom) / 2.0,
                Width = width,
                Height = height
            };

            return true;
        }

        private string NormalizeClassName(string? className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return "dent";

            string normalized = className.Trim().ToLowerInvariant();
            return _classToId.ContainsKey(normalized) ? normalized : "dent";
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

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

        private void UpdateDataSourceUiState()
        {
            UpdatePredictionFeatureUiState();
        }

        private void TryRestoreLastLoadedBatch()
        {
            RefreshAllImagesFromTrainingInbox(preferredImagePath: null, preferredBatchRoot: null);
        }

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            var fullPath = IOPath.GetFullPath(path)
                .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar)
                + IOPath.DirectorySeparatorChar;
            var fullRoot = IOPath.GetFullPath(rootPath)
                .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar)
                + IOPath.DirectorySeparatorChar;

            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
