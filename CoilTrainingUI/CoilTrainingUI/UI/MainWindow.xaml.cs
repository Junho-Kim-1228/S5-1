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
        private CanvasInteractionManager _canvasInteractionManager;
        private ImageStateManager _imageStateManager;
        private AnomalyStateService _anomalyService;
        private readonly ImageStateService _stateService = new();
        private readonly TrainingDatasetValidator _datasetValidator;
        private readonly BatchPredictionReviewService _predictionReviewService;

        private readonly Dictionary<string, string> _inferJsonByImagePath = new(StringComparer.OrdinalIgnoreCase);
        private const string PredictionOverlayTag = "__prediction_overlay";
        private string? _currentBatchRoot;
        private string _currentBatchType = "";
        private bool _currentBatchRequiresInfer;
        private bool _currentBatchHasAnyInfer;

        private DispatcherTimer _labelSaveDebounceTimer;
        private string? _pendingSaveImagePath;
        private const int LabelSaveDebounceMs = 300;

        // 항상 원본은 유지
        private BitmapSource _rawBitmap;
        private BitmapSource? _rawViewBitmap;
        private string? _rawViewBitmapPath;
        private bool _suppressRawToggleEvent;
        private int _imageListWheelDeltaAccumulator;
        private const int ImageListWheelDeltaStep = 240;

        private string _currentImagePath;
        private string _activeDrawClass = "dent";
        private bool _suppressClassComboBoxChange;


        private readonly Dictionary<string, int> _classToId = new()
        {
            { "dent", 0 },
            { "loose", 1 }
        };

        private ObservableCollection<ImageItem> _images
            = new ObservableCollection<ImageItem>();
        private ICollectionView? _imageCollectionView;
        private bool _suppressFilterRefresh;

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

            return PassStatusFilter(item)
                && PassDefectTypeFilter(item)
                && PassReviewPriorityFilter(item)
                && PassDataQualityFilter(item);
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

            var counts = GetEffectiveDefectCounts(item);
            int total = counts.Dent + counts.Loose + counts.Other;

            if (includeNormal && total == 0)
                return true;
            if (includeDent && counts.Dent > 0 && counts.Loose == 0 && counts.Other == 0)
                return true;
            if (includeLoose && counts.Loose > 0 && counts.Dent == 0 && counts.Other == 0)
                return true;
            if (includeNoLabel && item.GtDentCount + item.GtLooseCount + item.GtOtherCount == 0)
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

        private static (int Dent, int Loose, int Other) GetEffectiveDefectCounts(ImageItem item)
        {
            if (item.HasLabel)
                return (item.GtDentCount, item.GtLooseCount, item.GtOtherCount);

            if (item.HasAiInfer)
                return (item.AiDentCount, item.AiLooseCount, item.AiOtherCount);

            return (0, 0, 0);
        }

        private static (int Dent, int Loose, int Other) CountDefectClasses(IEnumerable<string?> classNames)
        {
            int dent = 0;
            int loose = 0;
            int other = 0;

            foreach (var className in classNames)
            {
                string normalized = (className ?? "").Trim().ToLowerInvariant();
                if (normalized == "dent")
                {
                    dent++;
                    continue;
                }

                if (normalized == "loose")
                {
                    loose++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(normalized))
                    other++;
            }

            return (dent, loose, other);
        }

        private void UpdateGtSummaryForImageItem(ImageItem item, string imagePath)
        {
            var boxes = _imageStateManager.GetLabels(imagePath);
            var counts = CountDefectClasses(boxes.Select(b => b.ClassName));
            var state = _stateService.Load(imagePath);
            item.GtDentCount = counts.Dent;
            item.GtLooseCount = counts.Loose;
            item.GtOtherCount = counts.Other;
            item.HasLabel = state.HasManualYoloDecision && boxes.Count > 0;
            item.HasStateFile = _stateService.HasState(imagePath);
            item.ReviewStatus = DeriveReviewStatusForItem(item, state);
            item.ReviewReasonText = state.ReviewReasons.Count > 0
                ? string.Join(", ", state.ReviewReasons.Take(3))
                : "";
        }

        private void SyncGtSummaryForImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            var item = _images.FirstOrDefault(i => i.ProcessedPath == imagePath);
            if (item == null)
                return;

            UpdateGtSummaryForImageItem(item, imagePath);
        }

        private static string DeriveReviewStatusForItem(ImageItem item, ImageStateDto state)
        {
            if (state.HasManualYoloDecision || state.HasManualAnomalyDecision)
                return ReviewStatus.ReviewDone;

            string normalized = (state.ReviewStatus ?? "").Trim().ToLowerInvariant();
            if (normalized == ReviewStatus.ReviewNeeded ||
                normalized == ReviewStatus.AutoCandidate ||
                normalized == ReviewStatus.ReviewDone)
            {
                return normalized;
            }

            if (item.InferParseFailed)
                return ReviewStatus.ReviewNeeded;

            if (item.RequiresInfer && !item.HasInferFile)
                return ReviewStatus.ReviewNeeded;

            if (item.HasAiInfer)
                return item.AiConsensusHighConfidence ? ReviewStatus.AutoCandidate : ReviewStatus.ReviewNeeded;

            return ReviewStatus.None;
        }

        private void ApplyAnomalyDecisionToItem(ImageItem item, bool isNormal, bool refreshSummary = true)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ProcessedPath))
                return;

            _imageStateManager.SetNormal(item.ProcessedPath, isNormal);
            _anomalyService.Save(item.ProcessedPath, isNormal);

            item.IsNormal = isNormal;
            item.HasStateFile = true;
            item.ReviewStatus = ReviewStatus.ReviewDone;
            item.ReviewReasonText = "";

            if (refreshSummary)
                RefreshSummaryCounts();
        }

        private void EnsureSelectedImageVisible()
        {
            if (ImageListBox.SelectedItem is ImageItem selectedItem && IsVisibleInCurrentFilter(selectedItem))
                return;

            var firstVisible = _imageCollectionView?.Cast<object>()
                .OfType<ImageItem>()
                .FirstOrDefault();

            if (firstVisible != null)
            {
                ImageListBox.SelectedItem = firstVisible;
                ImageListBox.ScrollIntoView(firstVisible);
                return;
            }

            ImageListBox.SelectedItem = null;
            ResetImageDisplay();
        }

        private void SyncAnomalyRadioFromSelectedItem()
        {
            _isLoadingImage = true;
            try
            {
                if (ImageListBox.SelectedItem is not ImageItem item)
                {
                    NormalRadio.IsChecked = false;
                    AbnormalRadio.IsChecked = false;
                    return;
                }

                NormalRadio.IsChecked = item.IsNormal;
                AbnormalRadio.IsChecked = !item.IsNormal;
            }
            finally
            {
                _isLoadingImage = false;
            }
        }

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

            // 1️⃣ 상태 저장
            _imageStateManager.AddLabel(_currentImagePath, bbox);

            // 2️⃣ 🔥 방금 만든 박스를 자동 선택 상태로 만들기
            _bboxManager.SelectLastCreated();

            // 3️⃣ 클래스 UI 활성화 + 기본값 반영
            ClassComboBox.IsEnabled = true;
            SetClassComboBoxSelection(bbox.ClassName);
            
            RequestSaveLabelsDebounced(_currentImagePath);


            SaveLabelsToStateJson(_currentImagePath, markManualYoloDecision: true);
            SyncGtSummaryForImage(_currentImagePath);
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
            if (!string.IsNullOrEmpty(_currentImagePath))
            {
                SaveLabelsToStateJson(_currentImagePath, markManualYoloDecision: true);
                RequestSaveLabelsDebounced(_currentImagePath);
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

            var removedBBox = _bboxManager.DeleteSelected();
            if (removedBBox == null)
                return;

            // 1️⃣ 메모리 상태에서 제거
            _imageStateManager.RemoveLabel(_currentImagePath, removedBBox);

            // 2️⃣ UI 모델 상태 갱신
            SyncGtSummaryForImage(_currentImagePath);

            // ✅ 삭제 반영 저장
            SaveLabelsToStateJson(_currentImagePath, markManualYoloDecision: true);

            RequestSaveLabelsDebounced(_currentImagePath);
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

            _bboxManager.SetSelectedClass(className);

            // ✅ 상태는 ImageStateManager 기준으로 갱신
            SyncGtSummaryForImage(_currentImagePath);

            RequestSaveLabelsDebounced(_currentImagePath);

            // ✅ 클래스 변경 반영 저장
            SaveLabelsToStateJson(_currentImagePath, markManualYoloDecision: true);

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
                // 라벨 편집(추가/삭제/수정)이 발생한 경우에만 수동 확정으로 본다.
                state.HasManualYoloDecision = true;
                state.ReviewStatus = ReviewStatus.ReviewDone;
                state.ReviewReasons.Clear();
                state.ReviewedAt = DateTime.UtcNow;
            }

            _stateService.Save(imagePath, state);
            SyncGtSummaryForImage(imagePath);
        }




        private void LoadImage(string imagePath)
        {
            _isLoadingImage = true;
            try
            {
                _currentImagePath = imagePath;
                ClassComboBox.IsEnabled = true;
                SetClassComboBoxSelection(_activeDrawClass);

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

                UpdatePredictionOverlayVisibility(imagePath);

                // 5️⃣ Anomaly 상태
                // 현재 앱은 training_inbox 라이브러리 기반이므로
                // 수동 확정이 없는 경우 기본 정상(true)으로 처리한다.
                bool isNormal = (state.HasManualAnomalyDecision && state.IsNormal.HasValue)
                    ? state.IsNormal.Value
                    : true;
                _imageStateManager.SetNormal(imagePath, isNormal);

                // 6️⃣ UI 반영
                if (ImageListBox.SelectedItem is ImageItem item)
                {
                    item.IsNormal = isNormal;
                    UpdateGtSummaryForImageItem(item, imagePath);

                    NormalRadio.IsChecked = isNormal;
                    AbnormalRadio.IsChecked = !isNormal;
                }

                // 7️⃣ 표시 모드(raw/processed) 체크 상태에 따라 화면 갱신
                UpdateMainImageDisplayFromToggle();
            }
            finally
            {
                _isLoadingImage = false;
            }
        }

        private void UpdateMainImageDisplayFromToggle()
        {
            if (_rawBitmap == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            UpdateMainImageSourceFromViewToggle(showMissingRawMessage: false, fallbackSource: _rawBitmap);
        }

        private void UpdateMainImageSourceFromViewToggle(bool showMissingRawMessage, BitmapSource? fallbackSource = null)
        {
            BitmapSource? processedSource = fallbackSource ?? _rawBitmap;
            if (processedSource == null)
                return;

            if (ShowRawCheckBox.IsChecked != true)
            {
                MainImage.Source = processedSource;
                return;
            }

            if (ImageListBox.SelectedItem is not ImageItem currentItem)
            {
                MainImage.Source = processedSource;
                return;
            }

            if (string.IsNullOrWhiteSpace(currentItem.RawPath) || !File.Exists(currentItem.RawPath))
            {
                if (showMissingRawMessage)
                {
                    MessageBox.Show(
                        "RAW 이미지가 배치에 없습니다.",
                        "Show RAW",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }

                _suppressRawToggleEvent = true;
                ShowRawCheckBox.IsChecked = false;
                _suppressRawToggleEvent = false;
                MainImage.Source = processedSource;
                return;
            }

            if (!string.Equals(_rawViewBitmapPath, currentItem.RawPath, StringComparison.OrdinalIgnoreCase) ||
                _rawViewBitmap == null)
            {
                var rawBitmap = new BitmapImage();
                rawBitmap.BeginInit();
                rawBitmap.UriSource = new Uri(currentItem.RawPath, UriKind.Absolute);
                rawBitmap.CacheOption = BitmapCacheOption.OnLoad;
                rawBitmap.EndInit();

                _rawViewBitmap = rawBitmap;
                _rawViewBitmapPath = currentItem.RawPath;
            }

            MainImage.Source = _rawViewBitmap;
        }


        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePredictionFeatureUiState();

            if (ImageListBox.SelectedItem is ImageItem item)
            {
                LoadImage(item.ProcessedPath);

                NormalRadio.IsChecked = item.IsNormal;
                AbnormalRadio.IsChecked = !item.IsNormal;

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            }
        }

        private void ImageListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject source)
                return;

            var scrollViewer = FindVisualChild<ScrollViewer>(source);
            if (scrollViewer == null)
                return;

            // 터치패드 두 손가락 스크롤이 너무 빠른 환경을 위해 감속 처리
            _imageListWheelDeltaAccumulator += e.Delta;

            while (_imageListWheelDeltaAccumulator >= ImageListWheelDeltaStep)
            {
                scrollViewer.LineUp();
                _imageListWheelDeltaAccumulator -= ImageListWheelDeltaStep;
            }

            while (_imageListWheelDeltaAccumulator <= -ImageListWheelDeltaStep)
            {
                scrollViewer.LineDown();
                _imageListWheelDeltaAccumulator += ImageListWheelDeltaStep;
            }

            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T target)
                    return target;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
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


        private void ShowPredictionCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePredictionOverlayVisibility();
        }

        private void ShowPredictionCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdatePredictionOverlayVisibility();
        }

        private void ShowRawCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressRawToggleEvent)
                return;

            UpdateMainImageSourceFromViewToggle(showMissingRawMessage: true);
        }

        private void ShowRawCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressRawToggleEvent)
                return;

            UpdateMainImageSourceFromViewToggle(showMissingRawMessage: false);
        }


        public MainWindow()
        {
            InitializeComponent();
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
