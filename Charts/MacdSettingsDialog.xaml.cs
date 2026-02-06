using System;
using System.Windows;

namespace Charts
{
    public partial class MacdSettingsDialog : Window
    {
        public int Ema1 { get; private set; }
        public int Ema2 { get; private set; }
        public int Day { get; private set; }

        public MacdSettingsDialog(int ema1, int ema2, int day)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Ema1 = ema1; Ema2 = ema2; Day = day;
            Ema1Box.Text = ema1.ToString();
            Ema2Box.Text = ema2.ToString();
            DayBox.Text = day.ToString();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(Ema1Box.Text, out var e1) || e1 <= 0) { MessageBox.Show("EMA1 必須為正整數"); return; }
            if (!int.TryParse(Ema2Box.Text, out var e2) || e2 <= e1) { MessageBox.Show("EMA2 必須大於 EMA1"); return; }
            if (!int.TryParse(DayBox.Text, out var d) || d <= 0) { MessageBox.Show("Day 必須為正整數"); return; }
            Ema1 = e1; Ema2 = e2; Day = d;
            DialogResult = true;
        }
    }
}
