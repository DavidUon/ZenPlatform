using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class SarSettingsDialog : Window
    {
        public decimal Step { get; private set; }
        public decimal Max { get; private set; }
        public Color DotColor { get; private set; }

        public SarSettingsDialog(decimal step, decimal max, Color color)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Step = step;
            Max = max;
            DotColor = color;
            StepBox.Text = step.ToString(CultureInfo.InvariantCulture);
            MaxBox.Text = max.ToString(CultureInfo.InvariantCulture);
            ApplyColor(color);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(StepBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var step) || step <= 0)
            {
                MessageBox.Show("Step 必須為正數");
                return;
            }
            if (!decimal.TryParse(MaxBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var max) || max <= 0)
            {
                MessageBox.Show("Max 必須為正數");
                return;
            }
            if (max < step)
            {
                MessageBox.Show("Max 必須大於或等於 Step");
                return;
            }
            Step = step;
            Max = max;
            DialogResult = true;
        }

        private void ApplyColor(Color c)
        {
            DotColor = c;
            DotSwatch.Background = new SolidColorBrush(c);
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(DotColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyColor(dlg.SelectedColor);
        }
    }
}
