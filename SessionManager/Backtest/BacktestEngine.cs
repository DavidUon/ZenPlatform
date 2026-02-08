using System;
using ZenPlatform.Core;
using ZenPlatform.Debug;

namespace ZenPlatform.SessionManager.Backtest
{
    public sealed class BacktestEngine : IDisposable
    {
        private readonly SessionManager _manager;
        private readonly Core.KChartBridge _chartBridge;
        private bool _initialized;
        private bool _prevAcceptPriceTicks;
        private bool _prevAcceptSecondTicks;
        private Func<int, System.Collections.Generic.List<KChartCore.FunctionKBar>>? _prevFetchHistoryBars;
        private Action<int>? _prevRegisterKBarPeriod;
        private Action<int>? _prevUnregisterKBarPeriod;
        private Func<bool>? _prevIsHistoryReady;
        private int _prevLogMaxLines;
        private readonly System.Collections.Concurrent.ConcurrentQueue<BacktestEvent> _queue = new();
        private readonly System.Threading.SemaphoreSlim _queueSignal = new(0);
        private readonly System.Threading.CancellationTokenSource _cts = new();
        private System.Threading.Tasks.Task? _consumerTask;
        private readonly System.Threading.Tasks.TaskCompletionSource<bool> _drainTcs = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _productionCompleted;
        private long _totalUnits;
        private long _processedUnits;
        private int _lastPercent = -1;
        private int _stopNotified;

        public event Action<int>? ProgressChanged;
        public event Action<bool>? Stopped;
        public bool UseBarCloseTimeSignal { get; set; }

        public BacktestEngine(SessionManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _chartBridge = new Core.KChartBridge(new KChartCore.BacktestKChartEngine());
            _chartBridge.KBarCompleted += OnKBarCompleted;
        }

        public SessionManager Manager => _manager;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _prevAcceptPriceTicks = _manager.AcceptPriceTicks;
            _prevAcceptSecondTicks = _manager.AcceptSecondTicks;
            _prevFetchHistoryBars = _manager.FetchHistoryBars;
            _prevRegisterKBarPeriod = _manager.RegisterKBarPeriod;
            _prevUnregisterKBarPeriod = _manager.UnregisterKBarPeriod;
            _prevIsHistoryReady = _manager.IsHistoryReady;
            _prevLogMaxLines = _manager.Log.MaxLines;
            _manager.IsBacktestActive = true;
            _manager.BacktestStatusText = "回測中";
            _manager.BacktestProgressPercent = 0;
            _manager.AcceptPriceTicks = false;
            _manager.AcceptSecondTicks = false;
            _manager.Log.SetMaxLines(500);

            _manager.FetchHistoryBars = period => _chartBridge.GetHistoryList(period);
            _manager.RegisterKBarPeriod = period => _chartBridge.RegisterPeriod(period);
            _manager.UnregisterKBarPeriod = period => _chartBridge.UnregisterPeriod(period);
            _manager.IsHistoryReady = () => _chartBridge.HistoryReady;

            _chartBridge.HistoryReady = true;
            _chartBridge.RegisterPeriod(_manager.RuleSet.KbarPeriod);

            _consumerTask = System.Threading.Tasks.Task.Run(() => ConsumeAsync(_cts.Token));
        }

        public void SetTotalUnits(long totalUnits)
        {
            _totalUnits = totalUnits < 0 ? 0 : totalUnits;
            _processedUnits = 0;
            _lastPercent = -1;
            ProgressChanged?.Invoke(0);
        }

        public void EnqueueProgressUnit()
        {
            _queue.Enqueue(BacktestEvent.FromProgress());
            _queueSignal.Release();
        }

        public void CompleteProduction()
        {
            _productionCompleted = true;
            _queueSignal.Release();
        }

        public System.Threading.Tasks.Task WaitForDrainAsync(System.Threading.CancellationToken token)
        {
            if (_productionCompleted && _queue.IsEmpty)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            return _drainTcs.Task.WaitAsync(token);
        }

        public void FeedTime(DateTime time)
        {
            _manager.OnSecond(time);
            _chartBridge.Handle(CoreQueueItem.FromTime(time));
        }

        public void FeedTick(QuoteUpdate quote)
        {
            _manager.OnTick(quote);
            _chartBridge.Handle(CoreQueueItem.FromQuote(quote));
        }

        public void FeedBar(KChartCore.FunctionKBar bar)
        {
            _chartBridge.AddOneMinuteBar(bar);
        }

        public void EnqueueTime(DateTime time)
        {
            _queue.Enqueue(BacktestEvent.FromTime(time));
            _queueSignal.Release();
        }

        public void EnqueueTick(QuoteUpdate quote)
        {
            _queue.Enqueue(BacktestEvent.FromQuote(quote));
            _queueSignal.Release();
        }

        public void EnqueueBar(KChartCore.FunctionKBar bar)
        {
            _queue.Enqueue(BacktestEvent.FromBar(bar));
            _queueSignal.Release();
        }

        public int QueueCount => _queue.Count;

        public void WaitForQueueBelow(int threshold, System.Threading.CancellationToken token)
        {
            while (!token.IsCancellationRequested && _queue.Count >= threshold)
            {
                System.Threading.Thread.Sleep(1);
            }
        }

