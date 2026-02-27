using System;
using System.Windows;

namespace TaifexHisDbManager
{
    public sealed class TaifexHisDbManager
    {
        private readonly Window _owner;
        private readonly UiLauncher _launcher = new();

        public TaifexHisDbManager(Window owner)
        {
            _owner = owner;
        }

        public void ImportDialog()
        {
            _launcher.ShowMaintenance(_owner);
        }

        public (bool Accepted, DateTime? Start, DateTime? End, int PreloadDays, BacktestProduct Product, BacktestPrecision Precision) GetBackTestInfo()
        {
            return _launcher.ShowDateRange(_owner);
        }
    }
}
