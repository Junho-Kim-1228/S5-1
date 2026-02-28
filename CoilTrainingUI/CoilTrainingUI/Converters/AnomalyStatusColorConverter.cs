using CoilTrainingUI.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CoilTrainingUI.Converters
{
    public class AnomalyStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isNormal)
                return isNormal ? Brushes.Green : Brushes.IndianRed;

            if (value is ImageItem item)
                return item.IsNormal ? Brushes.Green : Brushes.IndianRed;

            return Brushes.DimGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
