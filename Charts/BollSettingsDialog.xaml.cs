using System;
using System.Globalization;
using System.Windows.Media;
using System.Windows;

namespace Charts
{
    public partial class BollSettingsDialog : Window
    {
        public int Period { get; private set; }
        public double K { get; private set; }
        public Color FillColor { get; private set; } = Color.FromRgb(0xB7,0xB8,0xB7);
        public Color EdgeColor { get; private set; } = Color.FromRgb(0xB4,0xB4,0xB4);
        public Color MidColor { get; private set; } = Color.FromRgb(0xD7,0xD4,0xD5);
        public double FillOpacity { get; private set; } = 0.059597315436241624; // 0..1

        public BollSettingsDialog(int period, double k)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period; K = k;
            PeriodBox.Text = period.ToString();
            KBox.Text = k.ToString(CultureInfo.InvariantCulture);
            // 預設顏色、透明度
            SelectFillColor(Color.FromRgb(0xB7,0xB8,0xB7));
            SelectEdgeColor(Color.FromRgb(0xB4,0xB4,0xB4));
            SelectMidColor(Color.FromRgb(0xD7,0xD4,0xD5));
            OpacitySlider.Value = 5.9597;
            OpacitySlider.ValueChanged += (s,e)=> { OpacityText.Text = ((int)OpacitySlider.Value).ToString()+"%"; };
        }

        public BollSettingsDialog(int period, double k, Color fill, Color edge, Color mid, double opacity)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period; K = k;
            PeriodBox.Text = period.ToString();
            KBox.Text = k.ToString(CultureInfo.InvariantCulture);
            // 設定外觀為當前值
            SelectFillColor(fill);
            SelectEdgeColor(edge);
            SelectMidColor(mid);
            var percent = (int)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 100.0);
            OpacitySlider.Value = percent;
            OpacityText.Text = percent + "%";
            OpacitySlider.ValueChanged += (s,e)=> { OpacityText.Text = ((int)OpacitySlider.Value).ToString()+"%"; };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0) { MessageBox.Show("期間(N) 必須為正整數"); return; }
            if (!double.TryParse(KBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var k) || k <= 0) { MessageBox.Show("K 必須為正數"); return; }
            Period = p; K = k; FillColor = _fillColor; EdgeColor = _edgeColor; MidColor = _midColor; FillOpacity = OpacitySlider.Value / 100.0;
            DialogResult = true;
        }

        private Color _fillColor;
        private Color _edgeColor;
        private Color _midColor;
        private void SelectFillColor(Color c)
        {
            _fillColor = c;
            FillSwatch.Background = new SolidColorBrush(c);
        }

        private void PickFillColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(_fillColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                SelectFillColor(dlg.SelectedColor);
        }

        private void SelectEdgeColor(Color c)
        {
            _edgeColor = c;
            EdgeSwatch.Background = new SolidColorBrush(c);
        }

        private void SelectMidColor(Color c)
        {
            _midColor = c;
            MidSwatch.Background = new SolidColorBrush(c);
        }

        private void PickEdgeColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(_edgeColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                SelectEdgeColor(dlg.SelectedColor);
        }

        private void PickMidColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(_midColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                SelectMidColor(dlg.SelectedColor);
        }
    }
}
