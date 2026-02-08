using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Charts
{
    public partial class ColorPickerDialog : Window
    {
        private bool _syncing;
        public Color SelectedColor { get; private set; } = Color.FromRgb(0xFF, 0xD7, 0x00);

        public ColorPickerDialog(Color initial)
        {
            InitializeComponent();
            SetColor(initial);
        }

        private void SetColor(Color c)
        {
            SelectedColor = c;
            UpdatePreview(c);
            _syncing = true;
            RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
            RBox.Text = c.R.ToString(); GBox.Text = c.G.ToString(); BBox.Text = c.B.ToString();
            _syncing = false;
        }

        private void UpdatePreview(Color c)
        {
            PreviewRect.Fill = new SolidColorBrush(c);
            HexText.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            var r = (byte)Math.Round(RSlider.Value);
            var g = (byte)Math.Round(GSlider.Value);
            var b = (byte)Math.Round(BSlider.Value);
            var c = Color.FromRgb(r, g, b);
            SelectedColor = c;
            UpdatePreview(c);
            _syncing = true;
            RBox.Text = r.ToString(); GBox.Text = g.ToString(); BBox.Text = b.ToString();
            _syncing = false;
        }

        private void Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            if (sender is not TextBox tb) return;

            int Clamp(string text)
            {
                return int.TryParse(text, out var v) ? Math.Max(0, Math.Min(255, v)) : 0;
            }

            var r = Clamp(RBox.Text);
            var g = Clamp(GBox.Text);
            var b = Clamp(BBox.Text);

            var c = Color.FromRgb((byte)r, (byte)g, (byte)b);
            SelectedColor = c;
            UpdatePreview(c);

            _syncing = true;
            RSlider.Value = r; GSlider.Value = g; BSlider.Value = b;
            RBox.Text = r.ToString(); GBox.Text = g.ToString(); BBox.Text = b.ToString();
            _syncing = false;
        }

        private void Palette_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b) return;
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(b.Tag?.ToString() ?? "#FFD700");
                SetColor(col);
            }
            catch { }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
