using System;
using System.Threading.Tasks;

namespace ZenPlatform.Core
{
    public sealed class PriceManager
    {
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
        private bool _ddeSeenOnce;
        private bool _temporaryNetworkFallback;

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
        public bool DdeSeenOnce => _ddeSeenOnce;
        public event Action<PriceSource, PriceSource>? SourceChanged;

        public void SetTemporaryNetworkFallback(bool enabled)
        {
            _temporaryNetworkFallback = enabled;
        }

        public void ResetNetworkSubscription()
        {
            _networkProduct = null;
            _networkYear = 0;
            _networkMonth = 0;
        }

        public void Update(bool brokerConnected, bool ddeConnected, bool loggedIn, string contract, int year, int month)
        {
            if (!brokerConnected)
            {
                _ddeSeenOnce = false;
            }
            else if (ddeConnected)
            {
                _ddeSeenOnce = true;
            }

            var desiredSource = ResolvePriceSource(brokerConnected, ddeConnected, loggedIn);
            if (desiredSource != _priceSource)
            {
                var prev = _priceSource;
                if (_priceSource == PriceSource.Network)
                {
                    _ = UnsubscribeNetworkAsync();
                }

                _priceSource = desiredSource;
                SourceChanged?.Invoke(prev, _priceSource);
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

        private PriceSource ResolvePriceSource(bool brokerConnected, bool ddeConnected, bool loggedIn)
        {
            var ddeAvailable = brokerConnected && ddeConnected;
            var networkAvailable = loggedIn;

            return _mode switch
            {
                PriceMode.ManualDde => ddeAvailable ? PriceSource.Dde : (networkAvailable ? PriceSource.Network : PriceSource.None),
                PriceMode.ManualNetwork => networkAvailable ? PriceSource.Network : (ddeAvailable ? PriceSource.Dde : PriceSource.None),
                _ => ResolveAutoSource(brokerConnected, ddeAvailable, networkAvailable)
            };
        }

        private PriceSource ResolveAutoSource(bool brokerConnected, bool ddeAvailable, bool networkAvailable)
        {
            if (brokerConnected)
            {
                if (_temporaryNetworkFallback && networkAvailable)
                    return PriceSource.Network;

                if (ddeAvailable)
                    return PriceSource.Dde;

                return _ddeSeenOnce && networkAvailable
                    ? PriceSource.Network
                    : PriceSource.None;
            }

            return ddeAvailable ? PriceSource.Dde : (networkAvailable ? PriceSource.Network : PriceSource.None);
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
