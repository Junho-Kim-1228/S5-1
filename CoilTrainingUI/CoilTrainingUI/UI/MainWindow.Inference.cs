using CoilTrainingUI.Models;
using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

            UpdatePredictionOverlayVisibility(imagePath);

            SyncGtSummaryForImage(imagePath);
            RefreshSummaryCounts();

            SaveLabelsToStateJson(imagePath, markManualYoloDecision: false);
            RequestSaveLabelsDebounced(imagePath);

            MessageBox.Show(
                $"예측 박스 {predictionBoxes.Count}개를 GT 라벨로 적용했습니다.",
                "Apply Predictions to Labels",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
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

        private void UpdatePredictionFeatureUiState()
        {
            bool predictionAvailableInBatch = _currentBatchHasAnyInfer;

            ShowPredictionCheckBox.IsEnabled = predictionAvailableInBatch;
            if (!predictionAvailableInBatch)
                ShowPredictionCheckBox.IsChecked = false;

            PreLabelBatchMenuItem.IsEnabled = predictionAvailableInBatch;
            AutoApproveSafeMenuItem.IsEnabled = predictionAvailableInBatch;
            ShowReviewQueueMenuItem.IsEnabled = _images.Count > 0;

            bool canApplyCurrentImage =
                predictionAvailableInBatch &&
                ImageListBox.SelectedItem is ImageItem item &&
                item.HasAiInfer;

            ApplyPredictionsMenuItem.IsEnabled = canApplyCurrentImage;
        }

        private void TryRestoreLastLoadedBatch()
        {
            RefreshAllImagesFromTrainingInbox(preferredImagePath: null, preferredBatchRoot: null);
        }

        private string ResolveBatchImagePath(string batchFolder, ManifestItemDto item)
        {
            string byIdPath = IOPath.Combine(batchFolder, "images", $"{item.Id}.bmp");
            if (File.Exists(byIdPath))
                return byIdPath;

            string fromManifest = IOPath.IsPathRooted(item.ProcessedImage)
                ? item.ProcessedImage
                : IOPath.Combine(batchFolder, item.ProcessedImage);

            if (File.Exists(fromManifest))
                return fromManifest;

            throw new FileNotFoundException($"processed image를 찾을 수 없습니다. id={item.Id}", byIdPath);
        }

        private static string ResolveBatchInferJsonPath(string batchFolder, ManifestItemDto item)
        {
            if (string.IsNullOrWhiteSpace(item.InferJson))
                return IOPath.Combine(batchFolder, "inference", $"{item.Id}.infer.json");

            return IOPath.IsPathRooted(item.InferJson)
                ? item.InferJson
                : IOPath.Combine(batchFolder, item.InferJson);
        }

        private static string? ResolveBatchRawImagePath(string batchFolder, ManifestItemDto item)
        {
            if (!string.IsNullOrWhiteSpace(item.RawImage))
            {
                string configuredPath = IOPath.IsPathRooted(item.RawImage)
                    ? item.RawImage
                    : IOPath.Combine(batchFolder, item.RawImage);
                return File.Exists(configuredPath) ? configuredPath : null;
            }

            if (string.IsNullOrWhiteSpace(item.Id))
            {
                if (string.IsNullOrWhiteSpace(item.ProcessedImage))
                    return null;

                string processedFileName = IOPath.GetFileName(item.ProcessedImage);
                if (string.IsNullOrWhiteSpace(processedFileName))
                    return null;

                string byProcessedNamePath = IOPath.Combine(batchFolder, "raw", processedFileName);
                return File.Exists(byProcessedNamePath) ? byProcessedNamePath : null;
            }

            string byIdPath = IOPath.Combine(batchFolder, "raw", $"{item.Id}.bmp");
            if (File.Exists(byIdPath))
                return byIdPath;

            if (!string.IsNullOrWhiteSpace(item.ProcessedImage))
            {
                string processedFileName = IOPath.GetFileName(item.ProcessedImage);
                if (!string.IsNullOrWhiteSpace(processedFileName))
                {
                    string byProcessedNamePath = IOPath.Combine(batchFolder, "raw", processedFileName);
                    if (File.Exists(byProcessedNamePath))
                        return byProcessedNamePath;
                }
            }

            return null;
        }

        private sealed class InferMetaSummary
        {
            public bool HasInferFile { get; set; }
            public bool HasAiInfer { get; set; }
            public bool InferParseFailed { get; set; }
            public bool HasYoloDefect { get; set; }
            public bool IsAnomaNormal { get; set; } = true;
            public bool IsConsensusHighConfidence { get; set; }
            public double YoloMaxConf { get; set; }
            public double AnomaScore { get; set; }
            public int DentCount { get; set; }
            public int LooseCount { get; set; }
            public int OtherCount { get; set; }
        }

        private static InferMetaSummary EvaluateInferMetaFromInfer(string inferJsonPath)
        {
            var summary = new InferMetaSummary
            {
                HasInferFile = !string.IsNullOrWhiteSpace(inferJsonPath) && File.Exists(inferJsonPath)
            };

            if (!summary.HasInferFile)
                return summary;

            try
            {
                var infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
                var evaluation = PredictionConsensusPolicy.Evaluate(infer);
                summary.HasAiInfer = true;
                summary.HasYoloDefect = evaluation.YoloDefect;
                summary.IsAnomaNormal = !evaluation.AnomaDefect;
                summary.IsConsensusHighConfidence = !evaluation.RequiresReview;
                summary.YoloMaxConf = evaluation.YoloMaxConf;
                summary.AnomaScore = evaluation.AnomaScore;

                foreach (var detection in infer.Yolo?.Detections ?? Enumerable.Empty<DetectionDto>())
                {
                    if (!PredictionConsensusPolicy.IsUsableDetectionForDecision(detection))
                        continue;

                    string className = (detection.ClassName ?? "").Trim().ToLowerInvariant();
                    if (className == "dent")
                    {
                        summary.DentCount++;
                        continue;
                    }

                    if (className == "loose")
                    {
                        summary.LooseCount++;
                        continue;
                    }

                    summary.OtherCount++;
                }

                return summary;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AI status parse failed: {inferJsonPath}, {ex.Message}");
                summary.InferParseFailed = true;
                return summary;
            }
        }

        private InferenceBatchValidationResult ValidateBatchFolder(string batchFolder)
        {
            if (string.IsNullOrWhiteSpace(batchFolder) || !Directory.Exists(batchFolder))
                return InferenceBatchValidationResult.Fail("배치 폴더가 존재하지 않습니다.");

            string metaDir = IOPath.Combine(batchFolder, "meta");
            if (!Directory.Exists(metaDir))
                return InferenceBatchValidationResult.Fail("meta 폴더가 없습니다.");

            if (!File.Exists(IOPath.Combine(metaDir, "DONE.flag")))
                return InferenceBatchValidationResult.Fail("완성되지 않은 배치입니다. DONE.flag가 없습니다.");

            string manifestPath = IOPath.Combine(metaDir, "manifest.json");
            if (!File.Exists(manifestPath))
                return InferenceBatchValidationResult.Fail("manifest.json 파일이 없습니다.");

            ManifestDto manifest;
            try
            {
                manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
            }
            catch (Exception ex)
            {
                return InferenceBatchValidationResult.Fail($"manifest.json 파싱 실패: {ex.Message}");
            }

            bool requiresInfer = DetermineBatchRequiresInfer(batchFolder, manifest);
            var missingFiles = new List<string>();
            foreach (var item in manifest.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProcessedImage))
                {
                    missingFiles.Add($"[{item.Id}] processed_image가 비어 있음");
                    continue;
                }

                var processedPath = ResolveBatchRelativePath(batchFolder, item.ProcessedImage);
                if (!File.Exists(processedPath))
                    missingFiles.Add(item.ProcessedImage);

                if (!string.IsNullOrWhiteSpace(item.RawImage))
                {
                    var rawPath = ResolveBatchRelativePath(batchFolder, item.RawImage);
                    if (!File.Exists(rawPath))
                        missingFiles.Add(item.RawImage);
                }

                if (requiresInfer)
                {
                    if (string.IsNullOrWhiteSpace(item.InferJson))
                    {
                        missingFiles.Add($"[{item.Id}] infer_json가 비어 있음");
                        continue;
                    }

                    var inferPath = ResolveBatchRelativePath(batchFolder, item.InferJson);
                    if (!File.Exists(inferPath))
                        missingFiles.Add(item.InferJson);
                }
            }

            string previewIds = string.Join(", ", manifest.Items
                .Select(item => string.IsNullOrWhiteSpace(item.Id) ? "(no id)" : item.Id)
                .Take(3));

            string inferredBatchType = string.IsNullOrWhiteSpace(manifest.BatchType)
                ? (requiresInfer ? "inference" : "no_infer")
                : manifest.BatchType;

            if (missingFiles.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("배치 검증 실패");
                sb.AppendLine($"batch_type: {inferredBatchType}");
                sb.AppendLine($"총 item 수: {manifest.Items.Count}");
                sb.AppendLine($"누락 파일 개수: {missingFiles.Count}");
                sb.AppendLine("누락 파일 목록:");
                foreach (var item in missingFiles)
                    sb.AppendLine($"- {item}");
                sb.AppendLine($"첫 3개 id: {previewIds}");

                return new InferenceBatchValidationResult
                {
                    IsValid = false,
                    Message = sb.ToString().TrimEnd()
                };
            }

            return new InferenceBatchValidationResult
            {
                IsValid = true,
                Message = $"배치 검증 OK\nbatch_type: {inferredBatchType}\n총 item 수: {manifest.Items.Count}\n누락 파일 개수: 0\n첫 3개 id: {previewIds}"
            };
        }

        private static bool DetermineBatchRequiresInfer(string batchFolder, ManifestDto manifest)
        {
            string batchType = (manifest.BatchType ?? "").Trim().ToLowerInvariant();
            if (batchType == "no_infer")
                return false;

            if (batchType == "inference")
                return true;

            bool hasInferReference = manifest.Items.Any(item => !string.IsNullOrWhiteSpace(item.InferJson));
            if (hasInferReference)
                return true;

            string inferenceDir = IOPath.Combine(batchFolder, "inference");
            if (Directory.Exists(inferenceDir) &&
                Directory.EnumerateFiles(inferenceDir, "*.json", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }

            return false;
        }

        private static string ResolveBatchRelativePath(string batchFolder, string relativeOrAbsolutePath)
        {
            if (IOPath.IsPathRooted(relativeOrAbsolutePath))
                return relativeOrAbsolutePath;
            return IOPath.Combine(batchFolder, relativeOrAbsolutePath);
        }

        private void RenderPredictionOverlays(string imagePath)
        {
            ClearPredictionOverlays();

            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            if (!_inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath))
                return;

            if (!File.Exists(inferJsonPath))
                return;

            InferResultDto infer;
            try
            {
                infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Prediction overlay parse failed: {inferJsonPath}, {ex.Message}");
                return;
            }

            double canvasWidth = ImageCanvas.Width;
            double canvasHeight = ImageCanvas.Height;

            if (canvasWidth <= 1 || canvasHeight <= 1)
                return;

            foreach (var detection in infer.Yolo.Detections)
            {
                if (detection.BboxXywhNorm == null || detection.BboxXywhNorm.Length != 4)
                    continue;

                var cx = detection.BboxXywhNorm[0];
                var cy = detection.BboxXywhNorm[1];
                var bw = detection.BboxXywhNorm[2];
                var bh = detection.BboxXywhNorm[3];

                if (bw <= 0 || bh <= 0)
                    continue;

                if (double.IsNaN(cx) || double.IsNaN(cy) || double.IsNaN(bw) || double.IsNaN(bh))
                    continue;

                double left = (cx - bw / 2.0) * canvasWidth;
                double top = (cy - bh / 2.0) * canvasHeight;
                double width = bw * canvasWidth;
                double height = bh * canvasHeight;

                if (left < 0)
                {
                    width += left;
                    left = 0;
                }

                if (top < 0)
                {
                    height += top;
                    top = 0;
                }

                if (left + width > canvasWidth)
                    width = canvasWidth - left;

                if (top + height > canvasHeight)
                    height = canvasHeight - top;

                if (width <= 1 || height <= 1)
                    continue;

                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    // 예측 박스 가시성 강화: 더 진한 외곽선 + 약한 반투명 채움
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 153, 255)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 6, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(45, 0, 153, 255)),
                    IsHitTestVisible = false,
                    Tag = PredictionOverlayTag
                };

                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                Panel.SetZIndex(rect, 100);
                ImageCanvas.Children.Add(rect);
            }
        }

        private void UpdatePredictionOverlayVisibility(string? imagePath = null)
        {
            string? targetPath = imagePath ?? _currentImagePath;

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                ClearPredictionOverlays();
                return;
            }

            if (ShowPredictionCheckBox.IsChecked == true)
                RenderPredictionOverlays(targetPath);
            else
                ClearPredictionOverlays();
        }

        private void ClearPredictionOverlays()
        {
            var overlays = ImageCanvas.Children
                .OfType<Rectangle>()
                .Where(rect => Equals(rect.Tag, PredictionOverlayTag))
                .ToList();

            foreach (var overlay in overlays)
                ImageCanvas.Children.Remove(overlay);
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

        private void ResetImageDisplay()
        {
            _currentImagePath = null;
            MainImage.Source = null;
            _bboxManager.ClearAll();
            ClassComboBox.IsEnabled = false;
            SetClassComboBoxSelection(_activeDrawClass);
            UpdatePredictionFeatureUiState();
        }

    }
}
