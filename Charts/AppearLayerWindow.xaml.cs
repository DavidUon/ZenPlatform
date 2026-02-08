using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Charts
{
    public partial class AppearLayerWindow : Window
    {
        private readonly MultiPaneChartView _chart;

        public AppearLayerWindow(MultiPaneChartView chart)
        {
            ChartFontManager.EnsureInitialized();
            InitializeComponent();
            _chart = chart;
            InitStates();
            HookEvents();
        }

        private void InitStates()
        {
            var mainPane = _chart.GetMainPricePane();
            CbCrosshair.IsChecked = mainPane.IsCrosshairVisible;
            CbTooltip.IsChecked = mainPane.IsTooltipVisible;

            // 初始時根據十字線狀態設定查價視窗的可用性
            CbTooltip.IsEnabled = mainPane.IsCrosshairVisible;

            CbVol.IsChecked = _chart.HasIndicatorPanel(IndicatorPanelType.Vol);
            CbKd.IsChecked = _chart.HasIndicatorPanel(IndicatorPanelType.Kd);
            CbMacd.IsChecked = _chart.HasIndicatorPanel(IndicatorPanelType.Macd);

            var cfgs = _chart.GetMainPricePane().GetOverlayConfigs();
            CbMa.IsChecked = cfgs.Any(c => c.Type == "MA");
            CbBbi.IsChecked = cfgs.Any(c => c.Type == "BBI");
            CbBoll.IsChecked = cfgs.Any(c => c.Type == "BOLL");
        }

        private void HookEvents()
        {
            CbCrosshair.Checked += (_, __) => ToggleCrosshair(true);
            CbCrosshair.Unchecked += (_, __) => ToggleCrosshair(false);
            CbTooltip.Checked += (_, __) => ToggleTooltip(true);
            CbTooltip.Unchecked += (_, __) => ToggleTooltip(false);

            CbVol.Checked += (_, __) => ToggleVol(true);
            CbVol.Unchecked += (_, __) => ToggleVol(false);
            CbKd.Checked += (_, __) => ToggleKd(true);
            CbKd.Unchecked += (_, __) => ToggleKd(false);
            CbMacd.Checked += (_, __) => ToggleMacd(true);
            CbMacd.Unchecked += (_, __) => ToggleMacd(false);
            CbMa.Checked += (_, __) => ToggleMa(true);
            CbMa.Unchecked += (_, __) => ToggleMa(false);
            CbBbi.Checked += (_, __) => ToggleBbi(true);
            CbBbi.Unchecked += (_, __) => ToggleBbi(false);
            CbBoll.Checked += (_, __) => ToggleBoll(true);
            CbBoll.Unchecked += (_, __) => ToggleBoll(false);
        }

        private void ToggleCrosshair(bool on)
        {
            var mainPane = _chart.GetMainPricePane();
            mainPane.SetCrosshairVisible(on);

            if (!on)
            {
                // 十字線關閉時，查價視窗也必須關閉且不可選
                CbTooltip.IsChecked = false;
                mainPane.SetTooltipVisible(false);
                CbTooltip.IsEnabled = false;
            }
            else
            {
                // 十字線開啟時，恢復查價視窗的可選狀態
                CbTooltip.IsEnabled = true;
                // 可以選擇恢復查價視窗為開啟狀態（根據之前的設定或預設開啟）
                if (!CbTooltip.IsChecked.HasValue || !CbTooltip.IsChecked.Value)
                {
                    CbTooltip.IsChecked = true;
                    mainPane.SetTooltipVisible(true);
                }
            }

            // 同步所有副圖面板
            _chart.SyncCrosshairSettings();
        }

        private void ToggleTooltip(bool on)
        {
            var mainPane = _chart.GetMainPricePane();
            mainPane.SetTooltipVisible(on);

            // 同步所有副圖面板
            _chart.SyncCrosshairSettings();
        }

        private void ToggleVol(bool on)
        {
            if (on) _chart.AddIndicatorPanel(IndicatorPanelType.Vol);
            else _chart.RemoveIndicatorPanel(IndicatorPanelType.Vol);
        }
        private void ToggleKd(bool on)
        {
            if (on) _chart.AddIndicatorPanel(IndicatorPanelType.Kd);
            else _chart.RemoveIndicatorPanel(IndicatorPanelType.Kd);
        }
        private void ToggleMacd(bool on)
        {
            if (on) _chart.AddIndicatorPanel(IndicatorPanelType.Macd);
            else _chart.RemoveIndicatorPanel(IndicatorPanelType.Macd);
        }
        private void ToggleMa(bool on)
        {
            var pane = _chart.GetMainPricePane();
            var has = pane.GetOverlayConfigs().Any(c => c.Type == "MA");
            if (on)
            {
                if (!has)
                {
                    _chart.AddMaOverlay(144, "SMA", Colors.Gold);
                }
            }
            else
            {
                if (has) _chart.ClearMaOverlays();
            }
        }
        private void ToggleBbi(bool on)
        {
            var pane = _chart.GetMainPricePane();
            var has = pane.GetOverlayConfigs().Any(c => c.Type == "BBI");
            if (on)
            {
                if (!has) _chart.AddBbiOverlay();
            }
            else
            {
                if (has) _chart.RemoveBbiOverlay();
            }
        }
        private void ToggleBoll(bool on)
        {
            var pane = _chart.GetMainPricePane();
            var has = pane.GetOverlayConfigs().Any(c => c.Type == "BOLL");
            if (on)
            {
                if (!has) _chart.AddBollingerOverlay();
            }
            else
            {
                if (has) _chart.RemoveBollingerOverlay();
            }
        }
    }
}
