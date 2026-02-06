using System;
using System.Windows;
using ZenPlatform.Strategy;

namespace ZenPlatform
{
    public partial class StrategySettingsWindow : Window
    {
        private readonly RuleSet _ruleSet;

        public StrategySettingsWindow(RuleSet ruleSet)
        {
            _ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
            InitializeComponent();
            LoadFromRuleSet();
        }

        private void LoadFromRuleSet()
        {
            OrderSizeBox.Text = _ruleSet.OrderSize.ToString();
            KbarPeriodBox.Text = _ruleSet.KbarPeriod.ToString();
            KPeriodBox.Text = _ruleSet.KPeriod.ToString();
            DPeriodBox.Text = _ruleSet.DPeriod.ToString();
            RsvPeriodBox.Text = _ruleSet.RsvPeriod.ToString();
            TakeProfitBox.Text = _ruleSet.TakeProfitPoints.ToString();
            MaxReverseBox.Text = _ruleSet.MaxReverseCount.ToString();
            MaxSessionBox.Text = _ruleSet.MaxSessionCount.ToString();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (!TryReadInt(OrderSizeBox, 1, int.MaxValue, out var orderSize))
            {
                MessageBoxWindow.Show(this, "下單口數需為正整數。", "策略設定");
                return;
            }

            if (!TryReadInt(KbarPeriodBox, 1, 120, out var kbar))
            {
                MessageBoxWindow.Show(this, "K線週期需為 1~120。", "策略設定");
                return;
            }

            if (!TryReadInt(KPeriodBox, 1, 200, out var k))
            {
                MessageBoxWindow.Show(this, "K參數需為正整數。", "策略設定");
                return;
            }

            if (!TryReadInt(DPeriodBox, 1, 200, out var d))
            {
                MessageBoxWindow.Show(this, "D參數需為正整數。", "策略設定");
                return;
            }

            if (!TryReadInt(RsvPeriodBox, 1, 200, out var rsv))
            {
                MessageBoxWindow.Show(this, "RSV參數需為正整數。", "策略設定");
                return;
            }

            if (!TryReadInt(TakeProfitBox, 0, 100000, out var takeProfit))
            {
                MessageBoxWindow.Show(this, "停利點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(MaxReverseBox, 0, 1000, out var maxReverse))
            {
                MessageBoxWindow.Show(this, "最大反手次數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(MaxSessionBox, 1, 10000, out var maxSessions))
            {
                MessageBoxWindow.Show(this, "最大任務數需為正整數。", "策略設定");
                return;
            }

            _ruleSet.OrderSize = orderSize;
            _ruleSet.KbarPeriod = kbar;
            _ruleSet.KPeriod = k;
            _ruleSet.DPeriod = d;
            _ruleSet.RsvPeriod = rsv;
            _ruleSet.TakeProfitPoints = takeProfit;
            _ruleSet.MaxReverseCount = maxReverse;
            _ruleSet.MaxSessionCount = maxSessions;

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static bool TryReadInt(System.Windows.Controls.TextBox box, int min, int max, out int value)
        {
            if (!int.TryParse(box.Text.Trim(), out value))
            {
                return false;
            }

            if (value < min || value > max)
            {
                return false;
            }

            return true;
        }
    }
}
