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
using CoilTrainingUI.Models;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private Point _startPoint;
        private Rectangle _currentRect;
        private bool _isDrawing = false;

        private Rectangle _selectedRect = null;
        private bool _isDraggingRect = false;
        private Point _dragStartPoint;

        private Dictionary<Rectangle, BoundingBox> _bboxMap
            = new Dictionary<Rectangle, BoundingBox>();

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




        public MainWindow()
        {
            InitializeComponent();

            // 테스트용 이미지 로드 (본인 PC 경로로 수정)
            var bitmap = new BitmapImage(new Uri(@"C:\\Users\\wnsgh\\Desktop\\input\\img2.jpg"));

            MainImage.Source = bitmap;

            // Canvas 크기를 이미지 실제 픽셀 크기로 설정
            ImageCanvas.Width = bitmap.PixelWidth;
            ImageCanvas.Height = bitmap.PixelHeight;

            // Window가 완전히 로드된 이후에 Fit 적용
            Loaded += (s, e) =>
            {
                FitImageToView();
            };
        }
    }
}