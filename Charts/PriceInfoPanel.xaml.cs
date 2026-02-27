using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Charts
{
    public partial class PriceInfoPanel : UserControl
    {
        private readonly Dictionary<string, (Border header, StackPanel lines)> _sections = new();
        public event Action? TitleClicked;
        public event Action<string>? SectionHeaderClicked; // id => e.g., "MA", "BOLL"
        public event Action? OnContractChgReq; // 單一事件：要求變更商品/合約
        private bool _titleClickable = false;
        private int _priceDecimalPlaces = 0; // 價格小數位數
        public PriceInfoPanel()
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            TitleText.MouseLeftButtonUp += (_, __) => { if (_titleClickable) TitleClicked?.Invoke(); };
            try
            {
                ContractNameText.Cursor = Cursors.Hand;
                ContractYmText.Cursor = Cursors.Hand;
                ContractNameText.MouseLeftButtonUp += (_, __) => { OnContractChgReq?.Invoke(); };
                ContractYmText.MouseLeftButtonUp +=   (_, __) => { OnContractChgReq?.Invoke(); };
                // 標題也可點擊
                ContractNameLabel.Cursor = Cursors.Hand;
                ContractYmLabel.Cursor = Cursors.Hand;
                ContractNameLabel.MouseLeftButtonUp += (_, __) => { OnContractChgReq?.Invoke(); };
                ContractYmLabel.MouseLeftButtonUp +=   (_, __) => { OnContractChgReq?.Invoke(); };
            }
            catch { }
        }

        public void SetTitle(string title)
        {
            TitleText.Text = title;
        }

        public void SetTitleClickable(bool clickable)
        {
            _titleClickable = clickable;
            TitleText.Cursor = clickable ? Cursors.Hand : Cursors.Arrow;
        }

        public void SetPriceDecimalPlaces(int places)
        {
            _priceDecimalPlaces = Math.Max(0, places);
        }

        // Contract info
        public void SetContractInfo(string name, string ym)
        {
            ContractNameText.Text = string.IsNullOrWhiteSpace(name) ? "---" : name;
            ContractYmText.Text = string.IsNullOrWhiteSpace(ym) ? "---" : ym;
            ContractPanel.Visibility = Visibility.Visible;
            // 顯示報價區塊（先以佔位符顯示）
            SetQuotePlaceholders();
        }

        public void SetQuotePlaceholders()
        {
            QuoteTimeText.Text = "---";
            LastPriceText.Text = "---";
            QuotePanel.Visibility = Visibility.Visible;
        }

        public void SetQuoteInfo(string? quoteTime, string? ask, string? last, string? bid, string? volume, string? dayChange)
        {
            // 只更新有提供的欄位（null 表示不更新該欄位）
            if (quoteTime != null)
            {
                QuoteTimeText.Text = string.IsNullOrWhiteSpace(quoteTime) ? "---" : quoteTime;
                QuoteTimeText.Foreground = Brushes.White;
            }

            if (ask != null)
            {
            }

            if (last != null)
            {
                if (string.IsNullOrWhiteSpace(last))
                {
                    LastPriceText.Text = "---";
                }
                else if (decimal.TryParse(last, out var parsed))
                {
                    LastPriceText.Text = parsed.ToString("0");
                }
                else
                {
                    LastPriceText.Text = last;
                }
                LastPriceText.Foreground = Brushes.Yellow;
            }

            if (bid != null)
            {
            }

            if (volume != null)
            {
            }

            if (dayChange != null)
            {
            }

            QuotePanel.Visibility = Visibility.Visible;
        }

        public void ClearLines()
        {
            LinesPanel.Children.Clear();
            LinesPanel.Visibility = Visibility.Collapsed;
        }

        public void SetLines(IEnumerable<(string Text, Brush? Color)> lines)
        {
            LinesPanel.Children.Clear();
            foreach (var line in lines)
            {
                var tb = new TextBlock
                {
                    Text = line.Text,
                    Foreground = line.Color ?? Brushes.White,
                    FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                    Margin = new System.Windows.Thickness(0, 2, 0, 0)
                };
                tb.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                LinesPanel.Children.Add(tb);
            }
        }

        public struct InfoLine
        {
            public string Label;
            public string ValueText;
            public Brush ValueBrush;
            // ArrowDir: 1 上升、-1 下降、0 或 null 不顯示
            public int? ArrowDir;
        }

        public void SetStructuredLines(IEnumerable<InfoLine> lines)
        {
            LinesPanel.Children.Clear();
            bool any = false;
            foreach (var ln in lines)
            {
                var grid = new Grid { Margin = new System.Windows.Thickness(0, 2, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // label
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // value (right)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // arrow

                var label = new TextBlock
                {
                    Text = ln.Label,
                    Foreground = Brushes.Gainsboro,
                    FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                    Margin = new System.Windows.Thickness(0, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                label.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                var value = new TextBlock
                {
                    Text = ln.ValueText,
                    Foreground = ln.ValueBrush ?? Brushes.White,
                    FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                value.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);

                if (ln.ArrowDir.HasValue && ln.ArrowDir.Value != 0)
                {
                    bool up = ln.ArrowDir.Value > 0;
                    var arrow = new TextBlock
                    {
                        Text = up ? "▲" : "▼",
                        Foreground = up ? Brushes.Red : Brushes.LimeGreen,
                        FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                        Margin = new System.Windows.Thickness(2, 0, 0, 0),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                    arrow.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                    Grid.SetColumn(arrow, 2);
                    grid.Children.Add(arrow);
                }

                LinesPanel.Children.Add(grid);
                any = true;
            }
            LinesPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetTags(IEnumerable<string> tags)
        {
            TagsPanel.Children.Clear();
            bool any = false;
            foreach (var t in tags)
            {
                var b = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                    CornerRadius = new System.Windows.CornerRadius(3),
                    Padding = new System.Windows.Thickness(6, 2, 6, 2),
                    Margin = new System.Windows.Thickness(0, 0, 6, 6)
                };
                var tb = new TextBlock { Text = t, Foreground = Brushes.White, FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14) };
                tb.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                b.Child = tb;
                TagsPanel.Children.Add(b);
                any = true;
            }
            TagsPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- Sectioned API (MA / BOLL) ---
        public void SetSection(string id, string title, IEnumerable<InfoLine> lines)
        {
            if (!_sections.TryGetValue(id, out var sec))
            {
                // Create header
                var header = new Border
                {
                    // 與標題列一致的深灰背景
                    Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var titleTb = new TextBlock
                {
                    Text = title,
                    Foreground = Brushes.White,
                    FontSize = ChartFontManager.GetFontSize("ChartFontSizeLg", 17),
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    Cursor = Cursors.Hand
                };
                titleTb.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeLg");
                header.Child = titleTb;
                header.MouseLeftButtonUp += (_, __) => { SectionHeaderClicked?.Invoke(id); };

                var linesPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2, 6, 2, 4) };

                SectionsPanel.Children.Add(header);
                SectionsPanel.Children.Add(linesPanel);
                _sections[id] = (header, linesPanel);
            }
            // Update header title (allow rename)
            if (_sections[id].header.Child is TextBlock tb) tb.Text = title;

            // Fill lines
            var lp = _sections[id].lines;
            lp.Children.Clear();
            bool any = false;
            foreach (var ln in lines)
            {
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = ln.Label,
                    Foreground = Brushes.Gainsboro,
                    FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                    Margin = new Thickness(0, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                label.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                var value = new TextBlock
                {
                    Text = ln.ValueText,
                    Foreground = ln.ValueBrush ?? Brushes.White,
                    FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                value.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);

                if (ln.ArrowDir.HasValue && ln.ArrowDir.Value != 0)
                {
                    var arrow = new TextBlock
                    {
                        Text = ln.ArrowDir.Value > 0 ? "▲" : "▼",
                        Foreground = ln.ArrowDir.Value > 0 ? Brushes.Red : Brushes.LimeGreen,
                        FontSize = ChartFontManager.GetFontSize("ChartFontSizeSm", 14),
                        Margin = new Thickness(2, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    arrow.SetResourceReference(Control.FontSizeProperty, "ChartFontSizeSm");
                    Grid.SetColumn(arrow, 2);
                    grid.Children.Add(arrow);
                }

                lp.Children.Add(grid);
                any = true;
            }
            // Show/Hide section when empty
            _sections[id].header.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            _sections[id].lines.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            SectionsPanel.Visibility = SectionsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void RemoveSection(string id)
        {
            if (_sections.TryGetValue(id, out var sec))
            {
                SectionsPanel.Children.Remove(sec.header);
                SectionsPanel.Children.Remove(sec.lines);
                _sections.Remove(id);
            }
            SectionsPanel.Visibility = _sections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ClearSections()
        {
            foreach (var kv in _sections.Values)
            {
                SectionsPanel.Children.Remove(kv.header);
                SectionsPanel.Children.Remove(kv.lines);
            }
            _sections.Clear();
            SectionsPanel.Visibility = Visibility.Collapsed;
        }
    }
}
