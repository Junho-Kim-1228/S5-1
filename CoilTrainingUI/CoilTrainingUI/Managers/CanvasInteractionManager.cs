using CoilTrainingUI.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CoilTrainingUI.Managers
{
    public class CanvasInteractionManager
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly ScaleTransform _scale;
        private readonly BoundingBoxManager _bboxManager;
        private double _contentWidth;
        private double _contentHeight;
        private const double MaxZoomScale = 10.0;

        public CanvasInteractionManager(
            ScrollViewer scrollViewer,
            ScaleTransform scale,
            BoundingBoxManager bboxManager)
        {
            _scrollViewer = scrollViewer;
            _scale = scale;
            _bboxManager = bboxManager;
        }

        // =========================
        // Zoom
        // =========================
        public void OnMouseWheel(MouseWheelEventArgs e)
        {
            double zoomStep = 0.1;
            double delta = e.Delta > 0 ? zoomStep : -zoomStep;
            ApplyScale(_scale.ScaleX + delta);
        }

        public void ZoomIn()
        {
            ApplyScale(_scale.ScaleX + 0.1);
        }

        public void ZoomOut()
        {
            ApplyScale(_scale.ScaleX - 0.1);
        }

        public void FitToView(double imageWidth, double imageHeight)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
                return;

            _contentWidth = imageWidth;
            _contentHeight = imageHeight;

            double scale = GetMinFitScale();
            if (scale <= 0)
                return;

            _scale.ScaleX = scale;
            _scale.ScaleY = scale;
            ClampScrollOffsets();
        }

        public void EnsureWithinBounds()
        {
            if (_contentWidth <= 0 || _contentHeight <= 0)
                return;

            ApplyScale(_scale.ScaleX);
        }

        public void OnScrollChanged()
        {
            if (_contentWidth <= 0 || _contentHeight <= 0)
                return;

            ClampScrollOffsets();
        }

        private void ApplyScale(double requestedScale)
        {
            double minScale = GetMinFitScale();
            double maxScale = Math.Max(minScale, MaxZoomScale);
            double scale = Math.Clamp(requestedScale, minScale, maxScale);
            _scale.ScaleX = scale;
            _scale.ScaleY = scale;
            ClampScrollOffsets();
        }

        private double GetMinFitScale()
        {
            if (_contentWidth <= 0 || _contentHeight <= 0)
                return 0.1;

            if (_scrollViewer.ViewportWidth <= 0 || _scrollViewer.ViewportHeight <= 0)
                return 0.1;

            double scaleX = _scrollViewer.ViewportWidth / _contentWidth;
            double scaleY = _scrollViewer.ViewportHeight / _contentHeight;
            return Math.Max(0.1, Math.Min(scaleX, scaleY));
        }

        private void ClampScrollOffsets()
        {
            _scrollViewer.UpdateLayout();

            double h = Math.Clamp(_scrollViewer.HorizontalOffset, 0, _scrollViewer.ScrollableWidth);
            double v = Math.Clamp(_scrollViewer.VerticalOffset, 0, _scrollViewer.ScrollableHeight);
            _scrollViewer.ScrollToHorizontalOffset(h);
            _scrollViewer.ScrollToVerticalOffset(v);
        }

        // =========================
        // Canvas Input
        // =========================
        public void StartDraw(Point point)
        {
            _bboxManager.StartDraw(point);
        }

        public void UpdateDraw(Point point)
        {
            _bboxManager.UpdateDraw(point);
        }

        public BoundingBox? EndDraw(double imgW, double imgH)
        {
            return _bboxManager.EndDraw(imgW, imgH);
        }

        public BoundingBox? Select(Rectangle rect, Point point)
        {
            return _bboxManager.Select(rect, point);
        }

        public void Drag(Point point)
        {
            _bboxManager.Drag(point);
        }

        public bool EndDrag(double imgW, double imgH)
        {
            return _bboxManager.EndDrag(imgW, imgH);
        }
    }
}
