using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ZenPlatform.DdePrice;

namespace ZenPlatform.Core
{
    public sealed class DdeService
    {
        private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(1);
        private static readonly int HealthCheckFailureThreshold = 5;
        private static readonly int[] HealthCheckItems = { 101, 102, 125, 143 }; // 買價/賣價/成交價/時間

        private readonly DdePrice.DdePrice _dde;
        private IntPtr _conversation = IntPtr.Zero;
        private string? _subscribedKey;
        private string? _subscribedProduct;
        private int _subscribedYear;
        private int _subscribedMonth;
        private string? _subscribedSymbol;
        private readonly Timer _healthTimer;
        private int _healthRunning;
        private int _consecutiveHealthCheckFailures;
        private DateTime _lastConnectFailureAt = DateTime.MinValue;

        public DdeService(DdePrice.DdePrice dde)
        {
            _dde = dde ?? throw new ArgumentNullException(nameof(dde));
            _dde.ConnectionLost += OnConnectionLost;
            _healthTimer = new Timer(_ => HealthCheckTick(), null, HealthCheckInterval, HealthCheckInterval);
        }

        public IntPtr Conversation => _conversation;
        public bool IsConnected => _conversation != IntPtr.Zero;

        public event Action<IntPtr>? ConnectionLost;
        public event Action<string>? HealthCheckLog;
        public event Action<string>? HealthCheckRecovered;

        public Task TryConnectAsync(string service, string topic)
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_conversation != IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    _dde.Initialize();
                    _conversation = _dde.Connect(service, topic);
                    _lastConnectFailureAt = DateTime.MinValue;
                }
                catch (DdeException)
                {
                    _conversation = IntPtr.Zero;
                    _lastConnectFailureAt = DateTime.Now;
                }
            }).Task;
        }

        public void Subscribe(string product, int year, int month)
        {
            if (_conversation == IntPtr.Zero)
            {
                return;
            }

            var key = $"{product}_{year:D4}_{month:D2}";
            if (_subscribedKey == key)
            {
                return;
            }

            if (!DdeItemCatalog.TryGetProduct(product, out var ddeProduct))
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                _dde.SetActiveSubscription(ddeProduct.DisplayName, year, month);
                _dde.ResetQuoteStatus();
                foreach (var item in DdeItemCatalog.DefaultPriceItems)
                {
                    var ddeItem = DdeItemCatalog.BuildItem(ddeProduct.DdeSymbol, year, month, item);
                    _dde.TryAddHotLink(_conversation, ddeItem, out _);
                    try
                    {
                        // 首次訂閱立即 request 一次，讓 UI 先有可顯示的快照資料。
                        _dde.Request(_conversation, ddeItem);
                    }
                    catch
                    {
                        // Ignore per-item request failures; hotlink updates will continue.
                    }
                }

                _subscribedKey = key;
                _subscribedProduct = product;
                _subscribedYear = year;
                _subscribedMonth = month;
                _subscribedSymbol = ddeProduct.DdeSymbol;
            });
        }

        public void Reset()
        {
            _subscribedKey = null;
            _subscribedProduct = null;
            _subscribedYear = 0;
            _subscribedMonth = 0;
            _subscribedSymbol = null;
            _conversation = IntPtr.Zero;
            _consecutiveHealthCheckFailures = 0;
            _dde.ResetQuoteStatus();
        }

        private void OnConnectionLost(IntPtr conv)
        {
            if (_conversation == conv)
            {
                Reset();
                ConnectionLost?.Invoke(conv);
            }
        }

        private void HealthCheckTick()
        {
            if (Interlocked.Exchange(ref _healthRunning, 1) == 1)
            {
                return;
            }

            try
            {
                if (_conversation == IntPtr.Zero || string.IsNullOrWhiteSpace(_subscribedSymbol))
                {
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_conversation == IntPtr.Zero || string.IsNullOrWhiteSpace(_subscribedSymbol))
                    {
                        return;
                    }

                    var anySuccess = false;
                    foreach (var item in HealthCheckItems)
                    {
                        var ddeItem = DdeItemCatalog.BuildItem(_subscribedSymbol, _subscribedYear, _subscribedMonth, item);
                        try
                        {
                            _dde.Request(_conversation, ddeItem);
                            anySuccess = true;
                        }
                        catch (Exception ex)
                        {
                            _dde.RemoveHotLink(_conversation, ddeItem);
                            var ok = _dde.TryAddHotLink(_conversation, ddeItem, out var error);
                            if (!ok)
                            {
                                var result = $"失敗({error})";
                                HealthCheckLog?.Invoke($"[DDE]檢查失敗: {item} ({ex.Message})");
                                HealthCheckLog?.Invoke($"[DDE]嘗試重新連結:{item}{result}");
                            }
                        }
                    }

                    if (anySuccess)
                    {
                        if (_consecutiveHealthCheckFailures > 0 && !string.IsNullOrWhiteSpace(_subscribedKey))
                        {
                            HealthCheckRecovered?.Invoke(_subscribedKey);
                        }

                        _consecutiveHealthCheckFailures = 0;
                        return;
                    }

                    _consecutiveHealthCheckFailures++;
                    HealthCheckLog?.Invoke($"[DDE]關鍵欄位 request 失敗，連續 {_consecutiveHealthCheckFailures}/{HealthCheckFailureThreshold} 次。");
                    if (_consecutiveHealthCheckFailures < HealthCheckFailureThreshold)
                    {
                        return;
                    }

                    var conv = _conversation;
                    HealthCheckLog?.Invoke("[DDE]連續 5 次 request 失敗，判定 DDE 斷線。");
                    Reset();
                    ConnectionLost?.Invoke(conv);
                });
            }
            finally
            {
                Interlocked.Exchange(ref _healthRunning, 0);
            }
        }
    }
}
