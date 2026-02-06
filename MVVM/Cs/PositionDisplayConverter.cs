using System;
using System.Globalization;
using System.Windows.Data;

namespace ZenPlatform.MVVM.Cs
{
    public sealed class PositionDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int position)
            {
                return "無";
            }

            if (position == 0)
            {
                return "無";
            }

            var abs = Math.Abs(position);
            return position > 0 ? $"多單 {abs} 口" : $"空單 {abs} 口";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
