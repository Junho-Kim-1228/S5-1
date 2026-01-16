using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Managers
{
    public class BoundingBoxManager
    {
        private readonly Canvas _canvas;
        private readonly Dictionary<Rectangle, BoundingBox> _bboxMap = new();

        private Rectangle _currentRect;
        private Rectangle _selectedRect;

        private Point _startPoint;
        private Point _dragStartPoint;

        private bool _isDrawing;
        private bool _isDragging;
        private bool _hasDragged;

        public BoundingBoxManager(Canvas canvas)
        {
            // 중요: 전달받은 canvas를 내부 변수 _canvas에 할당해야 합니다!
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        // ========================
        // 생성
        // ========================
        public void StartDraw(Point startPoint)
        {
            _startPoint = startPoint;
            _isDrawing = true;
            _hasDragged = false;

            _currentRect = null; // 🔥 여기서 만들지 않는다
        }


        public void UpdateDraw(Point currentPoint)
        {
            if (!_isDrawing)
                return;

            double dx = Math.Abs(currentPoint.X - _startPoint.X);
            double dy = Math.Abs(currentPoint.Y - _startPoint.Y);

            // 🔥 드래그 인정 임계값 (픽셀)
            if (!_hasDragged && (dx > 3 || dy > 3))
            {
                _hasDragged = true;

                _currentRect = new Rectangle
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                };

                Canvas.SetLeft(_currentRect, _startPoint.X);
                Canvas.SetTop(_currentRect, _startPoint.Y);

                _canvas.Children.Add(_currentRect);

                _bboxMap[_currentRect] = new BoundingBox
                {
                    ClassName = "dent" // ✅ 항상 기본값
                };
            }

            if (_currentRect == null)
                return;

            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double w = Math.Abs(currentPoint.X - _startPoint.X);
            double h = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_currentRect, x);
            Canvas.SetTop(_currentRect, y);
            _currentRect.Width = w;
            _currentRect.Height = h;
        }


        public BoundingBox EndDraw(double imageWidth, double imageHeight)
        {
            _isDrawing = false;

            // 🔥 드래그 자체가 없었다면 무효
            if (!_hasDragged || _currentRect == null)
            {
                _currentRect = null;
                return null;
            }

            // 안전망 (거의 안 걸림)
            if (_currentRect.Width <= 1 || _currentRect.Height <= 1)
            {
                RemoveRect(_currentRect);
                _currentRect = null;
                return null;
            }

            // ✅ 이제 여기서만 좌표 계산
            UpdateBBoxModel(_currentRect, imageWidth, imageHeight);

            var finishedBBox = _bboxMap[_currentRect];
            _currentRect = null;

            return finishedBBox;
        }


        public void ForceUpdateAll(double imgW, double imgH)
        {
            foreach (var pair in _bboxMap)
            {
                var rect = pair.Key;

                // 안전장치
                if (rect.Width <= 1 || rect.Height <= 1)
                    continue;

                UpdateBBoxModel(rect, imgW, imgH);
            }
        }

        // ========================
        // 선택 / 이동
        // ========================
        public BoundingBox Select(Rectangle rect, Point mousePoint)
        {
            if (!_bboxMap.ContainsKey(rect))
                return null;

            if (rect.Width <= 1 || rect.Height <= 1)
                return null;

            RestoreSelectedColor();

            _selectedRect = rect;
            _selectedRect.Stroke = Brushes.LimeGreen;

            _isDragging = true;
            _dragStartPoint = mousePoint;
            _selectedRect.CaptureMouse();

            SelectedBBox = _bboxMap[_selectedRect];

            return _bboxMap[rect];
        }

        public void Drag(Point currentPoint)
        {
            if (!_isDragging || _selectedRect == null)
                return;

            double dx = currentPoint.X - _dragStartPoint.X;
            double dy = currentPoint.Y - _dragStartPoint.Y;

            Canvas.SetLeft(_selectedRect, Canvas.GetLeft(_selectedRect) + dx);
            Canvas.SetTop(_selectedRect, Canvas.GetTop(_selectedRect) + dy);

            _dragStartPoint = currentPoint;
        }

        public void EndDrag(double imageWidth, double imageHeight)
        {
            if (_selectedRect == null)
                return;

            _isDragging = false;
            _selectedRect.ReleaseMouseCapture();

            UpdateBBoxModel(_selectedRect, imageWidth, imageHeight);
        }

        // ========================
        // 삭제
        // ========================
        public BoundingBox DeleteSelected()
        {
            if (_selectedRect == null)
                return null;

            var bbox = _bboxMap[_selectedRect];
            RemoveRect(_selectedRect);
            _selectedRect = null;

            return bbox;
        }

        // ========================
        // 유틸
        // ========================
        private void UpdateBBoxModel(Rectangle rect, double imgW, double imgH)
        {
            var bbox = _bboxMap[rect];

            double left = Canvas.GetLeft(rect);
            double top = Canvas.GetTop(rect);

            bbox.X = (left + rect.Width / 2) / imgW;
            bbox.Y = (top + rect.Height / 2) / imgH;
            bbox.Width = rect.Width / imgW;
            bbox.Height = rect.Height / imgH;
        }

        private void RestoreSelectedColor()
        {
            if (_selectedRect == null || !_bboxMap.ContainsKey(_selectedRect))
                return;

            var bbox = _bboxMap[_selectedRect];
            _selectedRect.Stroke = bbox.ClassName == "dent"
                ? Brushes.Red
                : Brushes.Blue;
        }

        private void RemoveRect(Rectangle rect)
        {
            if (rect == null)
                return;

            _bboxMap.Remove(rect);
            _canvas.Children.Remove(rect);
        }

        public void ClearAll()
        {
            // 이제 _canvas가 null이 아니므로 안전하게 호출됩니다.
            if (_canvas != null)
            {
                _canvas.Children.Clear();
                _bboxMap.Clear();
                _selectedRect = null;
            }
        }

        public BoundingBox? SelectedBBox { get; private set; }

        public void SetSelectedClass(string className)
        {
            if (_selectedRect == null)
                return;

            var bbox = _bboxMap[_selectedRect];
            bbox.ClassName = className;

            _selectedRect.Stroke = className == "dent"
                ? Brushes.Red
                : Brushes.Blue;

            SelectedBBox = bbox;
        }
        public void AddFromModel(BoundingBox bbox, double imgW, double imgH)
        {
            double x = (bbox.X - bbox.Width / 2) * imgW;
            double y = (bbox.Y - bbox.Height / 2) * imgH;

            var rect = new Rectangle
            {
                Width = bbox.Width * imgW,
                Height = bbox.Height * imgH,
                StrokeThickness = 2,
                Stroke = bbox.ClassName == "dent" ? Brushes.Red : Brushes.Blue,
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);

            _canvas.Children.Add(rect);
            _bboxMap[rect] = bbox;
        }

        public void ClearSelection()
        {
            if (_selectedRect != null && _bboxMap.ContainsKey(_selectedRect))
            {
                RestoreSelectedColor();
                _selectedRect = null;
                SelectedBBox = null;
            }
        }

        public void SelectLastCreated()
        {
            if (_bboxMap.Count == 0)
                return;

            var last = _bboxMap.Last();
            RestoreSelectedColor();

            _selectedRect = last.Key;
            _selectedRect.Stroke = Brushes.LimeGreen;

            SelectedBBox = last.Value;
        }




    }
}
