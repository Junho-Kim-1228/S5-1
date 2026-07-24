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
        private const double BoxStrokeThickness = 6;
        private const double ResizeHitThickness = 10;
        private const double MinimumBoxSize = 8;

        private enum PointerDragMode
        {
            None,
            Move,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private readonly Canvas _canvas;
        private readonly Dictionary<Rectangle, BoundingBox> _bboxMap = new();

        private Rectangle? _currentRect;
        private Rectangle? _selectedRect;

        private Point _startPoint;
        private Point _dragStartPoint;
        private double _dragStartLeft;
        private double _dragStartTop;
        private double _dragStartWidth;
        private double _dragStartHeight;
        private PointerDragMode _dragMode;

        private bool _isDrawing;
        private bool _isDragging;
        private bool _hasDragged;
        private bool _dragMoved;
        private string _defaultClassName = "dent";

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
                    Stroke = GetStrokeBrush(_defaultClassName),
                    StrokeThickness = BoxStrokeThickness,
                    Fill = Brushes.Transparent
                };

                Canvas.SetLeft(_currentRect, _startPoint.X);
                Canvas.SetTop(_currentRect, _startPoint.Y);

                _canvas.Children.Add(_currentRect);

                _bboxMap[_currentRect] = new BoundingBox
                {
                    ClassName = _defaultClassName
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


        public BoundingBox? EndDraw(double imageWidth, double imageHeight)
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
        // 선택 / 이동 / 크기 조정
        // ========================
        public BoundingBox? Select(Rectangle rect, Point mousePoint)
        {
            if (!_bboxMap.ContainsKey(rect))
                return null;

            if (rect.Width <= 1 || rect.Height <= 1)
                return null;

            RestoreSelectedColor();

            _selectedRect = rect;
            _selectedRect.Stroke = Brushes.LimeGreen;

            _isDragging = true;
            _dragMoved = false;
            _dragStartPoint = mousePoint;
            _dragStartLeft = Canvas.GetLeft(_selectedRect);
            _dragStartTop = Canvas.GetTop(_selectedRect);
            _dragStartWidth = _selectedRect.Width;
            _dragStartHeight = _selectedRect.Height;
            _dragMode = GetPointerDragMode(_selectedRect, mousePoint);
            _selectedRect.CaptureMouse();
            _canvas.Cursor = GetCursor(_dragMode);

            SelectedBBox = _bboxMap[_selectedRect];

            return _bboxMap[rect];
        }

        public void Drag(Point currentPoint)
        {
            if (!_isDragging || _selectedRect == null)
                return;

            double dx = currentPoint.X - _dragStartPoint.X;
            double dy = currentPoint.Y - _dragStartPoint.Y;

            if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                _dragMoved = true;

            double canvasWidth = Math.Max(GetCanvasWidth(), _dragStartLeft + _dragStartWidth);
            double canvasHeight = Math.Max(GetCanvasHeight(), _dragStartTop + _dragStartHeight);
            double left = _dragStartLeft;
            double top = _dragStartTop;
            double right = _dragStartLeft + _dragStartWidth;
            double bottom = _dragStartTop + _dragStartHeight;

            if (_dragMode == PointerDragMode.Move)
            {
                left = Math.Clamp(
                    _dragStartLeft + dx,
                    0,
                    Math.Max(0, canvasWidth - _dragStartWidth));
                top = Math.Clamp(
                    _dragStartTop + dy,
                    0,
                    Math.Max(0, canvasHeight - _dragStartHeight));
                right = left + _dragStartWidth;
                bottom = top + _dragStartHeight;
            }
            else
            {
                if (ResizesLeft(_dragMode))
                    left = Math.Clamp(_dragStartLeft + dx, 0, right - MinimumBoxSize);
                if (ResizesRight(_dragMode))
                    right = Math.Clamp(right + dx, left + MinimumBoxSize, canvasWidth);
                if (ResizesTop(_dragMode))
                    top = Math.Clamp(_dragStartTop + dy, 0, bottom - MinimumBoxSize);
                if (ResizesBottom(_dragMode))
                    bottom = Math.Clamp(bottom + dy, top + MinimumBoxSize, canvasHeight);
            }

            Canvas.SetLeft(_selectedRect, left);
            Canvas.SetTop(_selectedRect, top);
            _selectedRect.Width = right - left;
            _selectedRect.Height = bottom - top;
        }

        public bool EndDrag(double imageWidth, double imageHeight)
        {
            if (_selectedRect == null)
                return false;

            _isDragging = false;
            _selectedRect.ReleaseMouseCapture();
            _dragMode = PointerDragMode.None;
            _canvas.Cursor = Cursors.Arrow;

            if (!_dragMoved)
                return false;

            UpdateBBoxModel(_selectedRect, imageWidth, imageHeight);
            return true;
        }

        public void UpdateHoverCursor(Rectangle? rect, Point mousePoint)
        {
            if (_isDragging)
                return;

            _canvas.Cursor = rect != null && _bboxMap.ContainsKey(rect)
                ? GetCursor(GetPointerDragMode(rect, mousePoint))
                : Cursors.Cross;
        }

        // ========================
        // 삭제
        // ========================
        public BoundingBox? DeleteSelected()
        {
            if (_selectedRect == null)
                return null;

            var bbox = _bboxMap[_selectedRect];
            RemoveRect(_selectedRect);
            _selectedRect = null;
            SelectedBBox = null;
            _dragMode = PointerDragMode.None;
            _canvas.Cursor = Cursors.Cross;

            return bbox;
        }

        // ========================
        // 유틸
        // ========================
        private PointerDragMode GetPointerDragMode(Rectangle rect, Point mousePoint)
        {
            double left = Canvas.GetLeft(rect);
            double top = Canvas.GetTop(rect);
            double right = left + rect.Width;
            double bottom = top + rect.Height;

            double leftDistance = Math.Abs(mousePoint.X - left);
            double rightDistance = Math.Abs(mousePoint.X - right);
            double topDistance = Math.Abs(mousePoint.Y - top);
            double bottomDistance = Math.Abs(mousePoint.Y - bottom);

            double horizontalTolerance = Math.Min(ResizeHitThickness, rect.Width / 3.0);
            double verticalTolerance = Math.Min(ResizeHitThickness, rect.Height / 3.0);
            bool nearHorizontalEdge = Math.Min(leftDistance, rightDistance) <= horizontalTolerance;
            bool nearVerticalEdge = Math.Min(topDistance, bottomDistance) <= verticalTolerance;
            bool useLeft = leftDistance <= rightDistance;
            bool useTop = topDistance <= bottomDistance;

            if (nearHorizontalEdge && nearVerticalEdge)
            {
                if (useLeft && useTop) return PointerDragMode.TopLeft;
                if (!useLeft && useTop) return PointerDragMode.TopRight;
                if (useLeft) return PointerDragMode.BottomLeft;
                return PointerDragMode.BottomRight;
            }

            if (nearHorizontalEdge)
                return useLeft ? PointerDragMode.Left : PointerDragMode.Right;
            if (nearVerticalEdge)
                return useTop ? PointerDragMode.Top : PointerDragMode.Bottom;
            return PointerDragMode.Move;
        }

        private static Cursor GetCursor(PointerDragMode mode) => mode switch
        {
            PointerDragMode.Left or PointerDragMode.Right => Cursors.SizeWE,
            PointerDragMode.Top or PointerDragMode.Bottom => Cursors.SizeNS,
            PointerDragMode.TopLeft or PointerDragMode.BottomRight => Cursors.SizeNWSE,
            PointerDragMode.TopRight or PointerDragMode.BottomLeft => Cursors.SizeNESW,
            PointerDragMode.Move => Cursors.SizeAll,
            _ => Cursors.Arrow
        };

        private static bool ResizesLeft(PointerDragMode mode)
            => mode is PointerDragMode.Left or PointerDragMode.TopLeft or PointerDragMode.BottomLeft;

        private static bool ResizesRight(PointerDragMode mode)
            => mode is PointerDragMode.Right or PointerDragMode.TopRight or PointerDragMode.BottomRight;

        private static bool ResizesTop(PointerDragMode mode)
            => mode is PointerDragMode.Top or PointerDragMode.TopLeft or PointerDragMode.TopRight;

        private static bool ResizesBottom(PointerDragMode mode)
            => mode is PointerDragMode.Bottom or PointerDragMode.BottomLeft or PointerDragMode.BottomRight;

        private double GetCanvasWidth()
        {
            double width = _canvas.Width;
            return double.IsNaN(width) || width <= 0 ? _canvas.ActualWidth : width;
        }

        private double GetCanvasHeight()
        {
            double height = _canvas.Height;
            return double.IsNaN(height) || height <= 0 ? _canvas.ActualHeight : height;
        }

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
            _selectedRect.Stroke = GetStrokeBrush(bbox.ClassName);
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
                SelectedBBox = null;
                _isDragging = false;
                _dragMode = PointerDragMode.None;
                _canvas.Cursor = Cursors.Cross;
            }
        }

        public BoundingBox? SelectedBBox { get; private set; }

        public string DefaultClassName
        {
            get => _defaultClassName;
            set => _defaultClassName = NormalizeClassName(value);
        }

        public void SetSelectedClass(string className)
        {
            if (_selectedRect == null)
                return;

            string normalizedClassName = NormalizeClassName(className);
            var bbox = _bboxMap[_selectedRect];
            bbox.ClassName = normalizedClassName;

            _selectedRect.Stroke = GetStrokeBrush(normalizedClassName);

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
                StrokeThickness = BoxStrokeThickness,
                Stroke = GetStrokeBrush(bbox.ClassName),
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
                _isDragging = false;
                _dragMode = PointerDragMode.None;
                _canvas.Cursor = Cursors.Cross;
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

        private static string NormalizeClassName(string? className)
        {
            string normalized = (className ?? "").Trim().ToLowerInvariant();
            return normalized == "loose" ? "loose" : "dent";
        }

        private static Brush GetStrokeBrush(string? className)
        {
            return NormalizeClassName(className) == "dent"
                ? Brushes.Red
                : Brushes.Blue;
        }




    }
}
