using System;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class MacdSettingsDialog : Window
    {
        public int Ema1 { get; private set; }
        public int Ema2 { get; private set; }
        public int Day { get; private set; }
        public Color DifColor { get; private set; } = Color.FromRgb(0x64, 0xC8, 0xFF);
        public Color DeaColor { get; private set; } = Color.FromRgb(0xFF, 0xFF, 0x00);

        public MacdSettingsDialog(int ema1, int ema2, int day, Color difColor, Color deaColor)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Ema1 = ema1; Ema2 = ema2; Day = day;
            Ema1Box.Text = ema1.ToString();
            Ema2Box.Text = ema2.ToString();
            DayBox.Text = day.ToString();
            ApplyDifColor(difColor);
            ApplyDeaColor(deaColor);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(Ema1Box.Text, out var e1) || e1 <= 0) { MessageBox.Show("EMA1 必須為正整數"); return; }
            if (!int.TryParse(Ema2Box.Text, out var e2) || e2 <= e1) { MessageBox.Show("EMA2 必須大於 EMA1"); return; }
            if (!int.TryParse(DayBox.Text, out var d) || d <= 0) { MessageBox.Show("Day 必須為正整數"); return; }
            Ema1 = e1; Ema2 = e2; Day = d;
            DialogResult = true;
        }

        private void ApplyDifColor(Color c)
        {
            DifColor = c;
            DifSwatch.Background = new SolidColorBrush(c);
        }

        private void ApplyDeaColor(Color c)
        {
            DeaColor = c;
            DeaSwatch.Background = new SolidColorBrush(c);
        }

        private void PickDifColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(DifColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyDifColor(dlg.SelectedColor);
        }

        private void PickDeaColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(DeaColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyDeaColor(dlg.SelectedColor);
        }
    }
}
