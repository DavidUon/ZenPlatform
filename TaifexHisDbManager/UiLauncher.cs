using System;
using System.Windows;

namespace TaifexHisDbManager
{
    internal sealed class UiLauncher
    {
        private static string DefaultDbFolder => System.IO.Path.Combine(AppContext.BaseDirectory, "回測歷史資料庫");

        public void ShowMaintenance(Window? owner = null)
        {
            var window = new DbValidationWindow(DefaultDbFolder, DbValidationWindow.ValidationMode.Import);
            if (owner != null)
                window.Owner = owner;
            window.ShowDialog();
        }

        public (bool Accepted, DateTime? Start, DateTime? End, int PreloadDays, BacktestMode Mode, BacktestProduct Product) ShowDateRange(Window? owner = null)
        {
            var window = new DbValidationWindow(DefaultDbFolder, DbValidationWindow.ValidationMode.DateRange);
            if (owner != null)
                window.Owner = owner;

            bool? result = window.ShowDialog();
            return (result == true, window.SelectedStartDateTime, window.SelectedEndDateTime, window.SelectedPreloadDays, window.SelectedBacktestMode, window.SelectedBacktestProduct);
        }
    }
}