        private void OnKBarCompleted(int period, KChartCore.FunctionKBar bar)
        {
            if (bar.IsNullBar || bar.IsFloating || bar.IsAlignmentBar)
            {
                return;
            }

            if (UseBarCloseTimeSignal)
            {
                _manager.OnSecond(bar.CloseTime);
            }

            if (!_manager.IsStrategyRunning)
            {
                return;
            }

            if (!_manager.IsKBarPeriodRegistered(period))
            {
                return;
            }

            if (_manager.RuleSet.KbarPeriod == period)
            {
                var closeStamp = new DateTime(bar.CloseTime.Year, bar.CloseTime.Month, bar.CloseTime.Day,
                    bar.CloseTime.Hour, bar.CloseTime.Minute, 0, DateTimeKind.Unspecified);
                if (_manager.LastIndicatorBarCloseTime.HasValue &&
                    closeStamp <= _manager.LastIndicatorBarCloseTime.Value)
                {
                    return;
                }

                if (_manager.Indicators != null)
                {
                    lock (_manager.IndicatorSync)
                    {
                        _manager.Indicators.Update(new ZenPlatform.Indicators.KBar(bar.StartTime, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
                        _manager.LastIndicatorBarCloseTime = closeStamp;

                        if (_manager.IndicatorsReadyForLog && !_manager.SuppressIndicatorLog)
                        {
                        }
                    }
                }
                else
                {
                    _manager.LastIndicatorBarCloseTime = closeStamp;
                }
            }

            _manager.OnKBarCompleted(period, bar);
        }

        public void Dispose()
        {
            NotifyStopped(canceled: true);
            _cts.Cancel();
            try
            {
                _consumerTask?.Wait(2000);
            }
            catch
            {
                // ignore shutdown exceptions
            }
            _chartBridge.KBarCompleted -= OnKBarCompleted;
            _manager.FetchHistoryBars = _prevFetchHistoryBars;
            _manager.RegisterKBarPeriod = _prevRegisterKBarPeriod;
            _manager.UnregisterKBarPeriod = _prevUnregisterKBarPeriod;
            _manager.IsHistoryReady = _prevIsHistoryReady;
            _manager.AcceptPriceTicks = _prevAcceptPriceTicks;
            _manager.AcceptSecondTicks = _prevAcceptSecondTicks;
            _manager.Log.SetMaxLines(_prevLogMaxLines > 0 ? _prevLogMaxLines : 5000);
            _drainTcs.TrySetCanceled();
        }

        private void NotifyStopped(bool canceled)
        {
            if (System.Threading.Interlocked.Exchange(ref _stopNotified, 1) == 1)
            {
                return;
            }

            _manager.IsBacktestActive = false;
            _manager.BacktestStatusText = string.Empty;
            _manager.BacktestProgressPercent = 0;
            Stopped?.Invoke(canceled);
            _manager.RaiseBacktestStopped(canceled);
        }

        private async System.Threading.Tasks.Task ConsumeAsync(System.Threading.CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await _queueSignal.WaitAsync(token);
                    while (_queue.TryDequeue(out var item))
                    {
                        if (item.Type == BacktestEventType.TimeSignal)
                        {
                            FeedTime(item.Time);
                        }
                        else if (item.Type == BacktestEventType.PriceUpdate && item.Quote != null)
                        {
                            FeedTick(item.Quote);
                        }
                        else if (item.Type == BacktestEventType.KBar1m && item.Bar.HasValue)
                        {
                            FeedBar(item.Bar.Value);
                        }
                        else if (item.Type == BacktestEventType.Progress)
                        {
                            var total = _totalUnits;
                            if (total > 0)
                            {
                                var processed = System.Threading.Interlocked.Increment(ref _processedUnits);
                                var percent = (int)System.Math.Min(100, System.Math.Max(0, (processed * 100) / total));
                                if (percent != _lastPercent)
                                {
                                    _lastPercent = percent;
                                    ProgressChanged?.Invoke(percent);
                                }
                            }
                        }
                    }

                    if (_productionCompleted && _queue.IsEmpty)
                    {
                        _drainTcs.TrySetResult(true);
                        NotifyStopped(canceled: false);
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
                NotifyStopped(canceled: true);
            }
        }

        private enum BacktestEventType
        {
            TimeSignal,
            PriceUpdate,
            KBar1m,
            Progress
        }

        private sealed class BacktestEvent
        {
            private BacktestEvent(BacktestEventType type, DateTime time, QuoteUpdate? quote, KChartCore.FunctionKBar? bar)
            {
                Type = type;
                Time = time;
                Quote = quote;
                Bar = bar;
            }

            public BacktestEventType Type { get; }
            public DateTime Time { get; }
            public QuoteUpdate? Quote { get; }
            public KChartCore.FunctionKBar? Bar { get; }

            public static BacktestEvent FromTime(DateTime time)
            {
                return new BacktestEvent(BacktestEventType.TimeSignal, time, null, null);
            }

            public static BacktestEvent FromQuote(QuoteUpdate quote)
            {
                return new BacktestEvent(BacktestEventType.PriceUpdate, quote.Time, quote, null);
            }

            public static BacktestEvent FromBar(KChartCore.FunctionKBar bar)
            {
                return new BacktestEvent(BacktestEventType.KBar1m, bar.CloseTime, null, bar);
            }

            public static BacktestEvent FromProgress()
            {
                return new BacktestEvent(BacktestEventType.Progress, DateTime.MinValue, null, null);
            }
        }
    }
}
