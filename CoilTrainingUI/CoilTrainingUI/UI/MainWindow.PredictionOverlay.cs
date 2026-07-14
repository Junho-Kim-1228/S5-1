using CoilTrainingUI.Models;
using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void ShowPredictionCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePredictionOverlayVisibility();
        }

        private void ShowPredictionCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdatePredictionOverlayVisibility();
        }

        private void UpdatePredictionFeatureUiState()
        {
            bool predictionAvailableInBatch = _currentBatchHasAnyInfer;
            bool predictionWasEnabled = ShowPredictionCheckBox.IsEnabled;

            ShowPredictionCheckBox.IsEnabled = predictionAvailableInBatch;
            if (!predictionAvailableInBatch)
                ShowPredictionCheckBox.IsChecked = false;
            else if (!predictionWasEnabled)
                ShowPredictionCheckBox.IsChecked = true;

            PreLabelBatchMenuItem.IsEnabled = predictionAvailableInBatch;
            AutoApproveSafeMenuItem.IsEnabled = predictionAvailableInBatch;
            ShowReviewQueueMenuItem.IsEnabled = _images.Count > 0;

            bool canApplyCurrentImage =
                predictionAvailableInBatch &&
                ImageListBox.SelectedItem is ImageItem item &&
                item.HasAiInfer;

            ApplyPredictionsMenuItem.IsEnabled = canApplyCurrentImage;
        }

        private void RenderPredictionOverlays(string imagePath)
        {
            ClearPredictionOverlays();

            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            if (!_inferJsonByImagePath.TryGetValue(imagePath, out var inferJsonPath))
                return;

            if (!File.Exists(inferJsonPath))
                return;

            InferResultDto infer;
            try
            {
                infer = InferenceBatchSchemaParser.ParseInferResult(inferJsonPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Prediction overlay parse failed: {inferJsonPath}, {ex.Message}");
                return;
            }

            double canvasWidth = ImageCanvas.Width;
            double canvasHeight = ImageCanvas.Height;

            if (canvasWidth <= 1 || canvasHeight <= 1)
                return;

            foreach (var detection in infer.Yolo.Detections)
            {
                if (detection.BboxXywhNorm == null || detection.BboxXywhNorm.Length != 4)
                    continue;

                var cx = detection.BboxXywhNorm[0];
                var cy = detection.BboxXywhNorm[1];
                var bw = detection.BboxXywhNorm[2];
                var bh = detection.BboxXywhNorm[3];

                if (bw <= 0 || bh <= 0)
                    continue;

                if (double.IsNaN(cx) || double.IsNaN(cy) || double.IsNaN(bw) || double.IsNaN(bh))
                    continue;

                double left = (cx - bw / 2.0) * canvasWidth;
                double top = (cy - bh / 2.0) * canvasHeight;
                double width = bw * canvasWidth;
                double height = bh * canvasHeight;

                if (left < 0)
                {
                    width += left;
                    left = 0;
                }

                if (top < 0)
                {
                    height += top;
                    top = 0;
                }

                if (left + width > canvasWidth)
                    width = canvasWidth - left;

                if (top + height > canvasHeight)
                    height = canvasHeight - top;

                if (width <= 1 || height <= 1)
                    continue;

                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 153, 255)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 6, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(45, 0, 153, 255)),
                    IsHitTestVisible = false,
                    Tag = PredictionOverlayTag
                };

                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                Panel.SetZIndex(rect, 100);
                ImageCanvas.Children.Add(rect);
            }
        }

        private void UpdatePredictionOverlayVisibility(string? imagePath = null)
        {
            string? targetPath = imagePath ?? _currentImagePath;

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                ClearPredictionOverlays();
                return;
            }

            if (ShowPredictionCheckBox.IsChecked == true)
                RenderPredictionOverlays(targetPath);
            else
                ClearPredictionOverlays();
        }

        private void ClearPredictionOverlays()
        {
            var overlays = ImageCanvas.Children
                .OfType<Rectangle>()
                .Where(rect => Equals(rect.Tag, PredictionOverlayTag))
                .ToList();

            foreach (var overlay in overlays)
                ImageCanvas.Children.Remove(overlay);
        }
    }
}
