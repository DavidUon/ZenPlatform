using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ZenPlatform.DdePrice;

namespace ZenPlatform.Core
{
    public sealed class DdeService
    {
        private readonly DdePrice.DdePrice _dde;
        private IntPtr _conversation = IntPtr.Zero;
        private string? _subscribedKey;
        private string? _subscribedProduct;
        private int _subscribedYear;
        private int _subscribedMonth;
        private string? _subscribedSymbol;
        private readonly Timer _healthTimer;
        private int _healthRunning;

        public DdeService(DdePrice.DdePrice dde)
        {
            _dde = dde ?? throw new ArgumentNullException(nameof(dde));
            _dde.ConnectionLost += OnConnectionLost;
            _healthTimer = new Timer(_ => HealthCheckTick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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

                _dde.Initialize();
                _conversation = _dde.Connect(service, topic);
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
                    foreach (var item in DdeItemCatalog.DefaultPriceItems)
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
                            var result = ok ? "成功" : $"失敗({error})";
                            HealthCheckLog?.Invoke($"[DDE]檢查失敗: {item} ({ex.Message})");
                            HealthCheckLog?.Invoke($"[DDE]嘗試重新連結:{item}{result}");
                        }
                    }

                    if (anySuccess && !string.IsNullOrWhiteSpace(_subscribedKey))
                    {
                        HealthCheckRecovered?.Invoke(_subscribedKey);
                    }
                });
            }
            finally
            {
                Interlocked.Exchange(ref _healthRunning, 0);
            }
        }
    }
}
