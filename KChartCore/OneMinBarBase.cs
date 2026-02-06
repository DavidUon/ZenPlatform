using System.IO;
using Utility;

namespace KChartCore
{
    public class OneMinBarBase
    {
        protected readonly List<FunctionKBar> _oneMinuteHistory = new();
        private readonly int _maxHistoryCount = 10000; // 最多保留10000根K棒
        protected DateTime _currentTime;
        protected readonly TaifexRule _tfxRule = new();
        protected FunctionKBar _floatingBar = new() { IsNullBar = true, IsFloating = true };
        protected DateTime? _lastImportTime = null;

        // 成交量處理相關
        private int _lastSealedVolume = 0;  // 記住上次封存時的總量（用於即時模式）
        private int _currentTotalVolume = 0; // 記住當前累積總量（即時模式下每次 SetVolume 更新）

        protected event Action<int, FunctionKBar>? OnKbarCompleted;

        public OneMinBarBase()
        {
        }

        public int ImportMmsHistory(string pathname, DateTime? currentTime = null)
        {
            // 如果 currentTime 為 null，使用系統當前時間
            DateTime compareTime = currentTime ?? DateTime.Now;
            _lastImportTime = compareTime; // 記錄匯入時間
            // 清空歷史資料和浮動K棒
            _oneMinuteHistory.Clear();
            _floatingBar = new FunctionKBar { IsNullBar = true, IsFloating = true };
            
            using (var reader = new StreamReader(pathname, System.Text.Encoding.UTF8))
            {
                string? line;
                int lineNumber = 0;
                
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    
                    // 跳過前兩行標題
                    if (lineNumber <= 2)
                        continue;
                        
                    // 跳過空行
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    
                    // 解析 CSV 行
                    string[] fields = line.Split(',');
                    if (fields.Length >= 6)
                    {
                        string timeStr = fields[0].Trim();
                        string openStr = fields[1].Trim();
                        string highStr = fields[2].Trim();
                        string lowStr = fields[3].Trim();
                        string closeStr = fields[4].Trim();
                        string volumeStr = fields[5].Trim();
                        string? floatingStr = fields.Length >= 7 ? fields[6].Trim() : null;
                        
                        // 解析時間和價格
                        if (DateTime.TryParse(timeStr, out DateTime closeTime) &&
                            decimal.TryParse(openStr, out decimal open) &&
                            decimal.TryParse(highStr, out decimal high) &&
                            decimal.TryParse(lowStr, out decimal low) &&
                            decimal.TryParse(closeStr, out decimal close) &&
                            int.TryParse(volumeStr, out int volume))
                        {
                            bool isFloating = floatingStr == "1";

                            // 計算開始時間（收盤時間減1分鐘）
                            DateTime startTime = closeTime.AddMinutes(-1);
                            
                            // 建立 K 棒
                            var kbar = new FunctionKBar
                            {
                                StartTime = startTime,
                                CloseTime = closeTime,
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                Volume = volume,
                                IsNullBar = false,
                                IsFloating = isFloating
                            };
                            
                            // 判斷開盤收盤旗標
                            TimeSpan closeTimeOfDay = closeTime.TimeOfDay;
                            if (closeTimeOfDay == new TimeSpan(8, 46, 0) || closeTimeOfDay == new TimeSpan(15, 1, 0))
                            {
                                kbar.ContainsMarketOpen = true;
                                kbar.IsAlignmentBar = true;
                            }
                            if (closeTimeOfDay == new TimeSpan(5, 0, 0) || closeTimeOfDay == new TimeSpan(13, 45, 0))
                            {
                                kbar.ContainsMarketClose = true;
                            }
                            
                            _oneMinuteHistory.Add(kbar);
                        }
                    }
                }
            }

            // 檢查最後一根K棒是否應該是浮動狀態，並同步到 _floatingBar
            if (_oneMinuteHistory.Count > 0)
            {
                var lastBar = _oneMinuteHistory[_oneMinuteHistory.Count - 1];
                if (lastBar.IsFloating || lastBar.CloseTime > compareTime)
                {
                    // 將最後一根移到 _floatingBar
                    _floatingBar = lastBar;
                    _floatingBar.IsFloating = true;
                    _oneMinuteHistory.RemoveAt(_oneMinuteHistory.Count - 1);
                }
            }

            return _oneMinuteHistory.Count;
        }

