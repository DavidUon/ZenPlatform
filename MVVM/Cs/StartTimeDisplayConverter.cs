using System;
using System.Globalization;
using System.Windows.Data;

namespace ZenPlatform.MVVM.Cs
{
    public sealed class StartTimeDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
            {
                return string.Empty;
            }

            if (values[0] is not DateTime time || values[1] is not int startPosition)
            {
                return string.Empty;
            }

            var suffix = startPosition > 0 ? " (多)" : startPosition < 0 ? " (空)" : string.Empty;
            return $"{time:MM/dd HH:mm:ss}{suffix}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
