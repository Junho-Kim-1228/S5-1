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


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private Point _startPoint;
        private Rectangle _currentRect;
        private bool _isDrawing = false;

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
                StrokeThickness = 2
            };

            Canvas.SetLeft(_currentRect, _startPoint.X);
            Canvas.SetTop(_currentRect, _startPoint.Y);

            ImageCanvas.Children.Add(_currentRect);
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



        //public MainWindow()
        //{
        //    InitializeComponent();

        //    Loaded += (s, e) =>
        //    {
        //        FitImageToView();
        //    };

        //    // 테스트용 이미지 로드 (아무 jpg/png 하나 경로 넣어도 됨)
        //    var bitmap = new BitmapImage(new Uri("C:\\Users\\wnsgh\\Desktop\\input\\img2.jpg"));
        //    MainImage.Source = bitmap;

        //    // Canvas 크기를 이미지 크기에 맞춤
        //    ImageCanvas.Width = bitmap.PixelWidth;
        //    ImageCanvas.Height = bitmap.PixelHeight;
        //}
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