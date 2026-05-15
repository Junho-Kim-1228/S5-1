using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CoilTrainingUI.Converters
{
    public class SimpleStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string text)
                return Brushes.LightGray;

            return text switch
            {
                "불량" => Brushes.IndianRed,
                "이상" => Brushes.IndianRed,
                "검출" => Brushes.IndianRed,
                "있음" => Brushes.IndianRed,
                "정상" => Brushes.SeaGreen,
                "없음" => Brushes.SeaGreen,
                "건너뜀" => Brushes.Gray,
                "미검출" => Brushes.Gray,
                _ => Brushes.Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
