using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class MaSettingsDialog : Window
    {
        public class MaLine : INotifyPropertyChanged
        {
            private string _maType = "MA";
            private int _period = 20;
            private Color _color = Color.FromRgb(0xFF, 0xD7, 0x00);
            private SolidColorBrush _brush;

            public MaLine()
            {
                _brush = new SolidColorBrush(_color);
            }

            public string MaType
            {
                get => _maType;
                set { _maType = (value == "EMA") ? "EMA" : "MA"; OnPropertyChanged(); }
            }

            public int Period
            {
                get => _period;
                set { _period = value; OnPropertyChanged(); }
            }

            public Color Color
            {
                get => _color;
                set
                {
                    _color = value;
                    _brush = new SolidColorBrush(_color);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }

            public SolidColorBrush ColorBrush => _brush;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public IReadOnlyList<string> MaTypes { get; } = new[] { "MA", "EMA" };
        public ObservableCollection<MaLine> Lines { get; } = new();

        public IReadOnlyList<MaLine> ResultLines { get; private set; } = Array.Empty<MaLine>();

        public MaSettingsDialog(IEnumerable<(int period, string maType, Color color)>? lines)
        {
            InitializeComponent();
            DataContext = this;

            var items = lines?.ToList() ?? new List<(int, string, Color)>();
            if (items.Count == 0)
            {
                items.Add((20, "MA", Color.FromRgb(0xFF, 0xD7, 0x00)));
            }

            foreach (var (p, t, c) in items)
            {
                Lines.Add(new MaLine { Period = Math.Max(1, p), MaType = (t == "EMA") ? "EMA" : "MA", Color = c });
            }

            MaGrid.ItemsSource = Lines;
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            Lines.Add(new MaLine { Period = 20, MaType = "MA", Color = Color.FromRgb(0xFF, 0xD7, 0x00) });
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            var selected = MaGrid.SelectedItem as MaLine;
            if (selected != null) Lines.Remove(selected);
        }


        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.DataContext is not MaLine line) return;
            var dlg = new ColorPickerDialog(line.Color) { Owner = this };
            if (dlg.ShowDialog() == true)
                line.Color = dlg.SelectedColor;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var valid = new List<MaLine>();
            foreach (var ln in Lines)
            {
                if (ln.Period <= 0)
                {
                    MessageBox.Show("期間必須為正整數");
                    return;
                }
                valid.Add(ln);
            }

            ResultLines = valid
                .GroupBy(x => $"{x.MaType}:{x.Period}")
                .Select(g => g.First())
                .ToList();

            DialogResult = true;
        }
    }
}
