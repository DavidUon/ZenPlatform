using System;
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

        // Use a longer freshness window so low-liquidity sessions don't flap source state.
        // Health checks should maintain connectivity signal even when there are no trades.
        private static readonly TimeSpan TickStaleThreshold = TimeSpan.FromSeconds(65);
        private static readonly TimeSpan SwitchDebounce = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MinSourceHold = TimeSpan.FromSeconds(10);

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
                return;
            }

            if (string.Equals(_networkProduct, contract, StringComparison.Ordinal) &&
                _networkYear == year &&
                _networkMonth == month)
            {
                return;
            }

            await UnsubscribeNetworkAsync().ConfigureAwait(false);
            _networkProduct = contract;
            _networkYear = year;
            _networkMonth = month;
            await _client.SubscribePrice(contract, year, month).ConfigureAwait(false);
        }

        private async Task UnsubscribeNetworkAsync()
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
