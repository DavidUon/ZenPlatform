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
        public Color FillColor { get; private set; } = Color.FromRgb(100,160,255);
        public double FillOpacity { get; private set; } = 0.2; // 0..1

        public BollSettingsDialog(int period, double k)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period; K = k;
            PeriodBox.Text = period.ToString();
            KBox.Text = k.ToString(CultureInfo.InvariantCulture);
            // 預設顏色、透明度
            SelectColor(Color.FromRgb(0x64,0xA0,0xFF));
            FillBox.Text = "#64A0FF";
            OpacitySlider.Value = 20;
            OpacitySlider.ValueChanged += (s,e)=> { OpacityText.Text = ((int)OpacitySlider.Value).ToString()+"%"; };
        }

        public BollSettingsDialog(int period, double k, Color fill, double opacity)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            Period = period; K = k;
            PeriodBox.Text = period.ToString();
            KBox.Text = k.ToString(CultureInfo.InvariantCulture);
            // 設定外觀為當前值
            SelectColor(fill);
            FillBox.Text = string.Format("#{0:X2}{1:X2}{2:X2}", fill.R, fill.G, fill.B);
            var percent = (int)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 100.0);
            OpacitySlider.Value = percent;
            OpacityText.Text = percent + "%";
            OpacitySlider.ValueChanged += (s,e)=> { OpacityText.Text = ((int)OpacitySlider.Value).ToString()+"%"; };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0) { MessageBox.Show("期間(N) 必須為正整數"); return; }
            if (!double.TryParse(KBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var k) || k <= 0) { MessageBox.Show("K 必須為正數"); return; }
            Period = p; K = k; FillColor = _selColor; FillOpacity = OpacitySlider.Value / 100.0;
            DialogResult = true;
        }

        private Color _selColor;
        private void SelectColor(Color c)
        {
            _selColor = c; // 顏色高亮（可加邊框效果，簡化略過）
            FillBox.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b)
            {
                try
                {
                    var col = (Color)ColorConverter.ConvertFromString(b.Tag?.ToString() ?? "#64A0FF");
                    SelectColor(col);
                }
                catch { }
            }
        }
    }
}
