using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class BbiSettingsDialog : Window
    {
        public int[] Periods { get; private set; } = new[] { 3, 6, 12, 24 };
        public Color LineColor { get; private set; } = Color.FromRgb(0xE9, 0x1E, 0x1F);

        public BbiSettingsDialog(int[] periods, Color color)
        {
            InitializeComponent();
            var p = (periods == null || periods.Length == 0) ? new[] { 3, 6, 12, 24 } : periods;
            Periods = p;
            LineColor = color;

            var norm = p.Select(x => Math.Max(1, x)).ToArray();
            if (norm.Length < 4) norm = norm.Concat(Enumerable.Repeat(norm.LastOrDefault(3), 4 - norm.Length)).ToArray();
            P1Box.Text = norm[0].ToString();
            P2Box.Text = norm[1].ToString();
            P3Box.Text = norm[2].ToString();
            P4Box.Text = norm[3].ToString();
            ApplyColor(LineColor);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParsePeriod(P1Box.Text, out int p1) ||
                !TryParsePeriod(P2Box.Text, out int p2) ||
                !TryParsePeriod(P3Box.Text, out int p3) ||
                !TryParsePeriod(P4Box.Text, out int p4))
            {
                MessageBox.Show("期間必須為正整數");
                return;
            }

            Periods = new[] { p1, p2, p3, p4 };
            DialogResult = true;
        }

        private static bool TryParsePeriod(string text, out int value)
        {
            return int.TryParse(text, out value) && value > 0;
        }

        private void ApplyColor(Color col)
        {
            LineColor = col;
            LineSwatch.Background = new SolidColorBrush(col);
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(LineColor) { Owner = this };
            if (dlg.ShowDialog() == true)
                ApplyColor(dlg.SelectedColor);
        }
    }
}
