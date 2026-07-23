using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Review;
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

            ShowPredictionCheckBox.IsEnabled = predictionAvailableInBatch;
            if (!predictionAvailableInBatch)
                ShowPredictionCheckBox.IsChecked = false;

            PreLabelBatchMenuItem.IsEnabled = predictionAvailableInBatch;
            ShowReviewQueueMenuItem.IsEnabled = _images.Count > 0;

            var selectedReviewItem = ImageListBox.SelectedItem as ImageItem;
            bool canApplyCurrentImage =
                predictionAvailableInBatch &&
                selectedReviewItem != null &&
                selectedReviewItem.HasAiInfer;

            bool canAcceptAiDecision =
                canApplyCurrentImage &&
                selectedReviewItem != null &&
                _predictionByImagePath.TryGetValue(selectedReviewItem.ProcessedPath, out var selectedPrediction) &&
                selectedPrediction.HasAnomaDecision;
            AcceptAiDecisionButton.IsEnabled = canAcceptAiDecision;
            bool canAcceptBoxes = canApplyCurrentImage &&
                                  selectedReviewItem != null &&
                                  (selectedReviewItem.AiAnomaDefect || selectedReviewItem.IsReviewConfirmedDefect);
            AcceptPredictionBoxesButton.IsEnabled = canAcceptBoxes;
            ConfirmBoxesButton.IsEnabled = ImageListBox.SelectedItem is ImageItem selectedItem &&
                                           selectedItem.IsReviewConfirmedDefect;
            ExcludeReviewButton.IsEnabled = ImageListBox.SelectedItem is ImageItem;
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

            PredictionSnapshot prediction = _predictionByImagePath.TryGetValue(imagePath, out var cached)
                ? cached
                : _predictionReader.Read(
                    inferJsonPath,
                    _expectedInferenceContextByImagePath.TryGetValue(imagePath, out var expectedContextId)
                        ? expectedContextId
                        : "");
            if (prediction.ParseFailed || !prediction.HasAnomaDecision || !prediction.AnomaIsDefect)
                return;

            double canvasWidth = ImageCanvas.Width;
            double canvasHeight = ImageCanvas.Height;

            if (canvasWidth <= 1 || canvasHeight <= 1)
                return;

            foreach (var detection in prediction.YoloBoxes)
            {
                var cx = detection.X;
                var cy = detection.Y;
                var bw = detection.Width;
                var bh = detection.Height;

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
