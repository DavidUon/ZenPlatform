using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Charts
{
    public partial class TimeframeBar : UserControl
    {
        // 目前選擇的週期（K），預設 1
        private int _currentTimeframe = 1;
        private readonly int[] _standard = new[] { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120 };
        private readonly Dictionary<int, Border> _chipByTf = new();
        private Border? _customChip;

        // 週期變更事件：外部只需訂閱此事件
        // 事件：OnTimeframeChange<int>
        public event EventHandler<int>? OnTimeframeChange;

        public TimeframeBar()
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            BuildUi();
            UpdateHighlight();
        }

        private void BuildUi()
        {
            Container.Children.Clear();

            foreach (var tf in _standard)
            {
                var chip = CreateChip(tf + "K", isCustom: false, tf);
                _chipByTf[tf] = chip;
                Container.Children.Add(chip);
            }

            // 分隔空隙再放自訂
            Container.Children.Add(new Border { Width = 14, Background = Brushes.Transparent });
            _customChip = CreateChip("自訂週期", isCustom: true);
            Container.Children.Add(_customChip);
        }

        private static readonly Brush BorderGray = new SolidColorBrush(Color.FromRgb(0x6D, 0x6D, 0x6D));

        private Border CreateChip(string text, bool isCustom, int tf = 0)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = isCustom ? null : tf
            };
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(10, 2, 10, 2),
                FontSize = ChartFontManager.GetFontSize("ChartFontSizeMd", 16),
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeMd");
            border.Child = tb;

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (isCustom)
                {
                    // 叫出輸入視窗僅允許 1-120
                    var dlg = new InputPeriodDialog { Owner = Window.GetWindow(this) };
                    try { dlg.Title = "輸入自訂週期 (1-120)"; } catch { }
                    if (dlg.ShowDialog() == true)
                    {
                        int v = dlg.Period;
                        if (v < 1 || v > 120)
                        {
                            MessageBox.Show("自訂週期須介於 1~120");
                            return;
                        }
                        _currentTimeframe = v;
                        // 更新顯示：自訂週期(17)
                        ((TextBlock)border.Child).Text = $"自訂週期({_currentTimeframe})";
                        UpdateHighlight();
                        OnTimeframeChange?.Invoke(this, _currentTimeframe);
                    }
                }
                else
                {
                    if (border.Tag is int value)
                    {
                        _currentTimeframe = value;
                        UpdateHighlight();
                        OnTimeframeChange?.Invoke(this, _currentTimeframe);
                    }
                }
            };

            return border;
        }

        private void UpdateHighlight()
        {
            foreach (var kv in _chipByTf)
            {
                bool isSelected = kv.Key == _currentTimeframe;
                ApplyChipStyle(kv.Value, isSelected);
            }

            if (_customChip != null)
            {
                bool isCustom = !_standard.Contains(_currentTimeframe);
                ApplyChipStyle(_customChip, isCustom);
                var tb = (TextBlock)_customChip.Child;
                tb.Text = isCustom ? $"自訂週期({_currentTimeframe})" : "自訂週期";
            }
        }

        private static void ApplyChipStyle(Border chip, bool selected)
        {
            if (selected)
            {
                chip.Background = new SolidColorBrush(Color.FromRgb(188, 188, 58)); // 舊樣式黃色
                chip.BorderBrush = BorderGray;
                ((TextBlock)chip.Child).Foreground = Brushes.Black;
                ((TextBlock)chip.Child).FontWeight = FontWeights.SemiBold;
            }
            else
            {
                chip.Background = Brushes.Transparent;
                chip.BorderBrush = BorderGray;
                ((TextBlock)chip.Child).Foreground = Brushes.Gray;
                ((TextBlock)chip.Child).FontWeight = FontWeights.Normal;
            }
        }

        public void SetTimeframe(int value)
        {
            if (value < 1 || value > 120) return;
            _currentTimeframe = value;
            UpdateHighlight();
        }
    }
}
