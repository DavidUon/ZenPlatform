using System;
using ZenPlatform.Core;

namespace ZenPlatform.SessionManager.Backtest
{
    public sealed class BacktestEngine : IDisposable
    {
        private readonly SessionManager _manager;
        private readonly Core.KChartBridge _chartBridge;
        private readonly System.Windows.Threading.Dispatcher? _uiDispatcher;
        private bool _initialized;
        private bool _prevAcceptPriceTicks;
        private bool _prevAcceptSecondTicks;
        private bool _prevEnableParallelBacktestTickDispatch;
        private int _prevParallelBacktestTickMaxDegreeOfParallelism;
        private int _prevParallelBacktestTickMinSessionCount;
        private decimal _prevBacktestTickMinDiffToProcess;
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
        private DateTime? _lastEquityBucket;

        public event Action<int>? ProgressChanged;
        public event Action<bool>? Stopped;
        public bool UseBarCloseTimeSignal { get; set; }
        public decimal TickMinDiffToProcess { get; set; } = 1m;
        public BacktestRecorder? Recorder { get; set; }

        public BacktestEngine(SessionManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _chartBridge = new Core.KChartBridge(new KChartCore.BacktestKChartEngine());
            _chartBridge.KBarCompleted += OnKBarCompleted;
            _uiDispatcher = System.Windows.Application.Current?.Dispatcher;
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
            _prevEnableParallelBacktestTickDispatch = _manager.EnableParallelBacktestTickDispatch;
            _prevParallelBacktestTickMaxDegreeOfParallelism = _manager.ParallelBacktestTickMaxDegreeOfParallelism;
            _prevParallelBacktestTickMinSessionCount = _manager.ParallelBacktestTickMinSessionCount;
            _prevBacktestTickMinDiffToProcess = _manager.BacktestTickMinDiffToProcess;
            _prevFetchHistoryBars = _manager.FetchHistoryBars;
            _prevRegisterKBarPeriod = _manager.RegisterKBarPeriod;
            _prevUnregisterKBarPeriod = _manager.UnregisterKBarPeriod;
            _prevIsHistoryReady = _manager.IsHistoryReady;
            _prevLogMaxLines = _manager.Log.MaxLines;
            _manager.PrepareBacktestQuoteIsolation();
            _manager.IsBacktestActive = true;
            _manager.BacktestStatusText = "回測中";
            _manager.BacktestProgressPercent = 0;
            _manager.AcceptPriceTicks = false;
            _manager.AcceptSecondTicks = false;
            _manager.EnableParallelBacktestTickDispatch = false;
            _manager.BacktestTickMinDiffToProcess = TickMinDiffToProcess;
            _manager.Log.SetMaxLines(500);

            _manager.FetchHistoryBars = period => _chartBridge.GetHistoryList(period);
            _manager.RegisterKBarPeriod = period => _chartBridge.RegisterPeriod(period);
            _manager.UnregisterKBarPeriod = period => _chartBridge.UnregisterPeriod(period);
            _manager.IsHistoryReady = () => _chartBridge.HistoryReady;

            _chartBridge.HistoryReady = true;
            _chartBridge.RegisterPeriod(_manager.RuleSet.KbarPeriod);
            _lastEquityBucket = null;

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

            if (period == 1)
            {
                var recorder = Recorder;
                if (recorder != null)
                {
                    try
                    {
                        recorder.AppendBar(bar.CloseTime, 1, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
                        TryAppendEquityCurve(bar.CloseTime);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Backtest is stopping; ignore late writes.
                    }
                    catch (InvalidOperationException)
                    {
                        // Recorder transaction may already be closed during shutdown.
                    }
                }
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

            var kbar = new ZenPlatform.Strategy.KBar(bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
            _manager.OnKBarCompleted(period, kbar);

            // Persist trend side snapshot on 1-minute timeline.
            // For higher-period callbacks (e.g. Auto mode 10m), this upserts
            // the same minute key so the latest side decision wins.
            var trendRecorder = Recorder;
            if (trendRecorder != null)
            {
                try
                {
                    trendRecorder.AppendTrendState(bar.CloseTime, 1, (int)_manager.CurrnetSide);
                }
                catch (ObjectDisposedException)
                {
                    // Backtest is stopping; ignore late writes.
                }
                catch (InvalidOperationException)
                {
                    // Recorder transaction may already be closed during shutdown.
                }
            }
        }

        private void TryAppendEquityCurve(DateTime barCloseTime)
        {
            var recorder = Recorder;
            if (recorder == null)
            {
                return;
            }

            // 10-minute sampling: 00,10,20,30,40,50
            var bucket = new DateTime(
                barCloseTime.Year,
                barCloseTime.Month,
                barCloseTime.Day,
                barCloseTime.Hour,
                (barCloseTime.Minute / 10) * 10,
                0,
                barCloseTime.Kind);

            if (_lastEquityBucket.HasValue && _lastEquityBucket.Value == bucket)
            {
                return;
            }

            decimal totalFloat = 0m;
            decimal totalRealized = 0m;
            foreach (var session in _manager.Sessions)
            {
                totalFloat += session.FloatProfit;
                totalRealized += session.RealizedProfit;
            }

            var totalProfit = totalFloat + totalRealized;
            recorder.AppendEquityCurve(bucket, totalProfit, totalFloat, totalRealized);
            _lastEquityBucket = bucket;
        }

        public void Dispose()
        {
            Recorder = null;
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
            _manager.EnableParallelBacktestTickDispatch = _prevEnableParallelBacktestTickDispatch;
            _manager.ParallelBacktestTickMaxDegreeOfParallelism = _prevParallelBacktestTickMaxDegreeOfParallelism;
            _manager.ParallelBacktestTickMinSessionCount = _prevParallelBacktestTickMinSessionCount;
            _manager.BacktestTickMinDiffToProcess = _prevBacktestTickMinDiffToProcess;
            _manager.Log.SetMaxLines(_prevLogMaxLines > 0 ? _prevLogMaxLines : 5000);
            _drainTcs.TrySetCanceled();
        }

        private void NotifyStopped(bool canceled)
        {
            if (System.Threading.Interlocked.Exchange(ref _stopNotified, 1) == 1)
            {
                return;
            }

            void Raise()
            {
                _manager.ClearBacktestQuoteIsolation();
                _manager.IsBacktestActive = false;
                _manager.BacktestStatusText = string.Empty;
                _manager.BacktestProgressPercent = 0;
                Stopped?.Invoke(canceled);
                _manager.RaiseBacktestStopped(canceled);
            }

            var dispatcher = _uiDispatcher ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(Raise));
                return;
            }

            Raise();
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
            catch (Exception ex)
            {
                AddLogSafe($"BacktestEngine 消費執行緒例外:\n{ex}");
                NotifyStopped(canceled: true);
            }
        }

        private void AddLogSafe(string text)
        {
            var dispatcher = _uiDispatcher ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => _manager.Log.Add(text)));
                return;
            }

            _manager.Log.Add(text);
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
