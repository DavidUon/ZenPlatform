using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZenPlatform.MVVM.Cs
{
    public sealed class PositionBrushConverter : IValueConverter
    {
        public Brush? LongBrush { get; set; }
        public Brush? ShortBrush { get; set; }
        public Brush? NeutralBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int position)
            {
                return NeutralBrush ?? Brushes.Transparent;
            }

            if (position > 0)
            {
                return LongBrush ?? Brushes.Transparent;
            }

            if (position < 0)
            {
                return ShortBrush ?? Brushes.Transparent;
            }

            return NeutralBrush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
