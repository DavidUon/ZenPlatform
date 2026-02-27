using System;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class AtrSettingsDialog : Window
    {
        public int Period { get; private set; }
        public Color LineColor { get; private set; } = Color.FromRgb(0x6A, 0x9E, 0xFF);

        public AtrSettingsDialog(int period, Color lineColor)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period;
            PeriodBox.Text = period.ToString();
            ApplyLineColor(lineColor);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0)
            {
                MessageBox.Show("Period 必須為正整數");
                return;
            }
            Period = p;
            DialogResult = true;
        }

        private void ApplyLineColor(Color c)
        {
            LineColor = c;
            LineSwatch.Background = new SolidColorBrush(c);
        }

        private void PickLineColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(LineColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyLineColor(dlg.SelectedColor);
        }
    }
}
