using System;
using System.Windows;

namespace TaifexHisDbManager
{
    internal sealed class UiLauncher
    {
        private static string DefaultDbFolder => MagistockStoragePaths.MagistockLibPath;

        public void ShowMaintenance(Window? owner = null)
        {
            MagistockStoragePaths.EnsureFolders();
            var window = new DbValidationWindow(DefaultDbFolder, DbValidationWindow.ValidationMode.Import);
            if (owner != null)
                window.Owner = owner;
            window.ShowDialog();
        }

        public (bool Accepted, DateTime? Start, DateTime? End, int PreloadDays, BacktestProduct Product, BacktestPrecision Precision) ShowDateRange(Window? owner = null)
        {
            MagistockStoragePaths.EnsureFolders();
            var window = new DbValidationWindow(DefaultDbFolder, DbValidationWindow.ValidationMode.DateRange);
            if (owner != null)
                window.Owner = owner;

            bool? result = window.ShowDialog();
            return (result == true, window.SelectedStartDateTime, window.SelectedEndDateTime, window.SelectedPreloadDays, window.SelectedBacktestProduct, window.SelectedBacktestPrecision);
        }
    }
}