        public List<FunctionKBar> GetRawDataList(int n)
        {
            if (n <= 0) return new List<FunctionKBar>();

            int count = Math.Min(n, _oneMinuteHistory.Count);
            int startIndex = Math.Max(0, _oneMinuteHistory.Count - count);

            var result = _oneMinuteHistory.GetRange(startIndex, count);

            // 如果需要包含當前浮動K棒，且浮動K棒有資料
            if (!_floatingBar.IsNullBar && n == int.MaxValue)
            {
                result.Add(_floatingBar);
            }

            return result;
        }


        /// <summary>
        /// 找到K棒時間範圍內的實際開盤時刻
        /// </summary>
        private DateTime GetMarketOpenTime(DateTime startTime, DateTime endTime)
        {
            // 檢查日盤開盤時刻 (08:45)
            for (DateTime time = startTime.Date; time <= endTime; time = time.AddDays(1))
            {
                DateTime dayOpenTime = new DateTime(time.Year, time.Month, time.Day, 8, 45, 0);
                if (dayOpenTime >= startTime && dayOpenTime <= endTime && _tfxRule.IsMarketOpenTime(dayOpenTime))
                {
                    return dayOpenTime;
                }
                
                // 檢查夜盤開盤時刻 (15:00)
                DateTime nightOpenTime = new DateTime(time.Year, time.Month, time.Day, 15, 0, 0);
                if (nightOpenTime >= startTime && nightOpenTime <= endTime && _tfxRule.IsMarketOpenTime(nightOpenTime))
                {
                    return nightOpenTime;
                }
            }
            
            return DateTime.MinValue;
        }

        /// <summary>
        /// 找到收盤K棒的合理StartTime（收盤時刻前1分鐘）
        /// </summary>
        private DateTime GetMarketCloseStartTime(DateTime closeTime)
        {
            TimeSpan timeOfDay = closeTime.TimeOfDay;
            
            // 日盤收盤：StartTime應該是13:44
            if (timeOfDay == new TimeSpan(13, 45, 0))
            {
                return new DateTime(closeTime.Year, closeTime.Month, closeTime.Day, 13, 44, 0);
            }
            
            // 夜盤收盤：StartTime應該是收盤前1分鐘
            if (timeOfDay.Hours >= 23 || timeOfDay.Hours <= 5)
            {
                return closeTime.AddMinutes(-1);
            }
            
            return closeTime;
        }

