using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CoilTrainingUI.Converters
{
    public class NormalToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isNormal)
            {
                return isNormal ? Brushes.Transparent : Brushes.MistyRose;
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
