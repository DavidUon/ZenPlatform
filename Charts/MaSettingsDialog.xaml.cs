using System;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class MaSettingsDialog : Window
    {
        public int Period { get; private set; }
        public string MaType { get; private set; } = "SMA";
        public Color LineColor { get; private set; } = Color.FromRgb(0xFF, 0xD7, 0x00);
        public bool IsAdd { get; private set; } = true; // 新增 or 刪除

        public MaSettingsDialog(int period, string type)
        {
            InitializeComponent();
            Period = period; MaType = type?.ToUpperInvariant() == "EMA" ? "EMA" : "SMA";
            PeriodBox.Text = Period.ToString();
            if (MaType == "EMA") TypeBox.SelectedIndex = 1; else TypeBox.SelectedIndex = 0;
            ColorBox.Text = "#FFD700";
            RbAdd.Checked += (_, __) => UpdateModeUI();
            RbRemove.Checked += (_, __) => UpdateModeUI();
            UpdateModeUI();
        }

        private void UpdateModeUI()
        {
            bool add = RbAdd.IsChecked == true;
            TypeBox.IsEnabled = add;
            ColorPanel.IsEnabled = add;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0) { MessageBox.Show("期間必須為正整數"); return; }
            Period = p;
            IsAdd = RbAdd.IsChecked == true;
            MaType = ((TypeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "SMA");
            DialogResult = true;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b)
            {
                try
                {
                    var col = (Color)ColorConverter.ConvertFromString(b.Tag?.ToString() ?? "#FFD700");
                    LineColor = col;
                    ColorBox.Text = string.Format("#{0:X2}{1:X2}{2:X2}", col.R, col.G, col.B);
                }
                catch { }
            }
        }
    }
}
