using System;

namespace ZenPlatform.DdePrice
{
    public class DdePrice
    {
        private readonly DdeCtrl _dde = new();
        private string _activeProduct = "";
        private int _activeYear;
        private int _activeMonth;
        private bool _hasActiveSubscription;
        private bool _isQuoteAvailable;
        private bool _quoteStatusLocked;

        public event Action<string, string, int, int, string, bool>? OnDdePrice;
        public event Action<bool>? QuoteStatusChanged;

        public bool IsQuoteAvailable => _isQuoteAvailable;

        public event Action<IntPtr, string, string>? DataUpdate
        {
            add => _dde.DataUpdate += value;
            remove => _dde.DataUpdate -= value;
        }

        public event Action<IntPtr>? ConnectionConfirmed
        {
            add => _dde.ConnectionConfirmed += value;
            remove => _dde.ConnectionConfirmed -= value;
        }

        public event Action<IntPtr>? ConnectionLost
        {
            add => _dde.ConnectionLost += value;
            remove => _dde.ConnectionLost -= value;
        }

        public IntPtr Initialize() => _dde.Initialize();

        public IntPtr Connect(string service, string topic) => _dde.Connect(service, topic);

        public void Disconnect(IntPtr hConv) => _dde.Disconnect(hConv);

        public string Request(IntPtr hConv, string item, uint timeout = DdeApi.TIMEOUT_SYNC)
        {
            var data = _dde.Request(hConv, item, timeout);
            RaiseDdePrice(item, data, true);
            return data;
        }

        public IntPtr AddHotLink(IntPtr hConv, string item, uint fmt = DdeApi.CF_TEXT) =>
            _dde.AddHotLink(hConv, item, fmt);

        public bool RemoveHotLink(IntPtr hConv, string item, uint fmt = DdeApi.CF_TEXT) =>
            _dde.RemoveHotLink(hConv, item, fmt);

        public bool TryAddHotLink(IntPtr hConv, string item, out string error, uint fmt = DdeApi.CF_TEXT)
        {
            error = "";
            try
            {
                _dde.AddHotLink(hConv, item, fmt);
                if (!_quoteStatusLocked)
                {
                    SetQuoteStatus(true);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!_quoteStatusLocked)
                {
                    SetQuoteStatus(false);
                    _quoteStatusLocked = true;
                }
                error = ex.Message;
                return false;
            }
        }

        public void SetActiveSubscription(string productName, int year, int month)
        {
            _activeProduct = productName;
            _activeYear = year;
            _activeMonth = month;
            _hasActiveSubscription = true;
        }

        public void ResetQuoteStatus()
        {
            _quoteStatusLocked = false;
            SetQuoteStatus(false);
        }

        public DdePrice()
        {
            _dde.DataUpdate += OnDdeDataUpdate;
        }

        private void OnDdeDataUpdate(IntPtr hConv, string item, string data)
        {
            RaiseDdePrice(item, data, false);
        }

        private void RaiseDdePrice(string item, string data, bool isRequest)
        {
            if (!DdeItemCatalog.TryParseDdeItem(item, out var productName, out var year, out var month, out var fieldName))
            {
                return;
            }

            if (_hasActiveSubscription &&
                (!string.Equals(productName, _activeProduct, StringComparison.OrdinalIgnoreCase) ||
                 year != _activeYear ||
                 month != _activeMonth))
            {
                return;
            }

            if (fieldName == "時間")
            {
                data = NormalizeTime(data);
            }
            else if (fieldName == "漲跌幅")
            {
                data = NormalizePercent(data);
            }

            OnDdePrice?.Invoke(productName, fieldName, year, month, data, isRequest);
        }

        private void SetQuoteStatus(bool value)
        {
            if (_isQuoteAvailable == value)
            {
                return;
            }

            _isQuoteAvailable = value;
            QuoteStatusChanged?.Invoke(value);
        }

        private static string NormalizeTime(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return data;
            }

            var raw = data.Trim();
            if (raw.Length != 6)
            {
                return data;
            }

            if (!int.TryParse(raw, out var value))
            {
                return data;
            }

            var hh = value / 10000;
            var mm = (value / 100) % 100;
            var ss = value % 100;
            if (hh < 0 || hh > 23 || mm < 0 || mm > 59 || ss < 0 || ss > 59)
            {
                return data;
            }

            return $"{hh:D2}:{mm:D2}:{ss:D2}";
        }

        private static string NormalizePercent(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return data;
            }

            if (!double.TryParse(data.Trim(), out var value))
            {
                return data;
            }

            return $"{value:0.00}%";
        }
    }
}
