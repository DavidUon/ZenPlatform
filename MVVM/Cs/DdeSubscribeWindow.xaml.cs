using System;
using System.Collections.Generic;
using ZenPlatform.DdePrice;
using System.Windows;

namespace ZenPlatform
{
    public partial class DdeSubscribeWindow : Window
    {
        public string SelectedProduct { get; private set; } = "";
        public int SelectedYear { get; private set; }
        public int SelectedMonth { get; private set; }

        private readonly List<(int Year, int Month, string Label)> _monthOptions = new();

        public DdeSubscribeWindow()
        {
            InitializeComponent();
            LoadProducts();
            LoadMonths();
        }

        private void LoadProducts()
        {
            foreach (var product in DdeItemCatalog.Products)
            {
                ProductBox.Items.Add(product.DisplayName);
            }
            ProductBox.SelectedIndex = 0;
        }

        private void LoadMonths()
        {
            var (startYear, startMonth) = GetStartContractMonth(DateTime.Now);
            for (var i = 0; i < 6; i++)
            {
                var dt = new DateTime(startYear, startMonth, 1).AddMonths(i);
                var label = $"{dt:yyyy/MM}";
                _monthOptions.Add((dt.Year, dt.Month, label));
                MonthBox.Items.Add(label);
            }
            MonthBox.SelectedIndex = 0;
        }

        private static (int Year, int Month) GetStartContractMonth(DateTime now)
        {
            var thirdWed = GetThirdWednesday(now.Year, now.Month);
            var switchTime = new DateTime(now.Year, now.Month, thirdWed.Day, 15, 0, 0, now.Kind);
            if (now >= switchTime)
            {
                var next = now.AddMonths(1);
                return (next.Year, next.Month);
            }

            return (now.Year, now.Month);
        }

        private static DateTime GetThirdWednesday(int year, int month)
        {
            var firstDay = new DateTime(year, month, 1);
            var offset = ((int)DayOfWeek.Wednesday - (int)firstDay.DayOfWeek + 7) % 7;
            var firstWednesday = firstDay.AddDays(offset);
            return firstWednesday.AddDays(14);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            var product = ProductBox.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(product))
            {
                MessageBoxWindow.Show(this, "請選擇商品。", "提示");
                return;
            }

            if (MonthBox.SelectedIndex < 0 || MonthBox.SelectedIndex >= _monthOptions.Count)
            {
                MessageBoxWindow.Show(this, "請選擇月份。", "提示");
                return;
            }

            SelectedProduct = product;
            var option = _monthOptions[MonthBox.SelectedIndex];
            SelectedYear = option.Year;
            SelectedMonth = option.Month;

            DialogResult = true;
            Close();
        }
    }
}
