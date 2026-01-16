using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CoilTrainingUI.Models;


using IOPath = System.IO.Path;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private YoloLabelService _yoloService;
        private BoundingBoxManager _bboxManager;
        private readonly DatasetExportService _exportService = new();
        private CanvasInteractionManager _canvasInteractionManager;
        private ImageStateManager _imageStateManager;
        private AnomalyStateService _anomalyService;
        private RoiStateService _roiService;
        private BitmapSource _originalBitmap;
        private RoiPreprocessService _roiPreprocessService;


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
        }

        private void SaveLabel_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            // 🔥🔥🔥 이 줄이 핵심
            _bboxManager.ForceUpdateAll(
                ImageCanvas.Width,
                ImageCanvas.Height
            );

            var labels = _imageStateManager.GetLabels(_currentImagePath);
            _yoloService.Save(_currentImagePath, labels);

            MessageBox.Show("YOLO label saved");
        }


        private void LoadImage(string imagePath)
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

            // 4️⃣ YOLO 라벨 로드
            _imageStateManager.ClearLabels(imagePath);
            _yoloService.Load(
                imagePath,
                _imageStateManager.GetMutableLabels(imagePath)
            );

            foreach (var bbox in _imageStateManager.GetLabels(imagePath))
            {
                _bboxManager.AddFromModel(
                    bbox,
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            }

            // 5️⃣ Anomaly 상태
            bool isNormal = _anomalyService.Load(imagePath);
            _imageStateManager.SetNormal(imagePath, isNormal);

            // 6️⃣ ROI 타입
            var roiType = _roiService.Load(imagePath);
            _imageStateManager.SetRoiType(imagePath, roiType);

            // 7️⃣ UI 반영
            if (ImageListBox.SelectedItem is ImageItem item)
            {
                item.IsNormal = isNormal;
                item.HasLabel = _imageStateManager.HasLabel(imagePath);
                item.RoiType = roiType;

                NormalRadio.IsChecked = isNormal;
                AbnormalRadio.IsChecked = !isNormal;
            }

            RestoreRoiTypeUI(imagePath);
            ImageListBox.Items.Refresh();

            // 🔥 8️⃣ ROI 체크 상태에 따라 화면 갱신 (이게 핵심)
            UpdateRoiDisplay();
        }

        private void UpdateRoiDisplay()
        {
            if (_rawBitmap == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            if (ShowRoiCheckBox.IsChecked == true)
            {
                var roiType = _imageStateManager.GetRoiType(_currentImagePath);

                var processed = _roiPreprocessService.GetOrCreateProcessedImage(
                    _currentImagePath,
                    roiType
                );

                MainImage.Source = processed;
            }
            else
            {
                MainImage.Source = _rawBitmap;
            }
        }


        private void LoadImageFolder(string folderPath)
        {
            _images.Clear();

            var imageFiles = Directory.GetFiles(folderPath, "*.bmp");

            foreach (var img in imageFiles)
            {
                // 1️⃣ YOLO 라벨 실제 로드
                var labels = new List<BoundingBox>();
                _yoloService.Load(img, labels);

                bool hasLabel = labels.Count > 0;

                // 2️⃣ Anomaly 상태 로드
                bool isNormal = _anomalyService.Load(img);

                _images.Add(new ImageItem
                {
                    FileName = IOPath.GetFileName(img),
                    FullPath = img,
                    HasLabel = hasLabel,
                    IsNormal = isNormal
                });
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
        }

        private void AbnormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            _imageStateManager.SetNormal(item.FullPath, false);
            _anomalyService.Save(item.FullPath, false);

            item.IsNormal = false;

            ImageListBox.Items.Refresh();
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
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            if (sender is not RadioButton rb)
                return;

            if (!Enum.TryParse<RoiType>(rb.Tag.ToString(), out var roiType))
                return;

            // 1️⃣ 메모리
            item.RoiType = roiType;

            // 2️⃣ 파일 저장
            _roiService.Save(item.FullPath, roiType);

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
            UpdateRoiDisplay();
        }

        private void ShowRoiCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateRoiDisplay();
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

            string folderPath = @"C:\Users\wnsgh\Desktop\input";
            LoadImageFolder(folderPath);

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