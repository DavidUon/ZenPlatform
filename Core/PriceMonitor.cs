using System;

namespace ZenPlatform.Core
{
    public sealed class PriceMonitor
    {
        public sealed record PriceStallInfo(
            DateTime LastChangeAt,
            DateTime? LastDdeAt,
            DateTime? LastNetworkAt,
            PriceManager.PriceSource CurrentSource,
            PriceManager.PriceMode Mode);

        private readonly TimeCtrl _timeCtrl;
        private readonly CoreEventHub _eventHub;
        private readonly PriceManager _priceManager;
        private readonly Func<(string Product, int Year, int Month)> _contractProvider;
        private readonly Func<bool> _isMarketOpen;
        private readonly Func<bool> _isDdeConnected;
        private readonly Action _requestResubscribe;
        private readonly TimeSpan _stallThreshold = TimeSpan.FromMinutes(5);

        private string? _lastBid;
        private string? _lastAsk;
        private string? _lastLast;
        private bool _hasBid;
        private bool _hasAsk;
        private bool _hasLast;
        private DateTime? _lastChangeAt;
        private DateTime? _lastDdeAt;
        private DateTime? _lastNetworkAt;
        private bool _stalledRaised;
        private bool _autoSwitchRaised;
        private bool _ddeDisconnectPending;
        private DateTime? _ddeDisconnectedAt;

        public event Action<PriceStallInfo>? PriceStalled;
        public event Action<PriceStallInfo>? AutoSwitchedToNetwork;
        public event Action? PriceFlowResumed;

        public PriceMonitor(
            TimeCtrl timeCtrl,
            CoreEventHub eventHub,
            PriceManager priceManager,
            Func<(string Product, int Year, int Month)> contractProvider,
            Func<bool> isMarketOpen,
            Func<bool> isDdeConnected,
            Action requestResubscribe)
        {
            _timeCtrl = timeCtrl ?? throw new ArgumentNullException(nameof(timeCtrl));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _priceManager = priceManager ?? throw new ArgumentNullException(nameof(priceManager));
            _contractProvider = contractProvider ?? throw new ArgumentNullException(nameof(contractProvider));
            _isMarketOpen = isMarketOpen ?? throw new ArgumentNullException(nameof(isMarketOpen));
            _isDdeConnected = isDdeConnected ?? throw new ArgumentNullException(nameof(isDdeConnected));
            _requestResubscribe = requestResubscribe ?? throw new ArgumentNullException(nameof(requestResubscribe));

            _eventHub.ItemReceived += OnItemReceived;
            _timeCtrl.TimeTick += OnTimeTick;
        }

        private void OnItemReceived(object? sender, CoreQueueItem item)
        {
            if (item.Type != CoreQueueType.PriceUpdate || item.Quote == null)
            {
                return;
            }

            var quote = item.Quote;
            var (product, year, month) = _contractProvider();
            if (!string.Equals(quote.Product, product, StringComparison.Ordinal) ||
                quote.Year != year ||
                quote.Month != month)
            {
                return;
            }

            if (quote.Source == QuoteSource.Dde)
            {
                _lastDdeAt = _timeCtrl.ExchangeTime;
            }
            else if (quote.Source == QuoteSource.Network)
            {
                _lastNetworkAt = _timeCtrl.ExchangeTime;
            }

            switch (quote.Field)
            {
                case QuoteField.Bid:
                    UpdateField(ref _lastBid, ref _hasBid, quote.Value);
                    break;
                case QuoteField.Ask:
                    UpdateField(ref _lastAsk, ref _hasAsk, quote.Value);
                    break;
                case QuoteField.Last:
                    UpdateField(ref _lastLast, ref _hasLast, quote.Value);
                    break;
            }
        }

        private void UpdateField(ref string? lastValue, ref bool hasValue, string value)
        {
            if (!hasValue || !string.Equals(lastValue, value, StringComparison.Ordinal))
            {
                lastValue = value;
                hasValue = true;
                _lastChangeAt = _timeCtrl.ExchangeTime;
                _stalledRaised = false;
                _autoSwitchRaised = false;
                if (_isDdeConnected())
                {
                    _priceManager.SetTemporaryNetworkFallback(false);
                    _ddeDisconnectPending = false;
                    _ddeDisconnectedAt = null;
                }
                PriceFlowResumed?.Invoke();
            }
        }

        public void HandleDdeDisconnected()
        {
            if (_autoSwitchRaised)
            {
                return;
            }

            _ddeDisconnectPending = true;
            _ddeDisconnectedAt = _timeCtrl.ExchangeTime;
            _priceManager.SetTemporaryNetworkFallback(true);
            _requestResubscribe();
        }

        private void OnTimeTick(object? sender, DateTime time)
        {
            if (!_isMarketOpen())
            {
                return;
            }

            if (!_hasBid || !_hasAsk || !_hasLast || _lastChangeAt == null)
            {
                return;
            }

            var now = _timeCtrl.ExchangeTime;
            if (_ddeDisconnectPending)
            {
                if (_isDdeConnected())
                {
                    _ddeDisconnectPending = false;
                    _ddeDisconnectedAt = null;
                    _priceManager.SetTemporaryNetworkFallback(false);
                }
                else if (_ddeDisconnectedAt.HasValue && (now - _ddeDisconnectedAt.Value) >= _stallThreshold && !_autoSwitchRaised)
                {
                    _autoSwitchRaised = true;
                    _ddeDisconnectPending = false;
                    AutoSwitchedToNetwork?.Invoke(new PriceStallInfo(
                        _lastChangeAt.Value,
                        _lastDdeAt,
                        _lastNetworkAt,
                        _priceManager.CurrentSource,
                        _priceManager.Mode));
                }
            }

            if (now - _lastChangeAt.Value < _stallThreshold)
            {
                return;
            }

            if (_priceManager.CurrentSource == PriceManager.PriceSource.Dde)
            {
                if (!_autoSwitchRaised)
                {
                    _autoSwitchRaised = true;
                    _priceManager.SetTemporaryNetworkFallback(true);
                    _requestResubscribe();
                    AutoSwitchedToNetwork?.Invoke(new PriceStallInfo(
                        _lastChangeAt.Value,
                        _lastDdeAt,
                        _lastNetworkAt,
                        _priceManager.CurrentSource,
                        _priceManager.Mode));
                    _lastChangeAt = now;
                    _stalledRaised = false;
                }
                return;
            }

            if (_stalledRaised)
            {
                return;
            }

            _stalledRaised = true;
            if (_priceManager.CurrentSource == PriceManager.PriceSource.Network)
            {
                _requestResubscribe();
            }
            PriceStalled?.Invoke(new PriceStallInfo(
                _lastChangeAt.Value,
                _lastDdeAt,
                _lastNetworkAt,
                _priceManager.CurrentSource,
                _priceManager.Mode));
        }
    }
}
