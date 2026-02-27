using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZenPlatform.MVVM.Cs
{
    public sealed class PositionBrushWithStatusConverter : IMultiValueConverter
    {
        public Brush? LongBrush { get; set; }
        public Brush? ShortBrush { get; set; }
        public Brush? NeutralBrush { get; set; }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 || values[0] is not int position || values[1] is not bool isFinished || values[2] is not bool isStrategyRunning)
            {
                return NeutralBrush ?? Brushes.Transparent;
            }

            if (isFinished || !isStrategyRunning)
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

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
