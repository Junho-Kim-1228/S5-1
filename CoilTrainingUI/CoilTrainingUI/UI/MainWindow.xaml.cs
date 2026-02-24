using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using CoilTrainingUI.Models.InferenceBatch;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Reflection;
using IOPath = System.IO.Path;
using System.Text.Json;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private bool _isLoadingImage;

        private YoloLabelService _yoloService;
        private BoundingBoxManager _bboxManager;
        private readonly DatasetExportService _exportService = new();
        private readonly InferenceBatchImportService _inferenceBatchImportService = new();
        private CanvasInteractionManager _canvasInteractionManager;
        private ImageStateManager _imageStateManager;
        private AnomalyStateService _anomalyService;
        private RoiStateService _roiService;
        private BitmapSource _originalBitmap;
        private RoiPreprocessService _roiPreprocessService;
        private readonly ImageStateService _stateService = new();

        private FileSystemWatcher? _watcher;
        private readonly HashSet<string> _knownImages = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _watchLock = new(1, 1);
        private readonly string _defaultInputFolder = @"C:\Users\wnsgh\Desktop\input";
        private DataSourceKind _currentDataSource = DataSourceKind.LocalInput;
        private const bool RoiFeaturesEnabled = false;
        private readonly Dictionary<string, string> _inferJsonByImagePath = new(StringComparer.OrdinalIgnoreCase);
        private const string PredictionOverlayTag = "__prediction_overlay";

        private DispatcherTimer _labelSaveDebounceTimer;
        private string? _pendingSaveImagePath;
        private const int LabelSaveDebounceMs = 300;

        // 항상 원본은 유지
        private BitmapSource _rawBitmap;

        // ROI 적용된 "실제 사용 이미지"
        private BitmapSource _processedBitmap;

        private string _currentImagePath;


        private readonly Dictionary<string, int> _classToId = new()
        {
            { "dent", 0 },
            { "loose", 1 }
        };

        private ObservableCollection<ImageItem> _images
            = new ObservableCollection<ImageItem>();

        private enum DataSourceKind
        {
            LocalInput,
            ImportedBatch
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj)
        where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                    yield return t;

                foreach (T childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }


        private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Rectangle)
                return;

            // 🔥 이전 선택 완전 해제
            ClassComboBox.SelectedIndex = -1;
            ClassComboBox.IsEnabled = false;

            _bboxManager.ClearSelection();

            _canvasInteractionManager.StartDraw(
                e.GetPosition(ImageCanvas)
            );
        }


        private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _canvasInteractionManager.UpdateDraw(e.GetPosition(ImageCanvas));
        }

        private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var bbox = _canvasInteractionManager.EndDraw(
                ImageCanvas.Width,
                ImageCanvas.Height
            );

            if (bbox == null)
                return;

            // 1️⃣ 상태 저장
            _imageStateManager.AddLabel(_currentImagePath, bbox);

            // 2️⃣ 🔥 방금 만든 박스를 자동 선택 상태로 만들기
            _bboxManager.SelectLastCreated();

            // 3️⃣ 클래스 UI 활성화 + 기본값 반영
            ClassComboBox.IsEnabled = true;
            ClassComboBox.SelectedItem = ClassComboBox.Items
                .OfType<ComboBoxItem>()
                .First(i => i.Content.ToString() == bbox.ClassName);
            
            RequestSaveLabelsDebounced(_currentImagePath);


            SaveLabelsToStateJson(_currentImagePath);
            var selectedItem = _images.FirstOrDefault(i => i.FullPath == _currentImagePath);
            if (selectedItem != null)
            {
                selectedItem.HasLabel = true;
                ImageListBox.Items.Refresh();
            }
            RefreshSummaryCounts();
        }

        private void ImageCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Rectangle rect)
            {
                var bbox = _bboxManager.Select(
                    rect,
                    e.GetPosition(ImageCanvas)
                );

                if (bbox != null)
                {
                    ClassComboBox.IsEnabled = true;

                    // 🔥 핵심: 선택된 박스의 클래스 → ComboBox 반영
                    ClassComboBox.SelectedItem = ClassComboBox.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(i =>
                            i.Content?.ToString() == bbox.ClassName
                        );
                }

                e.Handled = true;
            }
        }

        private void ImageCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _bboxManager.Drag(
                e.GetPosition(ImageCanvas)
            );
        }

        private void ImageCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _bboxManager.EndDrag(
                ImageCanvas.Width,
                ImageCanvas.Height
            );

            // ✅ 드래그가 끝난 좌표를 state.json에 저장
            if (!string.IsNullOrEmpty(_currentImagePath))
            {
                _bboxManager.ForceUpdateAll(ImageCanvas.Width, ImageCanvas.Height);
                SaveLabelsToStateJson(_currentImagePath);
            }

            // 드래그 끝난 결과 저장(디바운스)
            if (!string.IsNullOrEmpty(_currentImagePath))
                RequestSaveLabelsDebounced(_currentImagePath);
        }

        private void ImageCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _canvasInteractionManager.OnMouseWheel(e);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _canvasInteractionManager.ZoomIn();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _canvasInteractionManager.ZoomOut();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            var removedBBox = _bboxManager.DeleteSelected();
            if (removedBBox == null)
                return;

            // 1️⃣ 메모리 상태에서 제거
            _imageStateManager.RemoveLabel(_currentImagePath, removedBBox);

            // 2️⃣ UI 모델 상태 갱신
            var item = _images.FirstOrDefault(i => i.FullPath == _currentImagePath);
            if (item != null)
            {
                item.HasLabel = _imageStateManager.HasLabel(_currentImagePath);
                ImageListBox.Items.Refresh();
            }

            _imageStateManager.RemoveLabel(_currentImagePath, removedBBox);

            // ✅ 삭제 반영 저장
            SaveLabelsToStateJson(_currentImagePath);

            RequestSaveLabelsDebounced(_currentImagePath);
            RefreshSummaryCounts();

        }


        private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClassComboBox.SelectedItem is not ComboBoxItem item)
                return;

            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            string className = item.Content.ToString();

            _bboxManager.SetSelectedClass(className);

            // ✅ 상태는 ImageStateManager 기준으로 갱신
            var imageItem = _images.FirstOrDefault(i => i.FullPath == _currentImagePath);
            if (imageItem != null)
            {
                imageItem.HasLabel = _imageStateManager.HasLabel(_currentImagePath);
                ImageListBox.Items.Refresh();
            }

            _bboxManager.SetSelectedClass(className);
            RequestSaveLabelsDebounced(_currentImagePath);

            // ✅ 클래스 변경 반영 저장
            SaveLabelsToStateJson(_currentImagePath);

        }

        private void SaveLabelsToStateJson(string imagePath)
        {
            // 혹시 캔버스 변경이 남아있다면 확정(드래그 종료 등에서 호출하므로 안전)
            _bboxManager.ForceUpdateAll(ImageCanvas.Width, ImageCanvas.Height);

            var state = _stateService.Load(imagePath);

            state.Labels.Clear();

            var boxes = _imageStateManager.GetLabels(imagePath);
            foreach (var b in boxes)
            {
                state.Labels.Add(new LabelDto
                {
                    ClassName = b.ClassName,
                    X = b.X,
                    Y = b.Y,
                    Width = b.Width,
                    Height = b.Height
                });
            }

            // 라벨 편집(추가/삭제/수정)이 발생했다는 뜻이므로, 이후에는 infer 대신 GT를 우선한다.
            state.HasManualYoloDecision = true;

            _stateService.Save(imagePath, state);
        }




        private void LoadImage(string imagePath)
        {
            _isLoadingImage = true;
            try
            {
                _currentImagePath = imagePath;
                ClassComboBox.IsEnabled = false;

                // 1️⃣ ImageStateManager 보장
                _imageStateManager.EnsureImage(imagePath);

                // 2️⃣ 이미지 로드
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                _rawBitmap = bitmap;              // 🔥 반드시 저장
                MainImage.Source = bitmap;

                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;

                // 3️⃣ Canvas 초기화
                _bboxManager.ClearAll();

                // 4️⃣ 라벨 로드: state.json 우선
                _bboxManager.ClearAll();
                _imageStateManager.ClearLabels(imagePath);

                var state = _stateService.Load(imagePath);

                if (state.Labels.Count > 0)
                {
                    var mutable = _imageStateManager.GetMutableLabels(imagePath);

                    foreach (var l in state.Labels)
                    {
                        mutable.Add(new BoundingBox
                        {
                            X = l.X,
                            Y = l.Y,
                            Width = l.Width,
                            Height = l.Height,
                            ClassName = l.ClassName
                        });
                    }
                }
                else
                {
                    // 레거시 txt fallback (읽기만)
                    _yoloService.Load(imagePath, _imageStateManager.GetMutableLabels(imagePath));
                }

                // 캔버스에 표시
                foreach (var bbox in _imageStateManager.GetLabels(imagePath))
                {
                    _bboxManager.AddFromModel(bbox, ImageCanvas.Width, ImageCanvas.Height);
                }

                bool hasGtLabel = _imageStateManager.HasLabel(imagePath);
                UpdatePredictionOverlayVisibility(imagePath);

                // 5️⃣ Anomaly 상태
                bool isNormal;
                if (_currentDataSource == DataSourceKind.ImportedBatch)
                {
                    // Imported batch는 infer 기본값을 우선 사용하고,
                    // 사용자가 수동으로 Anomaly를 바꾼 경우에만 state 값을 적용한다.
                    if (state.HasManualAnomalyDecision && state.IsNormal.HasValue)
                    {
                        isNormal = state.IsNormal.Value;
                    }
                    else if (_inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath))
                    {
                        isNormal = EvaluateInferMetaFromInfer(inferJsonPath).IsAnomaNormal;
                    }
                    else
                    {
                        isNormal = true;
                    }
                }
                else
                {
                    isNormal = _anomalyService.Load(imagePath);
                }
                _imageStateManager.SetNormal(imagePath, isNormal);

                // 6) ROI 기능은 현재 비활성화 상태로 고정
                RoiType roiType = RoiType.None;
                if (RoiFeaturesEnabled)
                {
                    roiType = _roiService.Load(imagePath);
                    _imageStateManager.SetRoiType(imagePath, roiType);
                    _roiPreprocessService.EnsureProcessed(imagePath, roiType);
                }
                else
                {
                    _imageStateManager.SetRoiType(imagePath, RoiType.None);
                }

                // 7️⃣ UI 반영
                if (ImageListBox.SelectedItem is ImageItem item)
                {
                    item.IsNormal = isNormal;
                    if (_currentDataSource == DataSourceKind.ImportedBatch)
                    {
                        if (state.HasManualYoloDecision)
                        {
                            item.HasLabel = hasGtLabel;
                        }
                        else if (_inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath))
                        {
                            item.HasLabel = EvaluateInferMetaFromInfer(inferJsonPath).HasYoloDefect;
                        }
                        else
                        {
                            item.HasLabel = hasGtLabel;
                        }
                    }
                    else
                    {
                        item.HasLabel = hasGtLabel;
                    }
                    item.RoiType = roiType;

                    NormalRadio.IsChecked = isNormal;
                    AbnormalRadio.IsChecked = !isNormal;
                }

                if (RoiFeaturesEnabled)
                    RestoreRoiTypeUI(imagePath);
                ImageListBox.Items.Refresh();

                // 🔥 8️⃣ ROI 체크 상태에 따라 화면 갱신 (이게 핵심)
                UpdateRoiDisplay();
            }
            finally
            {
                _isLoadingImage = false;
            }
        }

        private void UpdateRoiDisplay()
        {
            if (_rawBitmap == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            if (!RoiFeaturesEnabled)
            {
                MainImage.Source = _rawBitmap;
                return;
            }

            if (ShowRoiCheckBox.IsChecked == true)
            {
                var roiType = _imageStateManager.GetRoiType(_currentImagePath);

                // ✅ 서비스가 알아서 (생성/로드)해서 BitmapSource 반환
                MainImage.Source = _roiPreprocessService.GetOrCreateProcessedImage(_currentImagePath, roiType);
            }
            else
            {
                MainImage.Source = _rawBitmap;
            }
        }



        private void LoadImageFolder(string folderPath)
        {
            _images.Clear();
            _inferJsonByImagePath.Clear();

            var imageFiles = Directory.GetFiles(folderPath, "*.bmp");

            foreach (var img in imageFiles)
            {
                _imageStateManager.EnsureImage(img);

                RoiType roiType = RoiType.None;
                if (RoiFeaturesEnabled)
                {
                    if (_roiService.HasState(img))
                        roiType = _roiService.Load(img);

                    if (roiType == RoiType.None)
                    {
                        var inferred = InferRoiTypeFromFileName(IOPath.GetFileName(img));
                        roiType = inferred;
                        _roiService.Save(img, roiType);
                    }

                    _imageStateManager.SetRoiType(img, roiType);
                    _roiPreprocessService.EnsureProcessed(img, roiType);
                }
                else
                {
                    _imageStateManager.SetRoiType(img, RoiType.None);
                }

                // 1️⃣ YOLO 라벨 실제 로드
                var s = _stateService.Load(img);
                bool hasLabel = s.Labels.Count > 0;

                if (!hasLabel)
                {
                    var labels = new List<BoundingBox>();
                    _yoloService.Load(img, labels);
                    hasLabel = labels.Count > 0;

                    // (선택) txt가 있으면 state.json으로 마이그레이션
                    if (hasLabel)
                    {
                        s.Labels.Clear();
                        foreach (var b in labels)
                        {
                            s.Labels.Add(new LabelDto { ClassName = b.ClassName, X = b.X, Y = b.Y, Width = b.Width, Height = b.Height });
                        }
                        _stateService.Save(img, s);
                    }
                }


                // 2️⃣ Anomaly 상태 로드
                bool isNormal = _anomalyService.Load(img);

                _images.Add(new ImageItem
                {
                    FileName = IOPath.GetFileName(img),
                    FullPath = img,
                    HasLabel = hasLabel,
                    IsNormal = isNormal,
                    RoiType = roiType
                });
                RefreshSummaryCounts();
            }

            ImageListBox.ItemsSource = _images;
        }




        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is ImageItem item)
            {
                LoadImage(item.FullPath);

                NormalRadio.IsChecked = item.IsNormal;
                AbnormalRadio.IsChecked = !item.IsNormal;

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            }
        }
        private void NormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            // 1️⃣ 메모리 상태 변경
            _imageStateManager.SetNormal(item.FullPath, true);

            // 2️⃣ 파일 저장
            _anomalyService.Save(item.FullPath, true);

            // 3️⃣ UI 모델 반영
            item.IsNormal = true;

            ImageListBox.Items.Refresh();
            RefreshSummaryCounts();
        }

        private void AbnormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            _imageStateManager.SetNormal(item.FullPath, false);
            _anomalyService.Save(item.FullPath, false);

            item.IsNormal = false;

            ImageListBox.Items.Refresh();
            RefreshSummaryCounts();
        }


        private void ExportAnomalyDataset_Click(object sender, RoutedEventArgs e)
        {
            // 1. 프로젝트 루트는 UI가 판단
            string root = FindProjectRoot("capstone_design");

            // 2. 실제 export는 서비스에게 맡김
            string outputPath = _exportService.ExportAnomalyDataset(_images, root);

            // 3. 결과를 사용자에게 보여줌
            MessageBox.Show($"완료: {outputPath}");
        }

        private void ImportInferenceBatch_Click(object sender, RoutedEventArgs e)
        {
            var selectedBatchFolder = TrySelectFolder("Import inference batch folder");
            if (string.IsNullOrWhiteSpace(selectedBatchFolder))
                return;

            var result = ValidateInferenceBatchForImport(selectedBatchFolder);
            if (!result.IsValid)
            {
                MessageBox.Show(
                    result.Message,
                    "Import Inference Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                var projectRoot = FindProjectRoot("capstone_design");
                var imported = _inferenceBatchImportService.Import(selectedBatchFolder, projectRoot);
                MessageBox.Show(
                    $"imported path: {imported.ImportedPath}",
                    "Import Inference Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"배치 Import 실패: {ex.Message}",
                    "Import Inference Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void LoadImportedBatch_Click(object sender, RoutedEventArgs e)
        {
            string projectRoot = FindProjectRoot("capstone_design");
            string inboxRoot = IOPath.Combine(projectRoot, "training_inbox");

            if (!Directory.Exists(inboxRoot))
            {
                MessageBox.Show(
                    $"training_inbox 폴더가 없습니다.\n{inboxRoot}",
                    "Load Imported Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            var selectedBatchFolder = TrySelectFolder("Load imported batch folder", inboxRoot);
            if (string.IsNullOrWhiteSpace(selectedBatchFolder))
                return;

            if (!IsPathUnderRoot(selectedBatchFolder, inboxRoot))
            {
                MessageBox.Show(
                    "training_inbox 하위 폴더만 선택할 수 있습니다.",
                    "Load Imported Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            var validation = ValidateImportedBatchForView(selectedBatchFolder);
            if (!validation.IsValid)
            {
                MessageBox.Show(
                    validation.Message,
                    "Load Imported Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                LoadImportedBatchFromFolder(selectedBatchFolder);
                MessageBox.Show(
                    $"Imported batch loaded\n{selectedBatchFolder}\n총 item 수: {_images.Count}",
                    "Load Imported Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Imported batch 로드 실패: {ex.Message}",
                    "Load Imported Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void LoadLocalInput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!LoadLocalInputFolder(showErrorMessage: true))
                    return;

                MessageBox.Show(
                    $"Local input 로딩 완료\n{_defaultInputFolder}\n총 item 수: {_images.Count}",
                    "Load Local Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Local input 로딩 실패: {ex.Message}",
                    "Load Local Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void ApplyPredictionsToLabelsCurrentImage_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item || string.IsNullOrWhiteSpace(item.FullPath))
            {
                MessageBox.Show(
                    "현재 선택된 이미지가 없습니다.",
                    "Apply Predictions to Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            string imagePath = item.FullPath;

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

            item.HasLabel = _imageStateManager.HasLabel(imagePath);
            ImageListBox.Items.Refresh();
            RefreshSummaryCounts();

            SaveLabelsToStateJson(imagePath);
            RequestSaveLabelsDebounced(imagePath);

            MessageBox.Show(
                $"예측 박스 {predictionBoxes.Count}개를 GT 라벨로 적용했습니다.",
                "Apply Predictions to Labels",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
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

        private bool LoadLocalInputFolder(bool showErrorMessage)
        {
            if (!Directory.Exists(_defaultInputFolder))
            {
                if (showErrorMessage)
                {
                    MessageBox.Show(
                        $"Local input 폴더가 없습니다.\n{_defaultInputFolder}",
                        "Load Local Input",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
                return false;
            }

            StopWatchingInputFolder();
            _currentDataSource = DataSourceKind.LocalInput;

            LoadImageFolder(_defaultInputFolder);
            StartWatchingInputFolder(_defaultInputFolder);

            if (_images.Count > 0)
                ImageListBox.SelectedIndex = 0;
            else
                ResetImageDisplay();

            return true;
        }

        private void LoadImportedBatchFromFolder(string batchFolder)
        {
            string manifestPath = IOPath.Combine(batchFolder, "meta", "manifest.json");
            var manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);

            StopWatchingInputFolder();
            _currentDataSource = DataSourceKind.ImportedBatch;

            _images.Clear();
            _knownImages.Clear();
            _inferJsonByImagePath.Clear();

            foreach (var item in manifest.Items)
            {
                string imagePath = ResolveImportedImagePath(batchFolder, item);
                string inferJsonPath = ResolveImportedInferJsonPath(batchFolder, item);
                var aiMeta = EvaluateInferMetaFromInfer(inferJsonPath);
                var state = _stateService.Load(imagePath);
                bool hasGtLabel = state.Labels.Count > 0;
                bool isNormal = (state.HasManualAnomalyDecision && state.IsNormal.HasValue)
                    ? state.IsNormal.Value
                    : aiMeta.IsAnomaNormal;
                bool hasYoloDefectForView = state.HasManualYoloDecision
                    ? hasGtLabel
                    : aiMeta.HasYoloDefect;

                _imageStateManager.EnsureImage(imagePath);

                var roiType = RoiFeaturesEnabled
                    ? ParseRoiTypeSafe(item.RoiType)
                    : RoiType.None;
                _imageStateManager.SetRoiType(imagePath, roiType);

                _images.Add(new ImageItem
                {
                    FileName = IOPath.GetFileName(imagePath),
                    FullPath = imagePath,
                    HasLabel = hasYoloDefectForView,
                    IsNormal = isNormal,
                    HasAiInfer = aiMeta.HasAiInfer,
                    AiIsDefect = aiMeta.HasYoloDefect || !aiMeta.IsAnomaNormal,
                    RoiType = roiType
                });

                _inferJsonByImagePath[imagePath] = inferJsonPath;
            }

            ImageListBox.ItemsSource = _images;
            ImageListBox.Items.Refresh();
            RefreshSummaryCounts();

            if (_images.Count > 0)
                ImageListBox.SelectedIndex = 0;
            else
                ResetImageDisplay();
        }

        private string ResolveImportedImagePath(string batchFolder, ManifestItemDto item)
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

        private static string ResolveImportedInferJsonPath(string batchFolder, ManifestItemDto item)
        {
            return IOPath.IsPathRooted(item.InferJson)
                ? item.InferJson
                : IOPath.Combine(batchFolder, item.InferJson);
        }

        private static (bool HasAiInfer, bool HasYoloDefect, bool IsAnomaNormal) EvaluateInferMetaFromInfer(string inferJsonPath)
        {
            if (string.IsNullOrWhiteSpace(inferJsonPath) || !File.Exists(inferJsonPath))
                return (false, false, true);

            try
            {
                var infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
                bool hasYoloDefect = infer.Yolo?.Detections?.Count > 0;
                bool isAnomaNormal = !string.Equals(infer.Anoma?.Decision, "anomaly", StringComparison.OrdinalIgnoreCase);
                return (true, hasYoloDefect, isAnomaNormal);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AI status parse failed: {inferJsonPath}, {ex.Message}");
                return (false, false, true);
            }
        }

        private InferenceBatchValidationResult ValidateImportedBatchForView(string batchFolder)
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

            try
            {
                _ = InferenceBatchSchemaParser.ParseManifest(manifestPath);
            }
            catch (Exception ex)
            {
                return InferenceBatchValidationResult.Fail($"manifest.json 파싱 실패: {ex.Message}");
            }

            return new InferenceBatchValidationResult
            {
                IsValid = true,
                Message = "배치 검증 OK"
            };
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
                    Stroke = Brushes.DodgerBlue,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = Brushes.Transparent,
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
        }

        private InferenceBatchValidationResult ValidateInferenceBatchForImport(string batchFolder)
        {
            if (string.IsNullOrWhiteSpace(batchFolder) || !Directory.Exists(batchFolder))
                return InferenceBatchValidationResult.Fail("배치 폴더가 존재하지 않습니다.");

            string metaDir = IOPath.Combine(batchFolder, "meta");
            string doneFlag = IOPath.Combine(metaDir, "DONE.flag");

            if (!Directory.Exists(metaDir))
                return InferenceBatchValidationResult.Fail("meta 폴더가 없습니다.");

            if (!File.Exists(doneFlag))
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

            var missingFiles = new List<string>();
            foreach (var item in manifest.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProcessedImage))
                {
                    missingFiles.Add($"[{item.Id}] processed_image가 비어 있음");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.InferJson))
                {
                    missingFiles.Add($"[{item.Id}] infer_json가 비어 있음");
                    continue;
                }

                var processedPath = IOPath.IsPathRooted(item.ProcessedImage)
                    ? item.ProcessedImage
                    : IOPath.Combine(batchFolder, item.ProcessedImage);

                var inferPath = IOPath.IsPathRooted(item.InferJson)
                    ? item.InferJson
                    : IOPath.Combine(batchFolder, item.InferJson);

                if (!File.Exists(processedPath))
                    missingFiles.Add(item.ProcessedImage);

                if (!File.Exists(inferPath))
                    missingFiles.Add(item.InferJson);
            }

            string previewIds = string.Join(", ", manifest.Items
                .Select(item => string.IsNullOrWhiteSpace(item.Id) ? "(no id)" : item.Id)
                .Take(3));

            if (missingFiles.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("배치 검증 실패");
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
                Message = $"배치 검증 OK\n총 item 수: {manifest.Items.Count}\n누락 파일 개수: 0\n첫 3개 id: {previewIds}"
            };
        }

        private sealed class InferenceBatchValidationResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; } = "";

            public static InferenceBatchValidationResult Fail(string message)
                => new() { IsValid = false, Message = message };
        }

        private string FindProjectRoot(string targetFolderName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (dir != null)
            {
                if (dir.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private void RoiTypeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!RoiFeaturesEnabled)
                return;

            if (_isLoadingImage) return;

            if (ImageListBox.SelectedItem is not ImageItem item)
                return;
            if (sender is not RadioButton rb)
                return;
            if (!Enum.TryParse<RoiType>(rb.Tag.ToString(), out var roiType))
                return;

            // 1) 메모리(UI 모델 + StateManager 둘 다)
            item.RoiType = roiType;
            _imageStateManager.SetRoiType(item.FullPath, roiType);

            // 2) 파일 저장
            _roiService.Save(item.FullPath, roiType);

            // ✅ Show 여부와 무관하게 "전처리 파일"을 생성/갱신
            _roiPreprocessService.EnsureProcessed(item.FullPath, roiType);

            // 4) 즉시 화면 갱신
            UpdateRoiDisplay();

            ImageListBox.Items.Refresh();
        }


        private void RestoreRoiTypeUI(string imagePath)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            foreach (var child in LogicalTreeHelper.GetChildren(this))
            {
                // 아무것도 안 함 (placeholder)
            }

            // 간단하게 직접 찾는 방식
            foreach (var rb in FindVisualChildren<RadioButton>(this))
            {
                if (rb.Tag?.ToString() == item.RoiType.ToString())
                {
                    rb.IsChecked = true;
                    return;
                }
            }
        }

        private void ShowRoiCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!RoiFeaturesEnabled)
                return;

            UpdateRoiDisplay();
        }

        private void ShowRoiCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!RoiFeaturesEnabled)
                return;

            UpdateRoiDisplay();
        }

        private void ShowPredictionCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePredictionOverlayVisibility();
        }

        private void ShowPredictionCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdatePredictionOverlayVisibility();
        }

        private RoiType InferRoiTypeFromFileName(string fileName)
        {
            // fileName: "250825_151708_A35W_4-1 [1024].bmp" 처럼 들어온다고 가정
            // 1) 확장자 제거
            string name = IOPath.GetFileNameWithoutExtension(fileName);

            // 2) 마지막 '-' 위치 찾기
            int idx = name.LastIndexOf('-');
            if (idx < 0 || idx == name.Length - 1)
                return RoiType.None;

            // 3) 마지막 '-' 뒤 숫자만 파싱 (뒤에 공백/대괄호가 있어도 안전하게)
            // 예: "4-1 [1024]" -> idx 뒤 문자열은 "1 [1024]"
            string tail = name.Substring(idx + 1).Trim();

            // tail의 선두 숫자만 읽기
            int n = 0;
            int i = 0;
            while (i < tail.Length && char.IsDigit(tail[i]))
            {
                n = n * 10 + (tail[i] - '0');
                i++;
            }

            if (i == 0) return RoiType.None; // 숫자 없음

            return n switch
            {
                1 => RoiType.A,
                2 => RoiType.B,
                3 => RoiType.C,
                _ => RoiType.None
            };
        }

        private RoiType ResolveRoiTypeOnlyWhenNone(string imagePath)
        {
            // 1) 저장된 값이 있으면 로드
            // (HasState는 RoiStateService에 추가되어 있어야 합니다)
            if (_roiService.HasState(imagePath))
            {
                var saved = _roiService.Load(imagePath);

                // ✅ 이미 지정(A/B/C)되어 있으면 그대로 둠
                if (saved != RoiType.None)
                    return saved;

                // ✅ 저장은 되어있는데 None이면 -> 자동 지정 시도
                var inferred = InferRoiTypeFromFileName(IOPath.GetFileName(imagePath));
                if (inferred != RoiType.None)
                {
                    _roiService.Save(imagePath, inferred);
                    return inferred;
                }

                return RoiType.None;
            }

            // 2) 저장 자체가 없으면 -> 자동 지정 시도 후 저장(원하면 None도 저장 가능)
            var inferred2 = InferRoiTypeFromFileName(IOPath.GetFileName(imagePath));
            _roiService.Save(imagePath, inferred2);  // None도 저장해두면 다음 실행 때 "처리됨" 상태가 됨
            return inferred2;
        }
        private void RequestSaveLabelsDebounced(string imagePath) //디바운스 트리거
        {
            if (_isLoadingImage) return;
            if (string.IsNullOrEmpty(imagePath)) return;

            _pendingSaveImagePath = imagePath;

            _labelSaveDebounceTimer.Stop();
            _labelSaveDebounceTimer.Start();
        }

        private async void TrainAll_Click(object sender, RoutedEventArgs e)
        {
            // 학습은 오래 걸리니 UI 멈춤 방지
            try
            {
                if (_images.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }


                string projectRoot = FindProjectRoot("capstone_design");
                var settings = AppSettingsLoader.LoadOrThrow(projectRoot);


                // 1) runRoot
                string inputDir = IOPath.GetDirectoryName(_images[0].FullPath)!;
                string runRoot = IOPath.Combine(inputDir, "_train_runs");
                Directory.CreateDirectory(runRoot);

                // 2) 현재 이미지 경로
                var imagePaths = _images.Select(x => x.FullPath)
                                        .Where(File.Exists)
                                        .ToList();

                int totalImages = imagePaths.Count;
                int normalImages = imagePaths.Count(p => (_stateService.Load(p).IsNormal ?? true) == true);

                // 3) YOLO workspace 생성 (라벨 txt는 여기서 workspace에만 생성됨)
                var yoloWsSvc = new YoloWorkspaceService(_stateService);
                var yoloWs = yoloWsSvc.BuildYoloWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: settings.Workspace.TrainRatio,
                    valRatio: settings.Workspace.ValRatio,
                    seed: settings.Workspace.Seed,
                    useRoiProcessedImages: settings.Workspace.UseRoiProcessedImages
                );

                // 4) Anoma workspace 생성 (정상만)
                var anomaWsSvc = new AnomaWorkspaceService(_stateService);
                var anomaWs = anomaWsSvc.BuildWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: settings.Workspace.TrainRatio,
                    valRatio: settings.Workspace.ValRatio,
                    seed: settings.Workspace.Seed,
                    useRoiProcessedImages: settings.Workspace.UseRoiProcessedImages
                );

                // 5) 이번 실행 결과 폴더(로그/산출물)
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string runDir = IOPath.Combine(runRoot, $"run_{stamp}_all");
                string logsDir = IOPath.Combine(runDir, "logs");
                Directory.CreateDirectory(logsDir);

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");
                Directory.CreateDirectory(yoloOut);
                Directory.CreateDirectory(anomaOut);

                // 6) 파이썬 스크립트 실행(순차)
                string pythonExe = settings.PythonExe;
                string yoloScript = IOPath.Combine(projectRoot, settings.Scripts.YoloTrain);
                string anomaScript = IOPath.Combine(projectRoot, settings.Scripts.AnomaTrain);


                if (!File.Exists(yoloScript) || !File.Exists(anomaScript))
                {
                    MessageBox.Show("scripts/train_yolo.py 또는 scripts/train_anoma.py가 없습니다.");
                    return;
                }

                var runner = new PythonRunner();
                using var cts = new CancellationTokenSource(); // 추후 Cancel 메뉴 추가 가능

                // (1) YOLO
                int yoloCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: yoloScript,
                    args: $"--workspace \"{yoloWs.WorkspaceRoot}\" --out \"{yoloOut}\"",
                    workingDir: projectRoot,
                    logPath: IOPath.Combine(logsDir, "yolo.log"),
                    ct: cts.Token
                );

                if (yoloCode != 0)
                {
                    MessageBox.Show($"YOLO 학습 실패 (ExitCode={yoloCode})\nlogs/yolo.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                // (2) Anomalib
                int anomaCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: anomaScript,
                    args: $"--workspace \"{anomaWs.WorkspaceRoot}\" --out \"{anomaOut}\"",
                    workingDir: projectRoot,
                    logPath: IOPath.Combine(logsDir, "anoma.log"),
                    ct: cts.Token
                );

                if (anomaCode != 0)
                {
                    MessageBox.Show($"Anomalib 학습 실패 (ExitCode={anomaCode})\nlogs/anoma.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                // 7) inference package 생성
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");
                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                // ✅ 스크립트가 아래 파일명을 생성한다는 계약이 필요
                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                if (!File.Exists(yoloOnnx) || !File.Exists(anomaOnnx))
                {
                    MessageBox.Show("ONNX 산출물이 없습니다. 스크립트가 yolo.onnx / anoma.onnx를 out 폴더에 생성해야 합니다.");
                    OpenFolder(runDir);
                    return;
                }

                File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);
                File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                var pipelineObj = new
                {
                    schema_version = 1,
                    input = new
                    {
                        image_format = "bmp",
                        use_roi_processed = settings.Workspace.UseRoiProcessedImages
                    },
                    yolo = new
                    {
                        model = "models/yolo.onnx",
                        imgsz = settings.YoloInfer.ImgSz,
                        letterbox = settings.YoloInfer.Letterbox,
                        conf_thres = settings.YoloInfer.ConfThres,
                        iou_thres = settings.YoloInfer.IouThres,
                        max_det = settings.YoloInfer.MaxDet,
                        class_map = new { dent = 0, loose = 1 }
                    },
                    anoma = new
                    {
                        model = "models/anoma.onnx",
                        mode = settings.AnomaInfer.Mode,
                        input_size = settings.AnomaInfer.InputSize,
                        score_thres = settings.AnomaInfer.ScoreThres,
                        crop_padding_px = settings.AnomaInfer.CropPaddingPx
                    },
                    fusion = new
                    {
                        rule = settings.Fusion.Rule,
                        yolo_conf_thres = settings.Fusion.YoloThreshold,
                        anoma_score_thres = settings.Fusion.AnomaThreshold
                    },
                    output = new
                    {
                        format = "json",
                        schema = "detections_v1"
                    }
                };

                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");
                File.WriteAllText(
                    pipelinePath,
                    System.Text.Json.JsonSerializer.Serialize(
                        pipelineObj,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                    ),
                    System.Text.Encoding.UTF8
                );



                // ✅ 패키지 검증 (없으면 성공 처리 금지)
                VerifyInferencePackageOrThrow(pkgDir);

                // ✅ manifest 기록 (재현성/디버깅용)
                WriteRunManifest(
                    runDir: runDir,
                    projectRoot: projectRoot,
                    pythonExe: pythonExe,
                    yoloScript: yoloScript,
                    anomaScript: anomaScript,
                    yoloWorkspaceRoot: yoloWs.WorkspaceRoot,
                    anomaWorkspaceRoot: anomaWs.WorkspaceRoot,
                    yoloOutDir: yoloOut,
                    anomaOutDir: anomaOut,
                    inferencePackageDir: pkgDir,
                    settings: settings,
                    totalImages: totalImages,
                    normalImages: normalImages
                );



                MessageBox.Show($"Train All 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Train All 중 예외 발생:\n" + ex.Message);

                // runDir 변수가 스코프 밖이면, 최소 logs 위치라도 열게 구조를 잡으세요.
                // (가장 쉬운 방법: runDir을 함수 시작에서 string? runDir = null;로 선언하고 나중에 채우기)
            }

        }

        private void OpenFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

    private void VerifyInferencePackageOrThrow(string pkgDir)
    {
        string modelsDir = IOPath.Combine(pkgDir, "models");
        string cfgDir = IOPath.Combine(pkgDir, "config");

        string yoloOnnx = IOPath.Combine(modelsDir, "yolo.onnx");
        string anomaOnnx = IOPath.Combine(modelsDir, "anoma.onnx");
        string pipeline = IOPath.Combine(cfgDir, "pipeline.json");

        if (!File.Exists(yoloOnnx))
            throw new FileNotFoundException("Missing yolo.onnx in inference_package/models", yoloOnnx);

        if (!File.Exists(anomaOnnx))
            throw new FileNotFoundException("Missing anoma.onnx in inference_package/models", anomaOnnx);

        if (!File.Exists(pipeline))
            throw new FileNotFoundException("Missing pipeline.json in inference_package/config", pipeline);

        // 0바이트 방지
        var yoloSize = new FileInfo(yoloOnnx).Length;
        var anomaSize = new FileInfo(anomaOnnx).Length;

        if (yoloSize <= 0)
            throw new InvalidOperationException("yolo.onnx is empty (0 bytes).");

        if (anomaSize <= 0)
            throw new InvalidOperationException("anoma.onnx is empty (0 bytes).");

            using var doc = JsonDocument.Parse(File.ReadAllText(pipeline));
            var root = doc.RootElement;

            Require(root, "schema_version");
            Require(root, "yolo");
            Require(root, "anoma");
            Require(root, "fusion");

            var yolo = root.GetProperty("yolo");
            Require(yolo, "model");
            Require(yolo, "imgsz");
            Require(yolo, "conf_thres");
            Require(yolo, "iou_thres");
            Require(yolo, "max_det");
            Require(yolo, "class_map");

            var anoma = root.GetProperty("anoma");
            Require(anoma, "model");
            Require(anoma, "mode");
            Require(anoma, "input_size");
            Require(anoma, "score_thres");

            var fusion = root.GetProperty("fusion");
            Require(fusion, "rule");
            Require(fusion, "yolo_conf_thres");
            Require(fusion, "anoma_score_thres");

        }

        private void Require(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out _))
                throw new InvalidOperationException($"pipeline.json missing required field: {prop}");
        }


        private void WriteRunManifest(
            string runDir,
            string projectRoot,
            string pythonExe,
            string yoloScript,
            string anomaScript,
            string yoloWorkspaceRoot,
            string anomaWorkspaceRoot,
            string yoloOutDir,
            string anomaOutDir,
            string inferencePackageDir,
            AppSettings settings,
            int totalImages,
            int normalImages
        )
        {
            var manifest = new
            {
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProjectRoot = projectRoot,
                PythonExe = pythonExe,
                Scripts = new { Yolo = yoloScript, Anoma = anomaScript },
                Workspaces = new { Yolo = yoloWorkspaceRoot, Anoma = anomaWorkspaceRoot },
                Outputs = new { YoloOut = yoloOutDir, AnomaOut = anomaOutDir, InferencePackage = inferencePackageDir },
                Dataset = new { TotalImages = totalImages, NormalImages = normalImages },
                Settings = settings
            };

            string path = IOPath.Combine(runDir, "run_manifest.json");
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        private void BuildPackageOnly_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_images.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }

                string projectRoot = FindProjectRoot("capstone_design");
                var settings = AppSettingsLoader.LoadOrThrow(projectRoot);

                string inputDir = IOPath.GetDirectoryName(_images[0].FullPath)!;
                string runRoot = IOPath.Combine(inputDir, "_train_runs");
                if (!Directory.Exists(runRoot))
                {
                    MessageBox.Show("_train_runs 폴더가 없습니다.");
                    return;
                }

                // ✅ 가장 최근 run_..._all 찾기
                var latestRunDir = Directory.GetDirectories(runRoot, "run_*_all")
                                            .Select(d => new DirectoryInfo(d))
                                            .OrderByDescending(d => d.CreationTimeUtc)
                                            .FirstOrDefault();

                if (latestRunDir == null)
                {
                    MessageBox.Show("run_*_all 폴더가 없습니다.");
                    return;
                }

                string runDir = latestRunDir.FullName;

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");

                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                // ✅ 여기서부터는 학습 재실행 없음. 없으면 그냥 실패해야 정상.
                if (!File.Exists(yoloOnnx) || !File.Exists(anomaOnnx))
                {
                    MessageBox.Show(
                        "필요한 ONNX가 없습니다.\n\n" +
                        $"yolo: {yoloOnnx}\n" +
                        $"anoma: {anomaOnnx}\n\n" +
                        "Train All을 먼저 수행했는지 확인하세요."
                    );
                    OpenFolder(runDir);
                    return;
                }

                // 패키지 재생성
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");

                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);
                File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                // pipeline.json은 기존이 있으면 그대로 두고, 없으면 최소 생성
                string pipelinePath = IOPath.Combine(cfgDir, "pipeline.json");

                // ✅ appsettings 기반 pipeline 객체 생성
                var pipelineObj = new
                {
                    schema_version = 1,
                    input = new
                    {
                        image_format = "bmp",
                        use_roi_processed = settings.Workspace.UseRoiProcessedImages
                    },
                    yolo = new
                    {
                        model = "models/yolo.onnx",
                        imgsz = settings.YoloInfer.ImgSz,
                        letterbox = settings.YoloInfer.Letterbox,
                        conf_thres = settings.YoloInfer.ConfThres,
                        iou_thres = settings.YoloInfer.IouThres,
                        max_det = settings.YoloInfer.MaxDet,
                        class_map = new { dent = 0, loose = 1 }
                    },
                    anoma = new
                    {
                        model = "models/anoma.onnx",
                        mode = settings.AnomaInfer.Mode,
                        input_size = settings.AnomaInfer.InputSize,
                        score_thres = settings.AnomaInfer.ScoreThres,
                        crop_padding_px = settings.AnomaInfer.CropPaddingPx
                    },
                    fusion = new
                    {
                        rule = settings.Fusion.Rule,
                        yolo_conf_thres = settings.Fusion.YoloThreshold,
                        anoma_score_thres = settings.Fusion.AnomaThreshold
                    },
                    output = new
                    {
                        format = "json",
                        schema = "detections_v1"
                    }
                };

                // ✅ 항상 덮어쓰기 (조건문 제거)
                File.WriteAllText(
                    pipelinePath,
                    System.Text.Json.JsonSerializer.Serialize(pipelineObj,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                    System.Text.Encoding.UTF8
                );


                // ✅ 검증
                VerifyInferencePackageOrThrow(pkgDir);

                MessageBox.Show($"Package Only 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Build Package Only 실패:\n" + ex.Message);
            }
        }

        private void StopWatchingInputFolder()
        {
            if (_watcher == null)
                return;

            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            catch
            {
            }
            finally
            {
                _watcher = null;
            }
        }

        private void StartWatchingInputFolder(string folderPath)
        {
            StopWatchingInputFolder();

            _knownImages.Clear();
            foreach (var it in _images)
                _knownImages.Add(it.FullPath);

            _currentDataSource = DataSourceKind.LocalInput;
            _watcher = new FileSystemWatcher(folderPath, "*.bmp")
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
            };

            _watcher.Created += async (_, e) => await OnNewImageArrived(e.FullPath);
            _watcher.Renamed += async (_, e) => await OnImageRenamed(e.OldFullPath, e.FullPath);
            _watcher.Deleted += async (_, e) => await OnImageDeleted(e.FullPath);
        }


        private async Task OnNewImageArrived(string path)
        {
            if (_currentDataSource != DataSourceKind.LocalInput)
                return;

            if (!path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                return;

            await _watchLock.WaitAsync();
            try
            {
                if (_knownImages.Contains(path))
                    return;

                if (!await WaitUntilFileReady(path, retries: 10, delayMs: 200))
                    return;

                _knownImages.Add(path);

                // ✅ 상태 로드
                var state = _stateService.Load(path);

                var roiType = RoiType.None;
                if (RoiFeaturesEnabled)
                {
                    roiType = ParseRoiTypeSafe(state.RoiType);
                    if (roiType == RoiType.None)
                    {
                        var inferred = InferRoiTypeFromFileName(IOPath.GetFileName(path));
                        if (inferred != RoiType.None)
                        {
                            state.RoiType = inferred.ToString();
                            _stateService.Save(path, state);

                            _roiPreprocessService.EnsureProcessed(path, inferred);
                            roiType = inferred;
                        }
                    }
                }

                // ✅ 라벨 여부만 체크(표시용)
                var labels = new List<BoundingBox>();
                _yoloService.Load(path, labels);

                bool isNormal = state.IsNormal ?? true;

                await Dispatcher.InvokeAsync(() =>
                {
                    // 중복 UI 방지(혹시라도)
                    if (_images.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                        return;

                    _images.Add(new ImageItem
                    {
                        FileName = IOPath.GetFileName(path),
                        FullPath = path,
                        HasLabel = labels.Count > 0,
                        IsNormal = isNormal,
                        RoiType = roiType
                    });
                    RefreshSummaryCounts();

                    ImageListBox.Items.Refresh();
                });
            }
            finally
            {
                _watchLock.Release();
            }
        }

        private async Task OnImageDeleted(string path)
        {
            if (_currentDataSource != DataSourceKind.LocalInput)
                return;

            await _watchLock.WaitAsync();
            try
            {
                _knownImages.Remove(path);

                await Dispatcher.InvokeAsync(() =>
                {
                    var target = _images.FirstOrDefault(x =>
                        string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));

                    if (target != null)
                    {
                        // 현재 선택된 이미지가 삭제되면 UI도 안전하게 처리
                        bool wasSelected = string.Equals(_currentImagePath, path, StringComparison.OrdinalIgnoreCase);

                        _images.Remove(target);
                        RefreshSummaryCounts();
                        ImageListBox.Items.Refresh();

                        if (wasSelected)
                        {
                            _currentImagePath = null;
                            MainImage.Source = null;
                            _bboxManager.ClearAll();
                        }
                    }
                });
            }
            finally
            {
                _watchLock.Release();
            }
        }

        private async Task OnImageRenamed(string oldPath, string newPath)
        {
            if (_currentDataSource != DataSourceKind.LocalInput)
                return;

            if (!newPath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                return;

            await _watchLock.WaitAsync();
            try
            {
                _knownImages.Remove(oldPath);

                if (await WaitUntilFileReady(newPath, retries: 10, delayMs: 200))
                    _knownImages.Add(newPath);

                await Dispatcher.InvokeAsync(() =>
                {
                    var item = _images.FirstOrDefault(x =>
                        string.Equals(x.FullPath, oldPath, StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        item.FullPath = newPath;
                        item.FileName = IOPath.GetFileName(newPath);
                        ImageListBox.Items.Refresh();

                        if (string.Equals(_currentImagePath, oldPath, StringComparison.OrdinalIgnoreCase))
                            _currentImagePath = newPath;
                    }
                    else
                    {
                        // 목록에 없던 게 renamed로 들어오면 그냥 추가로 처리
                        // (안전하게)
                        _ = OnNewImageArrived(newPath);
                    }
                });
            }
            finally
            {
                _watchLock.Release();
            }
        }


        private static RoiType ParseRoiTypeSafe(string? roiStr)
        {
            if (Enum.TryParse<RoiType>(roiStr, ignoreCase: true, out var r))
                return r;
            return RoiType.None;
        }

        private async Task<bool> WaitUntilFileReady(string path, int retries, int delayMs)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (fs.Length > 0) return true;
                }
                catch { }
                await Task.Delay(delayMs);
            }
            return false;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopWatchingInputFolder();
        }

        private void RefreshSummaryCounts()
        {
            int total = _images.Count;

            int defect = _images.Count(i => i.HasLabel || !i.IsNormal);
            int normal = total - defect;

            TotalCountText.Text = $"총 {total}개";
            NormalCountText.Text = $"정상 {normal}개";
            DefectCountText.Text = $"불량 {defect}개 (YOLO 또는 Anoma)";
        }

        public MainWindow()
        {
            InitializeComponent();
            _yoloService = new YoloLabelService(_classToId);
            _bboxManager = new BoundingBoxManager(ImageCanvas);
            _canvasInteractionManager = new CanvasInteractionManager(
                ImageScrollViewer,
                ImageScale,
                _bboxManager
            );
            _imageStateManager = new ImageStateManager();
            _anomalyService = new AnomalyStateService();
            _roiService = new RoiStateService();
            _roiPreprocessService = new RoiPreprocessService();

            if (!RoiFeaturesEnabled)
            {
                ShowRoiCheckBox.IsEnabled = false;
                ShowRoiCheckBox.IsChecked = false;
            }

            //타이머 초기화   
            _labelSaveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LabelSaveDebounceMs)
            };
            _labelSaveDebounceTimer.Tick += (s, e) =>
            {
                _labelSaveDebounceTimer.Stop();

                if (_isLoadingImage) return;
                if (string.IsNullOrEmpty(_pendingSaveImagePath)) return;

                SaveLabelsToStateJson(_pendingSaveImagePath);
            };

            LoadLocalInputFolder(showErrorMessage: false);


            Loaded += (s, e) =>
            {
                if (_images.Count > 0)
                {
                    ImageListBox.SelectedIndex = 0;
                }

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            };
        }

    }
}
