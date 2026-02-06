using System;
using System.Windows;

namespace Charts
{
    /// <summary>
    /// K棒螢幕座標結構
    /// </summary>
    public struct KBarScreenCoordinates
    {
        public double YOpen { get; set; }
        public double YClose { get; set; }
        public double YHigh { get; set; }
        public double YLow { get; set; }
        public double XCenter { get; set; }
        public double XLeft { get; set; }

        public KBarScreenCoordinates(double xLeft, double xCenter, double yOpen, double yClose, double yHigh, double yLow)
        {
            XLeft = xLeft;
            XCenter = xCenter;
            YOpen = yOpen;
            YClose = yClose;
            YHigh = yHigh;
            YLow = yLow;
        }
    }

    /// <summary>
    /// 滑鼠位置計算結果
    /// </summary>
    public struct MousePositionResult
    {
        public int BarIndex { get; set; }
        public int VisibleIndex { get; set; }
        public double SnapX { get; set; }
        public bool IsValidBar { get; set; }

        public MousePositionResult(int barIndex, int visibleIndex, double snapX, bool isValidBar)
        {
            BarIndex = barIndex;
            VisibleIndex = visibleIndex;
            SnapX = snapX;
            IsValidBar = isValidBar;
        }
    }

    /// <summary>
    /// 座標計算器 - 統一管理所有座標轉換和快取常用計算結果
    /// </summary>
    public class CoordinateCalculator
    {
        // 快取的計算結果
        private double _cachedChartWidthMinusBarWidth;
        private double _cachedChartHeightMinusXAxis;
        private double _cachedHalfBarWidth;
        private double _cachedRightmostCenterX;
        private bool _cacheValid = false;

        // 當前的圖表參數 (用於檢查快取是否有效)
        private double _lastChartWidth;
        private double _lastChartHeight;
        private double _lastBarWidth;
        private double _lastXAxisHeight;
        private double _lastLeftMargin;
        private double _lastTopMargin;
        private double _lastRightMargin;
        private double _lastBottomMargin;

        /// <summary>
        /// 更新快取 (當圖表尺寸或K棒參數改變時調用)
        /// </summary>
        public void UpdateCache(double chartWidth, double chartHeight, double barWidth, double xAxisHeight,
            double leftMargin, double topMargin, double rightMargin, double bottomMargin)
        {
            // 檢查是否需要更新快取
            if (_cacheValid &&
                _lastChartWidth == chartWidth &&
                _lastChartHeight == chartHeight &&
                _lastBarWidth == barWidth &&
                _lastXAxisHeight == xAxisHeight &&
                _lastLeftMargin == leftMargin &&
                _lastTopMargin == topMargin &&
                _lastRightMargin == rightMargin &&
                _lastBottomMargin == bottomMargin)
            {
                return; // 快取仍然有效
            }

            // 更新快取 (chartWidth 和 chartHeight 已經是扣除邊距後的尺寸)
            _cachedChartWidthMinusBarWidth = chartWidth - barWidth;
            _cachedChartHeightMinusXAxis = chartHeight;
            _cachedHalfBarWidth = barWidth / 2.0;
            _cachedRightmostCenterX = chartWidth - barWidth / 2.0;

            // 記錄當前參數
            _lastChartWidth = chartWidth;
            _lastChartHeight = chartHeight;
            _lastBarWidth = barWidth;
            _lastXAxisHeight = xAxisHeight;
            _lastLeftMargin = leftMargin;
            _lastTopMargin = topMargin;
            _lastRightMargin = rightMargin;
            _lastBottomMargin = bottomMargin;
            _cacheValid = true;
        }

        /// <summary>
        /// 將價格轉換為Y座標
        /// </summary>
        public double PriceToY(decimal price, decimal displayMaxPrice, decimal displayMinPrice)
        {
            if (displayMaxPrice == displayMinPrice) return 0;

            double ratio = (double)(displayMaxPrice - price) / (double)(displayMaxPrice - displayMinPrice);
            return ratio * _cachedChartHeightMinusXAxis;
        }

        /// <summary>
        /// 將Y座標轉換為價格
        /// </summary>
        public decimal YToPrice(double y, decimal displayMaxPrice, decimal displayMinPrice)
        {
            if (displayMaxPrice == displayMinPrice) return 0;

            double ratio = y / _cachedChartHeightMinusXAxis;
            return displayMaxPrice - (decimal)ratio * (displayMaxPrice - displayMinPrice);
        }

        /// <summary>
        /// 根據可視索引計算K棒的X座標
        /// </summary>
        public double GetBarXByVisibleIndex(int visibleIndexFromLeft, int visibleBarCount, double barSpacing)
        {
            double offsetFromRight = visibleBarCount - 1 - visibleIndexFromLeft;
            return _cachedChartWidthMinusBarWidth - offsetFromRight * barSpacing;
        }

        /// <summary>
        /// 計算K棒的完整螢幕座標
        /// </summary>
        public KBarScreenCoordinates CalculateBarCoordinates(GraphKBar bar, int visibleIndex, int visibleBarCount,
            double barSpacing, decimal displayMaxPrice, decimal displayMinPrice)
        {
            double xLeft = GetBarXByVisibleIndex(visibleIndex, visibleBarCount, barSpacing);
            double xCenter = xLeft + _cachedHalfBarWidth;

            double yOpen = PriceToY(bar.Open, displayMaxPrice, displayMinPrice);
            double yClose = PriceToY(bar.Close, displayMaxPrice, displayMinPrice);
            double yHigh = PriceToY(bar.High, displayMaxPrice, displayMinPrice);
            double yLow = PriceToY(bar.Low, displayMaxPrice, displayMinPrice);

            return new KBarScreenCoordinates(xLeft, xCenter, yOpen, yClose, yHigh, yLow);
        }

        /// <summary>
        /// 根據滑鼠位置計算對應的K棒索引和吸附座標
        /// </summary>
        public MousePositionResult CalculateMousePosition(Point mousePos, int visibleStartIndex, int visibleBarCount,
            double barSpacing, int totalBarCount)
        {
            int idxFromRight = (int)Math.Round((_cachedRightmostCenterX - mousePos.X) / barSpacing);
            int visIdx = visibleBarCount - 1 - idxFromRight;
            visIdx = Math.Max(0, Math.Min(visibleBarCount - 1, visIdx));
            int barIndex = visibleStartIndex + visIdx;

            bool isValidBar = barIndex >= 0 && barIndex < totalBarCount;
            double snapX = isValidBar ?
                GetBarXByVisibleIndex(visIdx, visibleBarCount, barSpacing) + _cachedHalfBarWidth :
                mousePos.X;

            return new MousePositionResult(barIndex, visIdx, snapX, isValidBar);
        }

        /// <summary>
        /// 取得快取的常用計算結果
        /// </summary>
        public double GetChartWidthMinusBarWidth() => _cachedChartWidthMinusBarWidth;
        public double GetChartHeightMinusXAxis() => _cachedChartHeightMinusXAxis;
        public double GetHalfBarWidth() => _cachedHalfBarWidth;
        public double GetRightmostCenterX() => _cachedRightmostCenterX;

        /// <summary>
        /// 檢查快取是否有效
        /// </summary>
        public bool IsCacheValid => _cacheValid;

        /// <summary>
        /// 清除快取
        /// </summary>
        public void ClearCache()
        {
            _cacheValid = false;
        }
    }
}
