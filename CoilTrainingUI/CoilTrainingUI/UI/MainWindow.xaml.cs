using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        private readonly string _defaultInputFolder = @"C:\Users\wnsgh\Desktop\input";
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

        private string _currentImagePath;


        private readonly Dictionary<string, int> _classToId = new()
        {
            { "dent", 0 },
            { "loose", 1 }
        };

        private ObservableCollection<ImageItem> _images
            = new ObservableCollection<ImageItem>();

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
            var item = _images.FirstOrDefault(i => i.FullPath == _currentImagePath);
            if (item != null)
            {
                item.HasLabel = _imageStateManager.HasLabel(_currentImagePath);
                ImageListBox.Items.Refresh();
            }

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
                    item.HasLabel = hasGtLabel;

                    NormalRadio.IsChecked = isNormal;
                    AbnormalRadio.IsChecked = !isNormal;
                }

                ImageListBox.Items.Refresh();

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
        private void NormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            // 1️⃣ 메모리 상태 변경
            _imageStateManager.SetNormal(item.ProcessedPath, true);

            // 2️⃣ 파일 저장
            _anomalyService.Save(item.ProcessedPath, true);

            // 3️⃣ UI 모델 반영
            item.IsNormal = true;

            ImageListBox.Items.Refresh();
            RefreshSummaryCounts();
        }

        private void AbnormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            _imageStateManager.SetNormal(item.ProcessedPath, false);
            _anomalyService.Save(item.ProcessedPath, false);

            item.IsNormal = false;

            ImageListBox.Items.Refresh();
            RefreshSummaryCounts();
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
            _canvasInteractionManager = new CanvasInteractionManager(
                ImageScrollViewer,
                ImageScale,
                _bboxManager
            );
            _imageStateManager = new ImageStateManager();
            _anomalyService = new AnomalyStateService();
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
            ImageListBox.ItemsSource = _images;
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
