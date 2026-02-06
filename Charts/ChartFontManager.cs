using System;
using System.Linq;
using System.Windows;

namespace Charts
{
    public enum ChartFontScale
    {
        Small,
        Medium,
        Large
    }

    public static class ChartFontManager
    {
        private const string DictionaryMarker = "ChartFontSizes.";
        private static readonly Uri MediumUri = new Uri("pack://application:,,,/Charts;component/Resources/ChartFontSizes.Medium.xaml", UriKind.Absolute);

        private static readonly Uri SmallUri = new Uri("pack://application:,,,/Charts;component/Resources/ChartFontSizes.Small.xaml", UriKind.Absolute);
        private static readonly Uri LargeUri = new Uri("pack://application:,,,/Charts;component/Resources/ChartFontSizes.Large.xaml", UriKind.Absolute);

        private static ResourceDictionary? _fallbackDictionary;
        private static ChartFontScale _currentScale = ChartFontScale.Medium;
        private static bool _initialized;

        public static ChartFontScale CurrentScale => _currentScale;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            ApplyScale(_currentScale);
        }

        public static void ApplyScale(ChartFontScale scale)
        {
            _currentScale = scale;
            var dict = LoadDictionary(scale);

            _fallbackDictionary = dict;

            var appResources = Application.Current?.Resources;
            if (appResources != null)
            {
                // Remove previous chart font dictionaries
                var existing = appResources.MergedDictionaries
                    .Where(d => d.Source != null && d.Source.OriginalString.Contains(DictionaryMarker, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var old in existing)
                {
                    appResources.MergedDictionaries.Remove(old);
                }
                appResources.MergedDictionaries.Add(dict);
            }

            _initialized = true;
        }

        private static ResourceDictionary LoadDictionary(ChartFontScale scale)
        {
            var source = scale switch
            {
                ChartFontScale.Small => SmallUri,
                ChartFontScale.Large => LargeUri,
                _ => MediumUri
            };
            return new ResourceDictionary { Source = source };
        }

        public static double GetFontSize(string key, double fallback)
        {
            if (!_initialized)
            {
                EnsureInitialized();
            }

            if (Application.Current != null && Application.Current.Resources.Contains(key))
            {
                if (Application.Current.Resources[key] is double valueFromApp)
                {
                    return valueFromApp;
                }
            }

            return _fallbackDictionary != null && _fallbackDictionary.Contains(key) && _fallbackDictionary[key] is double value
                ? value
                : fallback;
        }
    }
}
