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

            _scale.ScaleX = Math.Max(0.1, _scale.ScaleX + delta);
            _scale.ScaleY = Math.Max(0.1, _scale.ScaleY + delta);
        }

        public void ZoomIn()
        {
            _scale.ScaleX += 0.1;
            _scale.ScaleY += 0.1;
        }

        public void ZoomOut()
        {
            _scale.ScaleX = Math.Max(0.1, _scale.ScaleX - 0.1);
            _scale.ScaleY = Math.Max(0.1, _scale.ScaleY - 0.1);
        }

        public void FitToView(double imageWidth, double imageHeight)
        {
            if (_scrollViewer.ViewportWidth <= 0 || _scrollViewer.ViewportHeight <= 0)
                return;

            double scaleX = _scrollViewer.ViewportWidth / imageWidth;
            double scaleY = _scrollViewer.ViewportHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            _scale.ScaleX = scale;
            _scale.ScaleY = scale;
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

        public void EndDrag(double imgW, double imgH)
        {
            _bboxManager.EndDrag(imgW, imgH);
        }
    }
}
