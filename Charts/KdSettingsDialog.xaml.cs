using System;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class KdSettingsDialog : Window
    {
        public int Period { get; private set; }
        public int SmoothK { get; private set; }
        public int SmoothD { get; private set; }
        public Color KColor { get; private set; } = Color.FromRgb(0xFF, 0xFF, 0x00);
        public Color DColor { get; private set; } = Color.FromRgb(0x64, 0xC8, 0xFF);

        public KdSettingsDialog(int period, int smoothK, int smoothD, Color kColor, Color dColor)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period; SmoothK = smoothK; SmoothD = smoothD;
            PeriodBox.Text = period.ToString();
            SmoothKBox.Text = smoothK.ToString();
            SmoothDBox.Text = smoothD.ToString();
            ApplyKColor(kColor);
            ApplyDColor(dColor);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0) { MessageBox.Show("RSV 必須為正整數"); return; }
            if (!int.TryParse(SmoothKBox.Text, out var sk) || sk <= 0) { MessageBox.Show("平滑K必須為正整數"); return; }
            if (!int.TryParse(SmoothDBox.Text, out var sd) || sd <= 0) { MessageBox.Show("平滑D必須為正整數"); return; }
            Period = p; SmoothK = sk; SmoothD = sd;
            DialogResult = true;
        }

        private void ApplyKColor(Color c)
        {
            KColor = c;
            KSwatch.Background = new SolidColorBrush(c);
        }

        private void ApplyDColor(Color c)
        {
            DColor = c;
            DSwatch.Background = new SolidColorBrush(c);
        }

        private void PickKColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(KColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyKColor(dlg.SelectedColor);
        }

        private void PickDColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(DColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyDColor(dlg.SelectedColor);
        }
    }
}
