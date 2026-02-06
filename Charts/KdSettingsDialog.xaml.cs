using System;
using System.Windows;

namespace Charts
{
    public partial class KdSettingsDialog : Window
    {
        public int Period { get; private set; }
        public int SmoothK { get; private set; }
        public int SmoothD { get; private set; }

        public KdSettingsDialog(int period, int smoothK, int smoothD)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period; SmoothK = smoothK; SmoothD = smoothD;
            PeriodBox.Text = period.ToString();
            SmoothKBox.Text = smoothK.ToString();
            SmoothDBox.Text = smoothD.ToString();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0) { MessageBox.Show("RSV 必須為正整數"); return; }
            if (!int.TryParse(SmoothKBox.Text, out var sk) || sk <= 0) { MessageBox.Show("平滑K必須為正整數"); return; }
            if (!int.TryParse(SmoothDBox.Text, out var sd) || sd <= 0) { MessageBox.Show("平滑D必須為正整數"); return; }
            Period = p; SmoothK = sk; SmoothD = sd;
            DialogResult = true;
        }
    }
}
