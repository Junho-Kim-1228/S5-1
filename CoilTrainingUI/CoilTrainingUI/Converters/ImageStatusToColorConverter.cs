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

            // 확정 불량(수동/GT)은 빨간색 계열
            if (item.HasLabel || !item.IsNormal)
                return new SolidColorBrush(Color.FromRgb(255, 220, 220));

            // AI 예측 불량은 파란색 계열 (확정 불량과 구분)
            if (item.HasAiInfer && item.AiIsDefect)
                return new SolidColorBrush(Color.FromRgb(220, 235, 255));

            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
