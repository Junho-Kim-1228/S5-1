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

            if (item.IsReviewExcluded)
                return new SolidColorBrush(Color.FromRgb(225, 225, 225));
            if (item.IsAutoReviewAudit)
                return new SolidColorBrush(Color.FromRgb(232, 221, 255));
            if (item.IsReviewConfirmedDefect && !item.IsBoxReviewConfirmed)
                return new SolidColorBrush(Color.FromRgb(255, 226, 179));
            if (item.IsReviewConfirmedDefect)
                return new SolidColorBrush(Color.FromRgb(255, 220, 220));
            if (item.IsReviewConfirmedNormal)
                return new SolidColorBrush(Color.FromRgb(220, 255, 225));
            if (item.IsReviewing)
                return new SolidColorBrush(Color.FromRgb(220, 240, 255));

            return new SolidColorBrush(Color.FromRgb(255, 247, 220));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
