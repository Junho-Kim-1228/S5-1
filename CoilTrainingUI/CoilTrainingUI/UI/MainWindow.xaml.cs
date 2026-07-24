using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Imaging;
using CoilTrainingUI.Services.Review;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private bool _isLoadingImage;

        private BoundingBoxManager _bboxManager;
        private readonly InferenceBatchImportService _inferenceBatchImportService = new();
        private readonly BatchLibraryService _batchLibraryService = new();
        private readonly BatchImportService _batchImportService;
        private readonly BatchMergeService _batchMergeService = new();
        private CanvasInteractionManager _canvasInteractionManager;
        private ImageStateManager _imageStateManager;
        private readonly ReviewRepository _reviewRepository = new();
        private readonly PredictionReader _predictionReader = new();
        private readonly ReviewWorkflowService _reviewWorkflow = new();
        private readonly AutoReviewService _autoReviewService = new();
        private readonly ReviewProjectionService _reviewProjection = new();
        private readonly LegacyReviewMigrationService _reviewMigrationService;
        private readonly TrainingDatasetSelector _trainingDatasetSelector;
        private readonly TrainingDatasetValidator _datasetValidator;
        private AutoReviewPolicy _fallbackAutoReviewPolicy = AutoReviewPolicy.Disabled;

        private readonly Dictionary<string, string> _inferJsonByImagePath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _expectedInferenceContextByImagePath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PredictionSnapshot> _predictionByImagePath = new(StringComparer.OrdinalIgnoreCase);
        private const string PredictionOverlayTag = "__prediction_overlay";
        private string? _predictionOverlayAutoHiddenImagePath;
        private const string AllBatchFilterLabel = "(전체 배치)";
        private string? _currentBatchRoot;
        private bool _currentBatchHasAnyInfer;

        // 항상 원본은 유지
        private readonly ImageBitmapCache _imageBitmapCache = new(capacity: 3);
        private CancellationTokenSource? _imageLoadCancellation;
        private CancellationTokenSource? _imagePrefetchCancellation;
        private CancellationTokenSource? _rawViewLoadCancellation;
        private long _imageLoadRequestId;
        private BitmapSource? _rawBitmap;
        private BitmapSource? _rawViewBitmap;
        private string? _rawViewBitmapPath;
        private bool _suppressRawToggleEvent;
        private int _imageListWheelDeltaAccumulator;
        private const int ImageListWheelDeltaStep = 240;

        private string? _currentImagePath;
        private ReviewState _currentReviewState = new();
        private string _activeDrawClass = "dent";
        private bool _suppressClassComboBoxChange;


        private readonly Dictionary<string, int> _classToId = new()
        {
            { "dent", 0 },
            { "loose", 1 }
        };

        private readonly ObservableCollection<string> _batchFilterOptions = new();
        private ObservableCollection<ImageItem> _images
            = new ObservableCollection<ImageItem>();
        private ICollectionView? _imageCollectionView;
        private bool _suppressFilterRefresh;

        private void SetActiveDrawClass(string? className)
        {
            string normalized = NormalizeDrawClassName(className);
            _activeDrawClass = normalized;
            _bboxManager.DefaultClassName = normalized;
        }

        private string NormalizeDrawClassName(string? className)
        {
            string normalized = (className ?? "").Trim().ToLowerInvariant();
            return _classToId.ContainsKey(normalized) ? normalized : "dent";
        }

        private void SetClassComboBoxSelection(string? className)
        {
            string normalized = NormalizeDrawClassName(className);
            var comboItem = ClassComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Content?.ToString(), normalized, StringComparison.OrdinalIgnoreCase));

            _suppressClassComboBoxChange = true;
            try
            {
                ClassComboBox.SelectedItem = comboItem;
            }
            finally
            {
                _suppressClassComboBoxChange = false;
            }
        }

        private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Rectangle)
                return;
            if (!string.IsNullOrWhiteSpace(_currentImagePath) &&
                _reviewRepository.Load(_currentImagePath).State.Decision == ImageReviewDecision.ConfirmedNormal)
            {
                MessageBox.Show(
                    "정상 확정 이미지에는 박스를 추가할 수 없습니다. 먼저 불량으로 확정하세요.",
                    "박스 편집",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 🔥 이전 선택 완전 해제
            _bboxManager.ClearSelection();
            ClassComboBox.IsEnabled = !string.IsNullOrEmpty(_currentImagePath);
            SetClassComboBoxSelection(_activeDrawClass);

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

            string? currentImagePath = _currentImagePath;
            if (string.IsNullOrEmpty(currentImagePath))
                return;

            // 1️⃣ 상태 저장
            _imageStateManager.AddLabel(currentImagePath, bbox);

            // 2️⃣ 🔥 방금 만든 박스를 자동 선택 상태로 만들기
            _bboxManager.SelectLastCreated();

            // 3️⃣ 클래스 UI 활성화 + 기본값 반영
            ClassComboBox.IsEnabled = true;
            SetClassComboBoxSelection(bbox.ClassName);
            
            SaveLabelsToStateJson(currentImagePath, markManualYoloDecision: true);
            SyncGtSummaryForImage(currentImagePath);
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
                    SetClassComboBoxSelection(bbox.ClassName);
                }

                e.Handled = true;
            }
        }

        private void ImageCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed ||
                string.IsNullOrWhiteSpace(_currentImagePath))
            {
                return;
            }

            if (!_canvasInteractionManager.StartPan(e.GetPosition(ImageScrollViewer)))
                return;

            if (!ImageCanvas.CaptureMouse())
            {
                _canvasInteractionManager.EndPan();
                return;
            }

            ImageCanvas.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void ImageCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_canvasInteractionManager.IsPanning)
            {
                if (e.RightButton == MouseButtonState.Pressed)
                {
                    _canvasInteractionManager.UpdatePan(e.GetPosition(ImageScrollViewer));
                }
                else
                {
                    StopImagePan();
                }

                e.Handled = true;
                return;
            }

            Point point = e.GetPosition(ImageCanvas);
            if (e.LeftButton == MouseButtonState.Pressed)
                _bboxManager.Drag(point);
            else
                _bboxManager.UpdateHoverCursor(e.OriginalSource as Rectangle, point);
        }

        private void ImageCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool moved = _bboxManager.EndDrag(
                ImageCanvas.Width,
                ImageCanvas.Height
            );

            if (!moved)
                return;

            // ✅ 드래그가 실제로 발생한 경우에만 state.json 저장
            string? currentImagePath = _currentImagePath;
            if (!string.IsNullOrEmpty(currentImagePath))
            {
                SaveLabelsToStateJson(currentImagePath, markManualYoloDecision: true);
            }
        }

        private void ImageCanvas_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_canvasInteractionManager.IsPanning)
                return;

            StopImagePan();
            e.Handled = true;
        }

        private void ImageCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_canvasInteractionManager.IsPanning)
                return;

            _canvasInteractionManager.EndPan();
            ImageCanvas.Cursor = Cursors.Cross;
        }

        private void StopImagePan()
        {
            _canvasInteractionManager.EndPan();
            if (ImageCanvas.IsMouseCaptured)
                ImageCanvas.ReleaseMouseCapture();
            ImageCanvas.Cursor = Cursors.Cross;
        }

        private void ImageCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _canvasInteractionManager.OnMouseWheel(e);
            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _canvasInteractionManager.ZoomIn();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _canvasInteractionManager.ZoomOut();
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            if (ImageCanvas.Width <= 0 || ImageCanvas.Height <= 0)
                return;

            _canvasInteractionManager.FitToView(
                ImageCanvas.Width,
                ImageCanvas.Height
            );
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _canvasInteractionManager.EnsureWithinBounds();
        }

        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _canvasInteractionManager.OnScrollChanged();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            string currentImagePath = _currentImagePath;

            var removedBBox = _bboxManager.DeleteSelected();
            if (removedBBox == null)
                return;

            // 1️⃣ 메모리 상태에서 제거
            _imageStateManager.RemoveLabel(currentImagePath, removedBBox);

            // 2️⃣ UI 모델 상태 갱신
            SyncGtSummaryForImage(currentImagePath);

            // ✅ 삭제 반영 저장
            SaveLabelsToStateJson(currentImagePath, markManualYoloDecision: true);

            RefreshSummaryCounts();

        }


        private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressClassComboBoxChange)
                return;

            if (ClassComboBox.SelectedItem is not ComboBoxItem item)
                return;

            string className = NormalizeDrawClassName(item.Content?.ToString());
            SetActiveDrawClass(className);

            if (string.IsNullOrEmpty(_currentImagePath))
                return;
            if (_bboxManager.SelectedBBox == null)
                return;

            string currentImagePath = _currentImagePath;

            _bboxManager.SetSelectedClass(className);

            // ✅ 상태는 ImageStateManager 기준으로 갱신
            SyncGtSummaryForImage(currentImagePath);

            // ✅ 클래스 변경 반영 저장
            SaveLabelsToStateJson(currentImagePath, markManualYoloDecision: true);

        }

        private void SaveLabelsToStateJson(string imagePath, bool markManualYoloDecision = false)
        {
            _bboxManager.ForceUpdateAll(ImageCanvas.Width, ImageCanvas.Height);
            var boxes = _imageStateManager.GetLabels(imagePath)
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

            var loaded = LoadReviewForExplicitEdit(imagePath);
            _currentReviewState = _reviewWorkflow.ReplaceBoxesAfterEdit(loaded.State, boxes);
            _reviewRepository.Save(imagePath, _currentReviewState);
            SyncGtSummaryForImage(imagePath);
            UpdatePredictionFeatureUiState();
        }

        private void NormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingImage)
                return;

            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            ApplyAnomalyDecisionToItem(item, isNormal: true);
        }

        private void AbnormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingImage)
                return;

            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            ApplyAnomalyDecisionToItem(item, isNormal: false);
        }

        public MainWindow()
        {
            InitializeComponent();
            _fallbackAutoReviewPolicy = LoadFallbackAutoReviewPolicy();
            _batchImportService = new BatchImportService(_batchLibraryService);
            _reviewMigrationService = new LegacyReviewMigrationService(_reviewRepository);
            _trainingDatasetSelector = new TrainingDatasetSelector(_reviewRepository);
            _batchFilterOptions.Add(AllBatchFilterLabel);
            BatchFilterComboBox.ItemsSource = _batchFilterOptions;
            BatchFilterComboBox.SelectedItem = AllBatchFilterLabel;
            _bboxManager = new BoundingBoxManager(ImageCanvas);
            SetActiveDrawClass(_activeDrawClass);
            SetClassComboBoxSelection(_activeDrawClass);
            ClassComboBox.IsEnabled = false;
            _canvasInteractionManager = new CanvasInteractionManager(
                ImageScrollViewer,
                ImageScale,
                _bboxManager
            );
            _imageStateManager = new ImageStateManager();
            _datasetValidator = new TrainingDatasetValidator(_reviewRepository, _trainingDatasetSelector);
            UpdateDataSourceUiState();

            _images.CollectionChanged += Images_CollectionChanged;
            InitializeImageCollectionView();
            RefreshSummaryCounts();
            ResetImageDisplay();


            Loaded += (s, e) =>
            {
                TryRestoreLastLoadedBatch();

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            };

            Closed += (s, e) =>
            {
                _imageLoadRequestId++;
                CancelAndDispose(ref _imageLoadCancellation);
                CancelAndDispose(ref _imagePrefetchCancellation);
                CancelAndDispose(ref _rawViewLoadCancellation);
                _imageBitmapCache.Clear();
            };
        }

        private AutoReviewPolicy LoadFallbackAutoReviewPolicy()
        {
            try
            {
                AppSettings settings = AppSettingsLoader.LoadOrThrow(
                    FindProjectRoot("capstone_design"),
                    requireYoloPython: false,
                    requireAnomaPython: false);
                AutoReviewSection section = settings.AutoReview ?? new AutoReviewSection();
                return new AutoReviewPolicy
                {
                    Enabled = section.Enabled,
                    PolicyVersion = section.PolicyVersion,
                    AnomaNormalThresholdMultiplier = section.AnomaNormalThresholdMultiplier,
                    AnomaDefectThresholdMultiplier = section.AnomaDefectThresholdMultiplier,
                    YoloBoxMinConfidence = section.YoloBoxMinConfidence,
                    AuditSampleRate = 0
                };
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"auto review settings load failed; auto review disabled: {ex}");
                return AutoReviewPolicy.Disabled;
            }
        }

    }
}
