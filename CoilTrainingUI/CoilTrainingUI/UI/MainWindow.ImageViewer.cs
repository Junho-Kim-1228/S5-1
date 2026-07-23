using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services.Review;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void LoadImage(string imagePath, bool fitToView = false)
        {
            _ = LoadImageAsync(imagePath, fitToView);
        }

        private async Task LoadImageAsync(string imagePath, bool fitToView)
        {
            CancelAndDispose(ref _imageLoadCancellation);
            CancelAndDispose(ref _imagePrefetchCancellation);
            CancelAndDispose(ref _rawViewLoadCancellation);
            _rawViewBitmap = null;
            _rawViewBitmapPath = null;

            var cancellation = new CancellationTokenSource();
            _imageLoadCancellation = cancellation;
            CancellationToken cancellationToken = cancellation.Token;
            long requestId = ++_imageLoadRequestId;

            _isLoadingImage = true;
            _currentImagePath = null;
            ClassComboBox.IsEnabled = false;
            ImageCanvas.IsHitTestVisible = false;
            MainImage.Source = null;
            _bboxManager.ClearAll();

            try
            {
                Task<BitmapSource> bitmapTask =
                    _imageBitmapCache.LoadCachedAsync(imagePath, cancellationToken);
                Task<ReviewStateLoadResult> reviewTask = Task.Run(
                    () => _reviewRepository.Load(imagePath),
                    cancellationToken);

                await Task.WhenAll(bitmapTask, reviewTask);
                cancellationToken.ThrowIfCancellationRequested();

                if (requestId != _imageLoadRequestId ||
                    ImageListBox.SelectedItem is not ImageItem selectedItem ||
                    !string.Equals(
                        selectedItem.ProcessedPath,
                        imagePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                BitmapSource bitmap = await bitmapTask;
                ReviewStateLoadResult review = await reviewTask;

                _currentImagePath = imagePath;
                ClassComboBox.IsEnabled = true;
                SetClassComboBoxSelection(_activeDrawClass);

                _imageStateManager.EnsureImage(imagePath);

                _rawBitmap = bitmap;
                MainImage.Source = bitmap;

                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;

                _bboxManager.ClearAll();
                _imageStateManager.ClearLabels(imagePath);

                _currentReviewState = review.State.Clone();

                if (_currentReviewState.Boxes.Count > 0)
                {
                    var mutable = _imageStateManager.GetMutableLabels(imagePath);

                    foreach (var label in _currentReviewState.Boxes)
                    {
                        mutable.Add(new BoundingBox
                        {
                            X = label.X,
                            Y = label.Y,
                            Width = label.Width,
                            Height = label.Height,
                            ClassName = label.ClassName
                        });
                    }
                }

                foreach (var bbox in _imageStateManager.GetLabels(imagePath))
                    _bboxManager.AddFromModel(bbox, ImageCanvas.Width, ImageCanvas.Height);

                UpdatePredictionOverlayVisibility(imagePath);

                bool isNormal = _currentReviewState.Decision == ImageReviewDecision.ConfirmedNormal;
                _imageStateManager.SetNormal(imagePath, isNormal);

                UpdateGtSummaryForImageItem(selectedItem, imagePath, review);

                NormalRadio.IsChecked = _currentReviewState.Decision == ImageReviewDecision.ConfirmedNormal;
                AbnormalRadio.IsChecked = _currentReviewState.Decision == ImageReviewDecision.ConfirmedDefect;
                YoloBackgroundCheckBox.IsChecked = _currentReviewState.UseAsYoloBackground;
                YoloBackgroundCheckBox.IsEnabled = _currentReviewState.Decision == ImageReviewDecision.ConfirmedNormal;

                UpdateMainImageDisplayFromToggle();
                ImageCanvas.IsHitTestVisible = true;

                if (fitToView)
                {
                    _canvasInteractionManager.FitToView(
                        ImageCanvas.Width,
                        ImageCanvas.Height);
                }

                ScheduleAdjacentImagePrefetch(selectedItem);
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        if (requestId == _imageLoadRequestId &&
                            string.Equals(_currentImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                        {
                            UpdatePredictionOverlayVisibility(imagePath);
                        }
                    }));
            }
            catch (OperationCanceledException)
            {
                // A newer selection superseded this load.
            }
            catch (Exception ex)
            {
                if (requestId == _imageLoadRequestId)
                {
                    Trace.WriteLine($"image load failed: {imagePath}: {ex}");
                    MessageBox.Show(
                        $"이미지를 불러오지 못했습니다.\n\n{imagePath}\n\n{ex.Message}",
                        "이미지 로드 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                if (requestId == _imageLoadRequestId)
                {
                    _isLoadingImage = false;
                    ImageCanvas.IsHitTestVisible = _currentImagePath != null;
                }
            }
        }

        private void UpdateMainImageDisplayFromToggle()
        {
            if (_rawBitmap == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            UpdateMainImageSourceFromViewToggle(showMissingRawMessage: false, fallbackSource: _rawBitmap);
        }

        private void UpdateMainImageSourceFromViewToggle(bool showMissingRawMessage, BitmapSource? fallbackSource = null)
        {
            BitmapSource? processedSource = fallbackSource ?? _rawBitmap;
            if (processedSource == null)
                return;

            if (ShowRawCheckBox.IsChecked != true)
            {
                CancelAndDispose(ref _rawViewLoadCancellation);
                MainImage.Source = processedSource;
                return;
            }

            if (ImageListBox.SelectedItem is not ImageItem currentItem)
            {
                MainImage.Source = processedSource;
                return;
            }

            if (string.IsNullOrWhiteSpace(currentItem.RawPath) || !File.Exists(currentItem.RawPath))
            {
                if (showMissingRawMessage)
                {
                    MessageBox.Show(
                        "RAW 이미지가 배치에 없습니다.",
                        "Show RAW",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }

                _suppressRawToggleEvent = true;
                ShowRawCheckBox.IsChecked = false;
                _suppressRawToggleEvent = false;
                MainImage.Source = processedSource;
                return;
            }

            if (!string.Equals(_rawViewBitmapPath, currentItem.RawPath, StringComparison.OrdinalIgnoreCase) ||
                _rawViewBitmap == null)
            {
                MainImage.Source = processedSource;
                StartRawViewLoad(
                    currentItem.ProcessedPath,
                    currentItem.RawPath,
                    showMissingRawMessage);
                return;
            }

            MainImage.Source = _rawViewBitmap;
        }

        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePredictionFeatureUiState();

            if (ImageListBox.SelectedItem is ImageItem item)
                LoadImage(item.ProcessedPath, fitToView: true);
            else
                ResetImageDisplay();
        }

        private void StartRawViewLoad(
            string processedPath,
            string rawPath,
            bool showFailureMessage)
        {
            CancelAndDispose(ref _rawViewLoadCancellation);
            var cancellation = new CancellationTokenSource();
            _rawViewLoadCancellation = cancellation;
            long requestId = _imageLoadRequestId;

            _ = LoadRawViewAsync(
                processedPath,
                rawPath,
                requestId,
                showFailureMessage,
                cancellation.Token);
        }

        private async Task LoadRawViewAsync(
            string processedPath,
            string rawPath,
            long requestId,
            bool showFailureMessage,
            CancellationToken cancellationToken)
        {
            try
            {
                BitmapSource rawBitmap = await _imageBitmapCache.LoadUncachedAsync(
                    rawPath,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (requestId != _imageLoadRequestId ||
                    ShowRawCheckBox.IsChecked != true ||
                    !string.Equals(_currentImagePath, processedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _rawViewBitmap = rawBitmap;
                _rawViewBitmapPath = rawPath;
                MainImage.Source = rawBitmap;
            }
            catch (OperationCanceledException)
            {
                // The selected image or display mode changed.
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"raw image load failed: {rawPath}: {ex}");
                if (showFailureMessage &&
                    requestId == _imageLoadRequestId &&
                    ShowRawCheckBox.IsChecked == true)
                {
                    MessageBox.Show(
                        $"RAW 이미지를 불러오지 못했습니다.\n\n{rawPath}\n\n{ex.Message}",
                        "Show RAW",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void ScheduleAdjacentImagePrefetch(ImageItem selectedItem)
        {
            CancelAndDispose(ref _imagePrefetchCancellation);

            List<ImageItem> visibleItems = (_imageCollectionView?.Cast<object>() ?? _images.Cast<object>())
                .OfType<ImageItem>()
                .ToList();
            int selectedIndex = visibleItems.FindIndex(item =>
                string.Equals(
                    item.ProcessedPath,
                    selectedItem.ProcessedPath,
                    StringComparison.OrdinalIgnoreCase));
            if (selectedIndex < 0)
                return;

            var paths = new List<string>(capacity: 2);
            if (selectedIndex > 0)
                paths.Add(visibleItems[selectedIndex - 1].ProcessedPath);
            if (selectedIndex + 1 < visibleItems.Count)
                paths.Add(visibleItems[selectedIndex + 1].ProcessedPath);
            if (paths.Count == 0)
                return;

            var cancellation = new CancellationTokenSource();
            _imagePrefetchCancellation = cancellation;
            _ = PrefetchImagesAsync(paths, cancellation.Token);
        }

        private async Task PrefetchImagesAsync(
            IReadOnlyList<string> imagePaths,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(500, cancellationToken);
                foreach (string imagePath in imagePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _imageBitmapCache.LoadCachedAsync(imagePath, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // User navigation always has priority over speculative loading.
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"image prefetch failed: {ex}");
            }
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cancellation)
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        private void ImageListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject source)
                return;

            var scrollViewer = FindVisualChild<ScrollViewer>(source);
            if (scrollViewer == null)
                return;

            _imageListWheelDeltaAccumulator += e.Delta;

            while (_imageListWheelDeltaAccumulator >= ImageListWheelDeltaStep)
            {
                scrollViewer.LineUp();
                _imageListWheelDeltaAccumulator -= ImageListWheelDeltaStep;
            }

            while (_imageListWheelDeltaAccumulator <= -ImageListWheelDeltaStep)
            {
                scrollViewer.LineDown();
                _imageListWheelDeltaAccumulator += ImageListWheelDeltaStep;
            }

            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T target)
                    return target;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        private void ShowRawCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressRawToggleEvent)
                return;

            UpdateMainImageSourceFromViewToggle(showMissingRawMessage: true);
        }

        private void ShowRawCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressRawToggleEvent)
                return;

            UpdateMainImageSourceFromViewToggle(showMissingRawMessage: false);
        }

        private void ResetImageDisplay()
        {
            CancelAndDispose(ref _imageLoadCancellation);
            CancelAndDispose(ref _imagePrefetchCancellation);
            CancelAndDispose(ref _rawViewLoadCancellation);
            _imageLoadRequestId++;
            _isLoadingImage = false;
            _currentImagePath = null;
            _rawBitmap = null;
            _rawViewBitmap = null;
            _rawViewBitmapPath = null;
            MainImage.Source = null;
            _bboxManager.ClearAll();
            ImageCanvas.IsHitTestVisible = false;
            ClassComboBox.IsEnabled = false;
            SetClassComboBoxSelection(_activeDrawClass);
            UpdatePredictionFeatureUiState();
        }
    }
}
