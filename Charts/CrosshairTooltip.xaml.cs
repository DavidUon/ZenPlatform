using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Charts
{

    public partial class CrosshairTooltip : UserControl
    {
        public CrosshairTooltip()
        {
            InitializeComponent();
        }

        public void UpdateData(OhlcData ohlc, List<MaValue> maValues, Brush? accent = null)
        {
            // Update OHLC values (numbers only)
            this.HighText.Text = ohlc.High;
            this.OpenText.Text = ohlc.Open;
            this.CloseText.Text = ohlc.Close;
            this.LowText.Text = ohlc.Low;

            // Apply bullish/bearish accent on OHLC texts and border if provided
            if (accent != null)
            {
                this.HighText.Foreground = accent;
                this.OpenText.Foreground = accent;
                this.CloseText.Foreground = accent;
                this.LowText.Foreground = accent;
                try { this.RootBorder.BorderBrush = accent; } catch { }
            }
        }
    }
}
