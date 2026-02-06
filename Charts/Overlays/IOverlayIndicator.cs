using System.Collections.Generic;
using System.Windows.Controls;

namespace Charts
{
    public interface IOverlayIndicator
    {
        string TagName { get; }
        void OnDataChanged(List<GraphKBar> bars);
        void OnViewportChanged(int visibleStart, int visibleCount, double spacing);
        void OnCrosshairIndexChanged(int visibleIndex, bool isValid);
        void Draw(Canvas layer, ChartPane pane);
        IEnumerable<PriceInfoPanel.InfoLine> GetInfoLines(int dataIndex, int prevDataIndex, int priceDecimals);
    }
}
