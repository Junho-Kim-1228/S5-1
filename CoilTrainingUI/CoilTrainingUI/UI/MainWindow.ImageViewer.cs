using CoilTrainingUI.Models;
using System;
using System.IO;
using System.Linq;
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
        private void LoadImage(string imagePath)
        {
            _isLoadingImage = true;
            try
            {
                _currentImagePath = imagePath;
                ClassComboBox.IsEnabled = true;
                SetClassComboBoxSelection(_activeDrawClass);

                _imageStateManager.EnsureImage(imagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                _rawBitmap = bitmap;
                MainImage.Source = bitmap;

                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;

                _bboxManager.ClearAll();
                _imageStateManager.ClearLabels(imagePath);

                var state = _stateService.Load(imagePath);

                if (state.Labels.Count > 0)
                {
                    var mutable = _imageStateManager.GetMutableLabels(imagePath);

                    foreach (var label in state.Labels)
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
                else
                {
                    _yoloService.Load(imagePath, _imageStateManager.GetMutableLabels(imagePath));
                }

                foreach (var bbox in _imageStateManager.GetLabels(imagePath))
                    _bboxManager.AddFromModel(bbox, ImageCanvas.Width, ImageCanvas.Height);

                UpdatePredictionOverlayVisibility(imagePath);

                bool isNormal = (state.HasManualAnomalyDecision && state.IsNormal.HasValue)
                    ? state.IsNormal.Value
                    : true;
                _imageStateManager.SetNormal(imagePath, isNormal);

                if (ImageListBox.SelectedItem is ImageItem item)
                {
                    item.IsNormal = isNormal;
                    UpdateGtSummaryForImageItem(item, imagePath);

                    bool hasConfirmedImageDecision = state.HasManualAnomalyDecision && state.IsNormal.HasValue;
                    NormalRadio.IsChecked = hasConfirmedImageDecision ? isNormal : false;
                    AbnormalRadio.IsChecked = hasConfirmedImageDecision ? !isNormal : false;
                }

                UpdateMainImageDisplayFromToggle();
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        if (string.Equals(_currentImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                            UpdatePredictionOverlayVisibility(imagePath);
                    }));
            }
            finally
            {
                _isLoadingImage = false;
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
                var rawBitmap = new BitmapImage();
                rawBitmap.BeginInit();
                rawBitmap.UriSource = new Uri(currentItem.RawPath, UriKind.Absolute);
                rawBitmap.CacheOption = BitmapCacheOption.OnLoad;
                rawBitmap.EndInit();

                _rawViewBitmap = rawBitmap;
                _rawViewBitmapPath = currentItem.RawPath;
            }

            MainImage.Source = _rawViewBitmap;
        }

        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePredictionFeatureUiState();

            if (ImageListBox.SelectedItem is ImageItem item)
            {
                LoadImage(item.ProcessedPath);

                NormalRadio.IsChecked = item.IsNormal;
                AbnormalRadio.IsChecked = !item.IsNormal;

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            }
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
            _currentImagePath = null;
            MainImage.Source = null;
            _bboxManager.ClearAll();
            ClassComboBox.IsEnabled = false;
            SetClassComboBoxSelection(_activeDrawClass);
            UpdatePredictionFeatureUiState();
        }
    }
}
