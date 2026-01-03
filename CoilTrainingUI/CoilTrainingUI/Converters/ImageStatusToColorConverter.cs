using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Converters
{
    public class ImageStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ImageItem item)
                return Brushes.White;

            bool isAbnormal =
                item.HasLabel        // YOLO 불량
                || !item.IsNormal;   // Anomaly 불량

            return isAbnormal
                ? new SolidColorBrush(Color.FromRgb(255, 220, 220)) // 연한 빨강
                : Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
