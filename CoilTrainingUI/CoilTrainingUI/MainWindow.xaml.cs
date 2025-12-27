using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using CoilTrainingUI.Models;
using CoilTrainingUI.Models;
using System.Collections.ObjectModel;
using IOPath = System.IO.Path;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        // 드래그 시작점
        private Point _startPoint;
        private Rectangle _currentRect;
        private bool _isDrawing = false;

        private Rectangle _selectedRect = null;
        private bool _isDraggingRect = false;
        private Point _dragStartPoint;

        private string _currentImagePath;

        private Dictionary<Rectangle, BoundingBox> _bboxMap
            = new Dictionary<Rectangle, BoundingBox>();

        private readonly Dictionary<string, int> _classToId = new()
        {
            { "dent", 0 },
            { "loose", 1 }
        };

        private ObservableCollection<ImageItem> _images
            = new ObservableCollection<ImageItem>();


        private void OnMouseWheelZoom(object sender, MouseWheelEventArgs e)
        {
            const double zoomStep = 0.1;

            if (e.Delta > 0)
            {
                ImageScale.ScaleX += zoomStep;
                ImageScale.ScaleY += zoomStep;
            }
            else
            {
                ImageScale.ScaleX -= zoomStep;
                ImageScale.ScaleY -= zoomStep;
            }

            // 최소 배율 제한 
            if (ImageScale.ScaleX < 0.1)
            {
                ImageScale.ScaleX = 0.1;
                ImageScale.ScaleY = 0.1;
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ImageScale.ScaleX += 0.1;
            ImageScale.ScaleY += 0.1;
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ImageScale.ScaleX = Math.Max(0.1, ImageScale.ScaleX - 0.1);
            ImageScale.ScaleY = Math.Max(0.1, ImageScale.ScaleY - 0.1);
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(ImageCanvas);
            _isDrawing = true;

            _currentRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };

            _currentRect.MouseLeftButtonDown += Rect_MouseLeftButtonDown;
            _currentRect.MouseMove += Rect_MouseMove;
            _currentRect.MouseLeftButtonUp += Rect_MouseLeftButtonUp;


            Canvas.SetLeft(_currentRect, _startPoint.X);
            Canvas.SetTop(_currentRect, _startPoint.Y);

            ImageCanvas.Children.Add(_currentRect);

            var bbox = new BoundingBox
            {
                ClassName = "dent" // 기본값
            };

            _bboxMap[_currentRect] = bbox;
        }
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _currentRect == null)
                return;

            Point currentPoint = e.GetPosition(ImageCanvas);

            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double w = Math.Abs(currentPoint.X - _startPoint.X);
            double h = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_currentRect, x);
            Canvas.SetTop(_currentRect, y);
            _currentRect.Width = w;
            _currentRect.Height = h;
        }
        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDrawing = false;
            _currentRect = null;
        }
        private void FitImageToView()
        {
            if (MainImage.Source == null)
                return;

            double viewWidth = ImageScrollViewer.ViewportWidth;
            double viewHeight = ImageScrollViewer.ViewportHeight;

            if (viewWidth <= 0 || viewHeight <= 0)
                return;

            double scaleX = viewWidth / ImageCanvas.Width;
            double scaleY = viewHeight / ImageCanvas.Height;

            double scale = Math.Min(scaleX, scaleY);

            ImageScale.ScaleX = scale;
            ImageScale.ScaleY = scale;
        }

        private void Rect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (_selectedRect != null)
                _selectedRect.Stroke = Brushes.Red;

            _selectedRect = sender as Rectangle;
            _selectedRect.Stroke = Brushes.LimeGreen;

            _isDraggingRect = true;
            _dragStartPoint = e.GetPosition(ImageCanvas);
            _selectedRect.CaptureMouse();

            // 클래스 UI 반영
            var bbox = _bboxMap[_selectedRect];

            ClassComboBox.IsEnabled = true;
            ClassComboBox.SelectedItem = ClassComboBox.Items
                .Cast<ComboBoxItem>()
                .First(item => item.Content.ToString() == bbox.ClassName);
        }


        private void Rect_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingRect || _selectedRect == null)
                return;

            Point currentPoint = e.GetPosition(ImageCanvas);

            double dx = currentPoint.X - _dragStartPoint.X;
            double dy = currentPoint.Y - _dragStartPoint.Y;

            double left = Canvas.GetLeft(_selectedRect);
            double top = Canvas.GetTop(_selectedRect);

            Canvas.SetLeft(_selectedRect, left + dx);
            Canvas.SetTop(_selectedRect, top + dy);

            _dragStartPoint = currentPoint;
        }
        private void Rect_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_selectedRect == null)
                return;

            _isDraggingRect = false;
            _selectedRect.ReleaseMouseCapture();

            UpdateBoundingBoxModel(_selectedRect);

        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedRect != null)
            {
                _bboxMap.Remove(_selectedRect);
                ImageCanvas.Children.Remove(_selectedRect);
                _selectedRect = null;
            }
        }

        private void UpdateBoundingBoxModel(Rectangle rect)
        {
            if (!_bboxMap.ContainsKey(rect))
                return;

            double left = Canvas.GetLeft(rect);
            double top = Canvas.GetTop(rect);
            double width = rect.Width;
            double height = rect.Height;

            double imgW = ImageCanvas.Width;
            double imgH = ImageCanvas.Height;

            var bbox = _bboxMap[rect];

            bbox.X = (left + width / 2) / imgW;
            bbox.Y = (top + height / 2) / imgH;
            bbox.Width = width / imgW;
            bbox.Height = height / imgH;
        }

        private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedRect == null)
                return;

            if (!(ClassComboBox.SelectedItem is ComboBoxItem item))
                return;

            string className = item.Content.ToString();

            var bbox = _bboxMap[_selectedRect];
            bbox.ClassName = className;

            // 색상 변경 (가시성 중요)
            _selectedRect.Stroke = className == "dent"
                ? Brushes.Red
                : Brushes.Blue;
        }

        private void SaveYoloLabel(string imagePath)
        {
            if (_bboxMap.Count == 0)
                return;

            string labelPath = System.IO.Path.ChangeExtension(imagePath, ".txt");

            using var writer = new System.IO.StreamWriter(labelPath);

            foreach (var pair in _bboxMap)
            {
                BoundingBox bbox = pair.Value;

                int classId = _classToId[bbox.ClassName];

                writer.WriteLine(
                    $"{classId} " +
                    $"{bbox.X:F6} {bbox.Y:F6} " +
                    $"{bbox.Width:F6} {bbox.Height:F6}"
                );
            }
        }
        private void SaveLabel_Click(object sender, RoutedEventArgs e)
        {
            // 임시: 현재 이미지 경로
            string imagePath = @"C:\\Users\\wnsgh\\Desktop\\input\\img2.jpg";

            SaveYoloLabel(imagePath);

            MessageBox.Show("YOLO label saved");
        }

        private void LoadYoloLabel(string imagePath)
        {
            string labelPath = System.IO.Path.ChangeExtension(imagePath, ".txt");

            if (!File.Exists(labelPath))
                return;

            foreach (var line in File.ReadAllLines(labelPath))
            {
                var parts = line.Split(' ');
                if (parts.Length != 5)
                    continue;

                int classId = int.Parse(parts[0]);
                double xCenter = double.Parse(parts[1]);
                double yCenter = double.Parse(parts[2]);
                double w = double.Parse(parts[3]);
                double h = double.Parse(parts[4]);

                string className = _classToId.First(kv => kv.Value == classId).Key;

                double x = (xCenter - w / 2) * ImageCanvas.Width;
                double y = (yCenter - h / 2) * ImageCanvas.Height;
                double width = w * ImageCanvas.Width;
                double height = h * ImageCanvas.Height;

                // Rectangle 생성
                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    StrokeThickness = 2,
                    Stroke = className == "dent" ? Brushes.Red : Brushes.Blue
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);

                rect.MouseLeftButtonDown += Rect_MouseLeftButtonDown;

                ImageCanvas.Children.Add(rect);

                // BoundingBox 모델 생성
                var bbox = new BoundingBox
                {
                    X = xCenter,
                    Y = yCenter,
                    Width = w,
                    Height = h,
                    ClassName = className
                };

                _bboxMap[rect] = bbox;
            }
        }

        private void LoadImage(string imagePath)
        {
            _currentImagePath = imagePath;

            var bitmap = new BitmapImage(new Uri(imagePath));
            MainImage.Source = bitmap;

            ImageCanvas.Width = bitmap.PixelWidth;
            ImageCanvas.Height = bitmap.PixelHeight;

            // 🔥 박스만 제거 (이미지는 유지)
            var rectsToRemove = ImageCanvas.Children
                .OfType<Rectangle>()
                .ToList();

            foreach (var rect in rectsToRemove)
                ImageCanvas.Children.Remove(rect);

            _bboxMap.Clear();

            LoadYoloLabel(imagePath);
        }

        private void LoadImageFolder(string folderPath)
        {
            _images.Clear();

            var imageFiles = Directory.GetFiles(folderPath, "*.jpg");

            foreach (var img in imageFiles)
            {
                string labelPath = IOPath.ChangeExtension(img, ".txt");

                _images.Add(new ImageItem
                {
                    FileName = IOPath.GetFileName(img),
                    FullPath = img,
                    HasLabel = File.Exists(labelPath)
                });
            }

            ImageListBox.ItemsSource = _images;
        }

        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is ImageItem item)
            {
                LoadImage(item.FullPath);
                FitImageToView();
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            string folderPath = @"C:\Users\wnsgh\Desktop\input";
            LoadImageFolder(folderPath);

            Loaded += (s, e) =>
            {
                FitImageToView();
            };
        }
    }
}