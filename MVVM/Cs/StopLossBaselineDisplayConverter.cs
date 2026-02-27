using System;
using System.Globalization;
using System.Windows.Data;
using ZenPlatform.Strategy;

namespace ZenPlatform.MVVM.Cs
{
    public sealed class StopLossBaselineDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return "---";
            }

            var mode = ParseMode(values[1]);
            if (mode != StopLossMode.Auto)
            {
                return "---";
            }

            if (values[0] is decimal baseline)
            {
                return baseline.ToString("F0", culture);
            }

            try
            {
                if (values[0] != null)
                {
                    var number = System.Convert.ToDecimal(values[0], culture);
                    return number.ToString("F0", culture);
                }
            }
            catch
            {
                // ignored
            }

            return "---";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static StopLossMode ParseMode(object value)
        {
            return value switch
            {
                StopLossMode mode => mode,
                int i when Enum.IsDefined(typeof(StopLossMode), i) => (StopLossMode)i,
                string s when Enum.TryParse<StopLossMode>(s, true, out var mode) => mode,
                _ => StopLossMode.FixedPoints
            };
        }
    }
}
