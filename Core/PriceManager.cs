using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZenPlatform.Core
{
    public sealed class PriceManager
    {
        public sealed record QuoteHealth(
            bool DdeAvailable,
            bool NetworkAvailable,
            DateTime? LastDdeTickUtc,
            DateTime? LastNetworkTickUtc,
            string Reason);

        public enum PriceSource
        {
            None,
            Dde,
            Network
        }

        public enum PriceMode
        {
            Auto,
            ManualDde,
            ManualNetwork
        }

        private readonly ZClient.ZClient _client;
        private readonly DdeService _ddeService;
        private readonly SemaphoreSlim _networkSubscribeLock = new(1, 1);
        private PriceSource _priceSource = PriceSource.None;
        private PriceMode _mode = PriceMode.Auto;
        private string? _networkProduct;
        private int _networkYear;
        private int _networkMonth;
        private bool _temporaryNetworkFallback;
        private DateTime? _lastDdeTickUtc;
        private DateTime? _lastNetworkTickUtc;
        private QuoteHealth _lastHealth = new(false, false, null, null, "init");
        private PriceSource? _pendingSource;
        private DateTime? _pendingSinceUtc;
        private DateTime? _lastSourceChangedUtc;

        private CancellationTokenSource? _networkRetryCts;
        private string? _networkRetryProduct;
        private int _networkRetryYear;
        private int _networkRetryMonth;
        private int _networkRetryAttempts;

        // Use a longer freshness window so low-liquidity sessions don't flap source state.
        // Health checks should maintain connectivity signal even when there are no trades.
        private static readonly TimeSpan TickStaleThreshold = TimeSpan.FromSeconds(65);
        private static readonly TimeSpan SwitchDebounce = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MinSourceHold = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan NetworkSubscribeRetryInterval = TimeSpan.FromMinutes(5);
        private const int MaxNetworkSubscribeRetryAttempts = 10;

        public PriceManager(ZClient.ZClient client, DdeService ddeService)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _ddeService = ddeService ?? throw new ArgumentNullException(nameof(ddeService));
        }

        public void SetMode(PriceMode mode)
        {
            _mode = mode;
        }

        public PriceMode Mode => _mode;
        public PriceSource CurrentSource => _priceSource;
        public QuoteHealth LastHealth => _lastHealth;
        public event Action<PriceSource, PriceSource>? SourceChanged;
        public event Action<QuoteHealth>? HealthChanged;

        public void SetTemporaryNetworkFallback(bool enabled)
        {
            _temporaryNetworkFallback = enabled;
        }

        public void NotifyDdeTick(DateTime? atUtc = null)
        {
            _lastDdeTickUtc = atUtc ?? DateTime.UtcNow;
        }

        public void NotifyNetworkTick(DateTime? atUtc = null)
        {
            _lastNetworkTickUtc = atUtc ?? DateTime.UtcNow;
        }

        public void ResetNetworkSubscription()
        {
            CancelNetworkRetry();
            _networkProduct = null;
            _networkYear = 0;
            _networkMonth = 0;
        }

        public Task StopAsync()
        {
            _priceSource = PriceSource.None;
            _temporaryNetworkFallback = false;
            _pendingSource = null;
            _pendingSinceUtc = null;
            _lastSourceChangedUtc = null;
            CancelNetworkRetry();
            return UnsubscribeNetworkAsync();
        }

        public void Update(bool brokerConnected, bool ddeConnected, bool loggedIn, bool isMarketOpen, string contract, int year, int month)
        {
            var nowUtc = DateTime.UtcNow;
            var health = EvaluateHealth(nowUtc, brokerConnected, ddeConnected, loggedIn, isMarketOpen);
            if (!Equals(health, _lastHealth))
            {
                _lastHealth = health;
                HealthChanged?.Invoke(health);
            }

            var desiredSource = ResolvePriceSource(health);
            if (desiredSource == _priceSource)
            {
                _pendingSource = null;
                _pendingSinceUtc = null;
            }
            else if (CanSwitchTo(desiredSource, nowUtc))
            {
                ApplySwitch(desiredSource, nowUtc);
            }

            if (_priceSource == PriceSource.Dde)
            {
                _ddeService.Subscribe(contract, year, month);
            }
            else if (_priceSource == PriceSource.Network)
            {
                _ = SubscribeNetworkIfNeededAsync(contract, year, month, loggedIn);
            }
        }

        private QuoteHealth EvaluateHealth(DateTime nowUtc, bool brokerConnected, bool ddeConnected, bool loggedIn, bool isMarketOpen)
        {
            var ddeLinked = ddeConnected;
            var networkLinked = loggedIn;
            // DDE availability should be driven by connection/heartbeat, not trade flow cadence.
            // Low-liquidity periods can have long gaps without ticks while DDE is still healthy.
            var ddeFresh = true;
            var networkFresh = !isMarketOpen || !_lastNetworkTickUtc.HasValue || (nowUtc - _lastNetworkTickUtc.Value) <= TickStaleThreshold;

            var ddeAvailable = ddeLinked && ddeFresh;
            var networkAvailable = networkLinked && networkFresh;

            var reason = "ok";
            if (!ddeLinked && !networkLinked) reason = "no_dde_and_no_network_link";
            else if (!ddeLinked) reason = "dde_link_down";
            else if (!ddeFresh) reason = "dde_stale";
            else if (!networkLinked) reason = "network_not_logged_in";
            else if (!networkFresh) reason = "network_stale";

            return new QuoteHealth(ddeAvailable, networkAvailable, _lastDdeTickUtc, _lastNetworkTickUtc, reason);
        }

        private PriceSource ResolvePriceSource(QuoteHealth health)
        {
            return _mode switch
            {
                PriceMode.ManualDde => health.DdeAvailable ? PriceSource.Dde : (health.NetworkAvailable ? PriceSource.Network : PriceSource.None),
                PriceMode.ManualNetwork => health.NetworkAvailable ? PriceSource.Network : (health.DdeAvailable ? PriceSource.Dde : PriceSource.None),
                _ => ResolveAutoSource(health)
            };
        }

        private PriceSource ResolveAutoSource(QuoteHealth health)
        {
            if (_temporaryNetworkFallback && health.NetworkAvailable)
                return PriceSource.Network;

            if (health.DdeAvailable)
                return PriceSource.Dde;

            if (health.NetworkAvailable)
                return PriceSource.Network;

            return PriceSource.None;
        }

        private bool CanSwitchTo(PriceSource desiredSource, DateTime nowUtc)
        {
            if (_pendingSource != desiredSource)
            {
                _pendingSource = desiredSource;
                _pendingSinceUtc = nowUtc;
                return false;
            }

            if (!_pendingSinceUtc.HasValue || (nowUtc - _pendingSinceUtc.Value) < SwitchDebounce)
                return false;

            if (_lastSourceChangedUtc.HasValue &&
                _priceSource != PriceSource.None &&
                desiredSource != _priceSource &&
                (nowUtc - _lastSourceChangedUtc.Value) < MinSourceHold)
            {
                return false;
            }

            return true;
        }

        private void ApplySwitch(PriceSource desiredSource, DateTime nowUtc)
        {
            var prev = _priceSource;
            if (_priceSource == PriceSource.Network)
            {
                _ = UnsubscribeNetworkAsync();
            }
            if (desiredSource != PriceSource.Network)
            {
                CancelNetworkRetry();
            }

            _priceSource = desiredSource;
            _lastSourceChangedUtc = nowUtc;
            _pendingSource = null;
            _pendingSinceUtc = null;
            SourceChanged?.Invoke(prev, _priceSource);
        }

        private async Task SubscribeNetworkIfNeededAsync(string contract, int year, int month, bool loggedIn)
        {
            if (!loggedIn)
            {
                CancelNetworkRetry();
                return;
            }

            if (string.Equals(_networkProduct, contract, StringComparison.Ordinal) &&
                _networkYear == year &&
                _networkMonth == month)
            {
                CancelNetworkRetryIfDifferent(contract, year, month);
                return;
            }

            if (IsRetryScheduledFor(contract, year, month))
            {
                return;
            }

            var success = await TrySubscribeNetworkAsync(contract, year, month).ConfigureAwait(false);
            if (!success)
            {
                StartNetworkRetry(contract, year, month);
            }
        }

        private async Task<bool> TrySubscribeNetworkAsync(string contract, int year, int month)
        {
            await _networkSubscribeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_client.IsLoggedIn)
                {
                    return false;
                }

                if (string.Equals(_networkProduct, contract, StringComparison.Ordinal) &&
                    _networkYear == year &&
                    _networkMonth == month)
                {
                    return true;
                }

                await UnsubscribeNetworkAsyncCore().ConfigureAwait(false);

                var snapshot = await _client.SubscribePrice(contract, year, month).ConfigureAwait(false);
                if (snapshot == null)
                {
                    return false;
                }

                _networkProduct = contract;
                _networkYear = year;
                _networkMonth = month;
                return true;
            }
            finally
            {
                _networkSubscribeLock.Release();
            }
        }

        private void StartNetworkRetry(string contract, int year, int month)
        {
            if (IsRetryScheduledFor(contract, year, month))
            {
                return;
            }

            CancelNetworkRetry();

            var cts = new CancellationTokenSource();
            _networkRetryCts = cts;
            _networkRetryProduct = contract;
            _networkRetryYear = year;
            _networkRetryMonth = month;
            _networkRetryAttempts = 0;
            _ = Task.Run(() => RetrySubscribeLoopAsync(cts.Token));
        }

        private async Task RetrySubscribeLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_networkRetryAttempts >= MaxNetworkSubscribeRetryAttempts)
                {
                    CancelNetworkRetry();
                    return;
                }

                try
                {
                    await Task.Delay(NetworkSubscribeRetryInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                var product = _networkRetryProduct;
                var year = _networkRetryYear;
                var month = _networkRetryMonth;
                if (string.IsNullOrWhiteSpace(product))
                {
                    return;
                }

                _networkRetryAttempts++;

                var success = await TrySubscribeNetworkAsync(product, year, month).ConfigureAwait(false);
                if (success)
                {
                    CancelNetworkRetry();
                    return;
                }
            }
        }

        private bool IsRetryScheduledFor(string contract, int year, int month)
        {
            var cts = _networkRetryCts;
            return cts != null &&
                !cts.IsCancellationRequested &&
                string.Equals(_networkRetryProduct, contract, StringComparison.Ordinal) &&
                _networkRetryYear == year &&
                _networkRetryMonth == month;
        }

        private void CancelNetworkRetryIfDifferent(string contract, int year, int month)
        {
            if (_networkRetryCts == null || _networkRetryCts.IsCancellationRequested)
            {
                return;
            }

            if (!string.Equals(_networkRetryProduct, contract, StringComparison.Ordinal) ||
                _networkRetryYear != year ||
                _networkRetryMonth != month)
            {
                CancelNetworkRetry();
            }
        }

        private void CancelNetworkRetry()
        {
            _networkRetryCts?.Cancel();
            _networkRetryCts = null;
            _networkRetryProduct = null;
            _networkRetryYear = 0;
            _networkRetryMonth = 0;
            _networkRetryAttempts = 0;
        }

        private async Task UnsubscribeNetworkAsync()
        {
            await _networkSubscribeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await UnsubscribeNetworkAsyncCore().ConfigureAwait(false);
            }
            finally
            {
                _networkSubscribeLock.Release();
            }
        }

        private async Task UnsubscribeNetworkAsyncCore()
        {
            if (string.IsNullOrWhiteSpace(_networkProduct))
            {
                return;
            }

            var product = _networkProduct;
            var year = _networkYear;
            var month = _networkMonth;
            _networkProduct = null;
            _networkYear = 0;
            _networkMonth = 0;
            await _client.UnsubscribePrice(product, year, month).ConfigureAwait(false);
        }
    }
}
