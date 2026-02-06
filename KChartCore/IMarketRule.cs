namespace KChartCore
{
    /// <summary>
    /// 市場規則介面，用於定義各種期貨商品的交易特性
    /// </summary>
    public interface IMarketRule
    {
        /// <summary>
        /// 除錯訊息事件，用於輸出除錯資訊
        /// </summary>
        event Action<string>? DebugMsg;
        /// <summary>
        /// 判斷指定時間是否為市場開市時間
        /// </summary>
        /// <param name="time">要檢查的時間</param>
        /// <returns>true表示開市，false表示休市</returns>
        bool IsMarketOpen(DateTime time);

        /// <summary>
        /// 判斷指定時間是否為市場開盤時刻
        /// </summary>
        /// <param name="time">要檢查的時間</param>
        /// <returns>true表示是開盤時刻</returns>
        bool IsMarketOpenTime(DateTime time);

        /// <summary>
        /// 判斷指定時間是否為市場收盤時刻
        /// </summary>
        /// <param name="time">要檢查的時間</param>
        /// <returns>true表示是收盤時刻</returns>
        bool IsMarketCloseTime(DateTime time);

        /// <summary>
        /// 判斷指定週期的K棒是否應該結束
        /// </summary>
        /// <param name="currentTime">當前時間</param>
        /// <param name="periodMinutes">K棒週期（分鐘）</param>
        /// <returns>true表示該週期K棒應該結束</returns>
        bool ShouldClosePeriod(DateTime currentTime, int periodMinutes);

        /// <summary>
        /// 計算指定時間所屬的週期開始時間
        /// </summary>
        /// <param name="time">指定時間</param>
        /// <param name="periodMinutes">週期長度（分鐘）</param>
        /// <returns>該週期的開始時間</returns>
        DateTime GetPeriodStartTime(DateTime time, int periodMinutes);

        /// <summary>
        /// 判斷指定日期是否為交易日
        /// </summary>
        /// <param name="date">要檢查的日期</param>
        /// <returns>true表示是交易日</returns>
        bool IsTradingDay(DateTime date);

        /// <summary>
        /// 取得市場名稱
        /// </summary>
        string MarketName { get; }

        /// <summary>
        /// 取得支援的交易時段資訊
        /// </summary>
        IEnumerable<TradingSession> TradingSessions { get; }

        /// <summary>
        /// 判斷指定時間是否應該執行K棒封存
        /// </summary>
        /// <param name="time">要檢查的時間</param>
        /// <returns>true表示應該封存K棒，false表示跳過</returns>
        bool IsSealKbar(DateTime time);
    }

    /// <summary>
    /// 交易時段資訊
    /// </summary>
    public class TradingSession
    {
        /// <summary>
        /// 時段名稱（如：日盤、夜盤）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 開始時間
        /// </summary>
        public TimeSpan StartTime { get; set; }

        /// <summary>
        /// 結束時間
        /// </summary>
        public TimeSpan EndTime { get; set; }

        /// <summary>
        /// 是否跨日（夜盤通常會跨日）
        /// </summary>
        public bool CrossDay { get; set; }
    }
}