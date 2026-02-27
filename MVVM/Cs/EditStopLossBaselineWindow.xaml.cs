using System.Globalization;
using System.Windows;

namespace ZenPlatform
{
    public partial class EditStopLossBaselineWindow : Window
    {
        public EditStopLossBaselineWindow(int sessionId, decimal? currentBaseline, decimal? currentPrice)
        {
            InitializeComponent();

            HeaderText.Text = $"任務 [{sessionId}] 停損基準線調整";
            CurrentPriceText.Text = currentPrice.HasValue
                ? $"目前市價：{currentPrice.Value:0.##}"
                : "目前市價：---";
            CurrentBaselineText.Text = currentBaseline.HasValue
                ? $"目前基準線：{currentBaseline.Value:0.##}"
                : "目前基準線：---";

            if (currentBaseline.HasValue)
            {
                BaselineInput.Text = currentBaseline.Value.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        public decimal? NewBaseline { get; private set; }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            var text = BaselineInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text) ||
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var baseline) ||
                baseline <= 0m)
            {
                MessageBoxWindow.Show(this, "請輸入大於 0 的停損基準線數值。", "停損基準線");
                return;
            }

            NewBaseline = baseline;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
