using CoilTrainingUI.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CoilTrainingUI.Converters
{
    public class YoloStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasLabel)
                return hasLabel ? Brushes.IndianRed : Brushes.Green;

            if (value is ImageItem item)
                return item.HasLabel ? Brushes.IndianRed : Brushes.Green;

            return Brushes.DimGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
