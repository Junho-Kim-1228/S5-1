using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private bool _isLoadingImage;

        private YoloLabelService _yoloService;
        private BoundingBoxManager _bboxManager;
        private readonly InferenceBatchImportService _inferenceBatchImportService = new();
        private readonly BatchLibraryService _batchLibraryService = new();
        private readonly BatchMergeService _batchMergeService = new();
        private CanvasInteractionManager _canvasInteractionManager;
        private ImageStateManager _imageStateManager;
        private AnomalyStateService _anomalyService;
        private readonly ImageStateService _stateService = new();
        private readonly TrainingDatasetValidator _datasetValidator;
        private readonly BatchPredictionReviewService _predictionReviewService;

        private readonly Dictionary<string, string> _inferJsonByImagePath = new(StringComparer.OrdinalIgnoreCase);
        private const string PredictionOverlayTag = "__prediction_overlay";
        private const string AllBatchFilterLabel = "(전체 배치)";
        private string? _currentBatchRoot;
        private bool _currentBatchHasAnyInfer;

        private DispatcherTimer _labelSaveDebounceTimer;
        private string? _pendingSaveImagePath;
        private const int LabelSaveDebounceMs = 300;

        // 항상 원본은 유지
        private BitmapSource? _rawBitmap;
        private BitmapSource? _rawViewBitmap;
        private string? _rawViewBitmapPath;
        private bool _suppressRawToggleEvent;
        private int _imageListWheelDeltaAccumulator;
        private const int ImageListWheelDeltaStep = 240;

        private string? _currentImagePath;
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
            
            RequestSaveLabelsDebounced(currentImagePath);


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

        private void ImageCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _bboxManager.Drag(
                e.GetPosition(ImageCanvas)
            );
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
                RequestSaveLabelsDebounced(currentImagePath);
            }
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

            RequestSaveLabelsDebounced(currentImagePath);
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

            RequestSaveLabelsDebounced(currentImagePath);

            // ✅ 클래스 변경 반영 저장
            SaveLabelsToStateJson(currentImagePath, markManualYoloDecision: true);

        }

        private void SaveLabelsToStateJson(string imagePath, bool markManualYoloDecision = false)
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
                    Height = b.Height,
                    Source = "manual",
                    InferConf = null
                });
            }

            if (markManualYoloDecision)
            {
                // 박스 편집만으로 이미지 정상/불량 판정을 확정하지 않는다.
                state.HasManualYoloDecision = true;
                if (state.IsManualAnomalyDecision)
                {
                    state.ReviewStatus = ReviewStatus.ReviewDone;
                    state.ReviewReasons.Clear();
                    state.ReviewedAt = DateTime.UtcNow;
                    state.DecisionSource = "manual";
                }
                else
                {
                    state.ReviewStatus = ReviewStatus.ReviewNeeded;
                    state.ReviewReasons = new List<string> { "bbox_edited_pending_confirmation" };
                    state.ReviewedAt = null;
                    state.DecisionSource = "";
                }
            }

            _stateService.Save(imagePath, state);
            SyncGtSummaryForImage(imagePath);
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
            _batchFilterOptions.Add(AllBatchFilterLabel);
            BatchFilterComboBox.ItemsSource = _batchFilterOptions;
            BatchFilterComboBox.SelectedItem = AllBatchFilterLabel;
            _yoloService = new YoloLabelService(_classToId);
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
            _anomalyService = new AnomalyStateService();
            _datasetValidator = new TrainingDatasetValidator(_stateService);
            _predictionReviewService = new BatchPredictionReviewService(_stateService);
            UpdateDataSourceUiState();

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
        }

    }
}