        public void SetCurrentTime(DateTime time)
        {
            // 去掉毫秒誤差，只保留到秒
            _currentTime = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second);
        }

        public bool IsMarketCurrentlyOpen()
        {
            return _tfxRule.IsMarketOpen(_currentTime);
        }

        public void Save(string filePath)
        {
            try
            {
                var lines = new List<string>();

                // 加入完整標頭
                lines.Add("開始時間,收盤時間,開盤價,最高價,最低價,收盤價,成交量,包含開盤,包含收盤,空K棒,浮動K棒,對齊K棒");

                // 遍歷一分鐘歷史資料
                foreach (var bar in _oneMinuteHistory)
                {
                    // 格式化每一行
                    string line = string.Join(",",
                        bar.StartTime.ToString("yyyy/M/d HH:mm"),
                        bar.CloseTime.ToString("yyyy/M/d HH:mm"),
                        (int)bar.Open,
                        (int)bar.High,
                        (int)bar.Low,
                        (int)bar.Close,
                        bar.Volume,
                        bar.ContainsMarketOpen ? "1" : "0",
                        bar.ContainsMarketClose ? "1" : "0",
                        bar.IsNullBar ? "1" : "0",
                        bar.IsFloating ? "1" : "0",
                        bar.IsAlignmentBar ? "1" : "0"
                    );
                    lines.Add(line);
                }

                // 如果有浮動K棒且非空，也要儲存
                if (!_floatingBar.IsNullBar)
                {
                    // 浮動K棒的 CloseTime 可能尚未設定（在封棒前），需要推算
                    DateTime floatingCloseTime = _floatingBar.CloseTime;
                    if (floatingCloseTime == default(DateTime) || floatingCloseTime < _floatingBar.StartTime)
                    {
                        // CloseTime 尚未設定或不合理，使用 _currentTime 的下一個整數分鐘
                        // 例如：_currentTime = 13:45:30 → floatingCloseTime = 13:46:00
                        floatingCloseTime = new DateTime(
                            _currentTime.Year,
                            _currentTime.Month,
                            _currentTime.Day,
                            _currentTime.Hour,
                            _currentTime.Minute,
                            0
                        ).AddMinutes(1);
                    }

                    string floatingLine = string.Join(",",
                        _floatingBar.StartTime.ToString("yyyy/M/d HH:mm"),
                        floatingCloseTime.ToString("yyyy/M/d HH:mm"),
                        (int)_floatingBar.Open,
                        (int)_floatingBar.High,
                        (int)_floatingBar.Low,
                        (int)_floatingBar.Close,
                        _floatingBar.Volume,
                        _floatingBar.ContainsMarketOpen ? "1" : "0",
                        _floatingBar.ContainsMarketClose ? "1" : "0",
                        _floatingBar.IsNullBar ? "1" : "0",
                        "1", // 浮動K棒標記為 true
                        _floatingBar.IsAlignmentBar ? "1" : "0"
                    );
                    lines.Add(floatingLine);
                }

                // 將所有文字行一次性寫入檔案
                File.WriteAllLines(filePath, lines);
                // 已成功儲存K棒資料（包含歷史K棒和浮動K棒）
            }
            catch
            {
                // 儲存檔案時發生錯誤，可在此處理異常
            }
        }

        public int Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // 檔案不存在，返回0
                return 0;
            }

            try
            {
                // 清空舊資料
                _oneMinuteHistory.Clear();
                _floatingBar = new FunctionKBar { IsNullBar = true, IsFloating = true };

                // 讀取檔案
                using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8))
                {
                    string? line;
                    int lineNumber = 0;

                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;

                        // 跳過標題行
                        if (lineNumber == 1 && (line.Contains("日期") || line.Contains("開始時間")))
                            continue;

                        // 跳過空行
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (TryParseCsvLine(line, out var bar))
                        {
                            _oneMinuteHistory.Add(bar);

                            // 更新當前時間為最新的K棒時間
                            if (bar.CloseTime > _currentTime)
                            {
                                _currentTime = bar.CloseTime;
                            }
                        }
                    }
                }

                // 檢查最後一根K棒是否為浮動K棒
                if (_oneMinuteHistory.Count > 0)
                {
                    var lastBar = _oneMinuteHistory[_oneMinuteHistory.Count - 1];
                    if (lastBar.IsFloating)
                    {
                        // 檢查浮動K棒的收盤時間是否已過期（超過當前時間）
                        if (lastBar.CloseTime > _currentTime)
                        {
                            // 時間未過期，移到 _floatingBar
                            _floatingBar = lastBar;
                            _oneMinuteHistory.RemoveAt(_oneMinuteHistory.Count - 1);
                        }
                        else
                        {
                            // 時間已過期，將此K棒轉為完成狀態，保留在歷史資料中
                            // 因為 FunctionKBar 是 struct，必須直接修改 List 中的元素
                            var modifiedBar = lastBar;
                            modifiedBar.IsFloating = false;
                            _oneMinuteHistory[_oneMinuteHistory.Count - 1] = modifiedBar;
                            _floatingBar = new FunctionKBar { IsNullBar = true, IsFloating = true };
                        }
                    }
                    else
                    {
                        // 最後一根不是浮動K棒，設定 _floatingBar 為空
                        _floatingBar = new FunctionKBar { IsNullBar = true, IsFloating = true };
                    }
                }
                else
                {
                    // 沒有K棒資料，設定 _floatingBar 為空
                    _floatingBar = new FunctionKBar { IsNullBar = true, IsFloating = true };
                }

                // 已成功載入K棒資料
                return _oneMinuteHistory.Count;
            }
            catch
            {
                // 載入檔案時發生錯誤，返回0
                return 0;
            }
        }

        private bool TryParseCsvLine(string line, out FunctionKBar bar)
        {
            bar = new FunctionKBar();
            var parts = line.Split(',');

            try
            {
                // 檢查是否為新格式（12個欄位）、中格式（11個欄位）或舊格式（6個欄位）
                if (parts.Length >= 12)
                {
                    // 新格式：開始時間,收盤時間,開盤價,最高價,最低價,收盤價,成交量,包含開盤,包含收盤,空K棒,浮動K棒
                    if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy/M/d HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var startTime) ||
                        !DateTime.TryParseExact(parts[1].Trim(), "yyyy/M/d HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var closeTime) ||
                        !decimal.TryParse(parts[2].Trim(), out var open) ||
                        !decimal.TryParse(parts[3].Trim(), out var high) ||
                        !decimal.TryParse(parts[4].Trim(), out var low) ||
                        !decimal.TryParse(parts[5].Trim(), out var close) ||
                        !int.TryParse(parts[6].Trim(), out var volume))
                    {
                        return false;
                    }

                    // 解析旗標
                    bool containsMarketOpen = parts[7].Trim() == "1";
                    bool containsMarketClose = parts[8].Trim() == "1";
                    bool isNullBar = parts[9].Trim() == "1";
                    bool isFloating = parts[10].Trim() == "1";
                    bool isAlignmentBar = parts[11].Trim() == "1";

                    bar = new FunctionKBar
                    {
                        StartTime = startTime,
                        CloseTime = closeTime,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        ContainsMarketOpen = containsMarketOpen,
                        ContainsMarketClose = containsMarketClose,
                        IsNullBar = isNullBar,
                        IsFloating = isFloating,
                        IsAlignmentBar = isAlignmentBar
                    };
                }
                else if (parts.Length >= 6)
                {
                    // 舊格式：日期,開盤價,最高價,最低價,收盤價,成交量
                    if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy/M/d HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var closeTime) ||
                        !decimal.TryParse(parts[1].Trim(), out var open) ||
                        !decimal.TryParse(parts[2].Trim(), out var high) ||
                        !decimal.TryParse(parts[3].Trim(), out var low) ||
                        !decimal.TryParse(parts[4].Trim(), out var close) ||
                        !int.TryParse(parts[5].Trim(), out var volume))
                    {
                        return false;
                    }

                    bar = new FunctionKBar
                    {
                        StartTime = closeTime.AddMinutes(-1),
                        CloseTime = closeTime,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        IsNullBar = false,
                        IsFloating = false,
                        IsAlignmentBar = false
                    };

                    // 舊格式需要重新計算旗標
                    TimeSpan timeOfDay = closeTime.TimeOfDay;
                    if (timeOfDay == new TimeSpan(8, 46, 0) || timeOfDay == new TimeSpan(15, 1, 0))
                    {
                        bar.ContainsMarketOpen = true;
                    }
                    if (timeOfDay == new TimeSpan(5, 0, 0) || timeOfDay == new TimeSpan(13, 45, 0))
                    {
                        bar.ContainsMarketClose = true;
                    }
                }
                else
                {
                    return false; // 欄位數量不足
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SetNewTick(decimal price)
        {
            if (_floatingBar.IsNullBar)
            {
                _floatingBar.Open = price;
                _floatingBar.High = price;
                _floatingBar.Low = price;
                _floatingBar.Close = price;
                _floatingBar.StartTime = _currentTime;
                _floatingBar.IsNullBar = false;

                // 移除這裡的開盤判斷，在 SealCurrentBar 時統一處理
            }
            else
            {
                if (price > _floatingBar.High) _floatingBar.High = price;
                if (price < _floatingBar.Low) _floatingBar.Low = price;
                _floatingBar.Close = price;
            }

        }

        /// <summary>
        /// 設定成交量基準（用於 DDE Request 初始化）
        /// </summary>
        /// <param name="totalVolume">當日累積總量</param>
        public void SetVolumeCountBase(int totalVolume)
        {
            _lastSealedVolume = totalVolume;
            _currentTotalVolume = totalVolume;
        }

        /// <summary>
        /// 取得當前浮動棒的成交量
        /// </summary>
        public int GetFloatingVolume()
        {
            return _floatingBar.Volume;
        }

        /// <summary>
        /// 設定成交量
        /// </summary>
        /// <param name="volume">成交量</param>
        /// <param name="isBackTestMode">是否為回測模式（預設 false）。
        /// false: 即時模式，volume 為累積總量，自動計算增量後更新浮動棒
        /// true: 回測模式，volume 為單筆 tick 增量，直接累加到浮動棒</param>
        public void SetVolume(int volume, bool isBackTestMode = false)
        {
            if (isBackTestMode)
            {
                // 回測模式：tick 增量，直接累加
                _floatingBar.Volume += volume;
            }
            else
            {
                // 即時模式：累積總量，計算增量
                _currentTotalVolume = volume;

                // 如果當前量比基準量小，表示開新盤或程式重啟，重置基準
                if (volume < _lastSealedVolume)
                {
                    _lastSealedVolume = 0;
                }

                int increment = volume - _lastSealedVolume;
                _floatingBar.Volume = increment;
            }
        }

        // 新增：帶成交量的 Tick，會累加到當前浮動棒
        public void AddTick(decimal price, int volume = 1)
        {
            if (_floatingBar.IsNullBar)
            {
                _floatingBar.Open = price;
                _floatingBar.High = price;
                _floatingBar.Low = price;
                _floatingBar.Close = price;
                _floatingBar.StartTime = _currentTime;
                _floatingBar.IsNullBar = false;
                _floatingBar.Volume = Math.Max(0, volume);
            }
            else
            {
                if (price > _floatingBar.High) _floatingBar.High = price;
                if (price < _floatingBar.Low) _floatingBar.Low = price;
                _floatingBar.Close = price;
                if (volume > 0) _floatingBar.Volume += volume;
            }
        }

        public void SealCurrentBar()
        {
            if (!_tfxRule.IsSealKbar(_currentTime))
            {
                return;
            }

            _floatingBar.CloseTime = _currentTime;

            // 如果是 NullBar，用上一根 K棒的收盤價填充
            if (_floatingBar.IsNullBar && _oneMinuteHistory.Count > 0)
            {
                var lastBar = _oneMinuteHistory[_oneMinuteHistory.Count - 1];
                _floatingBar.Open = lastBar.Close;
                _floatingBar.High = lastBar.Close;
                _floatingBar.Low = lastBar.Close;
                _floatingBar.Close = lastBar.Close;
                _floatingBar.StartTime = lastBar.CloseTime;
                _floatingBar.IsNullBar = false;
            }
            
            // 直接時間比對：開盤K棒判斷
            TimeSpan currentTimeOfDay = _currentTime.TimeOfDay;
            if (currentTimeOfDay == new TimeSpan(8, 46, 0) || currentTimeOfDay == new TimeSpan(15, 1, 0))
            {
                _floatingBar.ContainsMarketOpen = true;
                _floatingBar.IsAlignmentBar = true;
            }
            
            // 直接時間比對：收盤K棒判斷
            if (_currentTime.TimeOfDay == new TimeSpan(5, 0, 0) || _currentTime.TimeOfDay == new TimeSpan(13, 45, 0))
            {
                _floatingBar.ContainsMarketClose = true;
            }

            if (_floatingBar.High > 0 || _floatingBar.Open > 0 || _floatingBar.Close > 0 || _floatingBar.Low > 0)
            {
                // 封棒時將 IsFloating 設為 false
                _floatingBar.IsFloating = false;
                _oneMinuteHistory.Add(_floatingBar);

                // 限制歷史資料數量，避免記憶體無限增長
                if (_oneMinuteHistory.Count > _maxHistoryCount)
                {
                    _oneMinuteHistory.RemoveAt(0);
                }

                OnKbarCompleted?.Invoke(1, _floatingBar);
            }

            // 記住當前總量（用於即時模式的增量計算）
            // 下一根K棒的增量將從這個基準開始計算
            _lastSealedVolume = _currentTotalVolume;

            _floatingBar = new FunctionKBar
            {
                IsNullBar = true,
                IsFloating = true
            };
            
            // 如果是收盤K棒，確保徹底清空 (避免繼承收盤時間)
            if (currentTimeOfDay == new TimeSpan(5, 0, 0) || currentTimeOfDay == new TimeSpan(13, 45, 0))
            {
                _floatingBar = new FunctionKBar
                {
                    IsNullBar = true,
                    IsFloating = true
                };
            }
        }

        protected void AppendOneMinuteBar(FunctionKBar bar)
        {
            if (bar.IsNullBar)
            {
                return;
            }

            bar.IsFloating = false;
            _oneMinuteHistory.Add(bar);

            if (_oneMinuteHistory.Count > _maxHistoryCount)
            {
                _oneMinuteHistory.RemoveAt(0);
            }

            OnKbarCompleted?.Invoke(1, bar);
        }

        public virtual void ClearFloatingBar()
        {
            // 清空浮動K棒，重置為初始狀態
            _floatingBar = new FunctionKBar
            {
                IsNullBar = true,
                IsFloating = true
            };
        }

        protected virtual void ClearAllData()
        {
            // 清空所有K線資料
            _oneMinuteHistory.Clear();
            _floatingBar = new FunctionKBar { IsNullBar = true, IsFloating = true };
        }

    }

    public struct FunctionKBar
    {
        public DateTime StartTime { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public int Volume { get; set; }

        public bool ContainsMarketOpen { get; set; }
        public bool ContainsMarketClose { get; set; }
        public bool IsNullBar { get; set; }
        public bool IsFloating { get; set; }
        public bool IsAlignmentBar { get; set; }
    }
}
