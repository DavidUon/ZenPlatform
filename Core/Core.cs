using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brokers;
using ZenPlatform.DdePrice;
using ZenPlatform.LogText;
using ZenPlatform.MVVM.RulePage;
using ZenPlatform.SessionManager;
using ZenPlatform.Trade;

namespace ZenPlatform.Core
{
    public class Core : INotifyPropertyChanged
    {
        public ZClient.ZClient Client { get; } = new ZClient.ZClient();
        public UserInfoCtrl ClientCtrl { get; }
        public TimeCtrl TimeCtrl { get; }
        public IBroker Broker { get; }
        public DdePrice.DdePrice DdePrice { get; }
        public DdeService DdeService { get; }
        public PriceManager PriceManager { get; }
        public PriceMonitor PriceMonitor { get; }
        public LogCtrl LogCtrl { get; }
        public LogModel MainLog { get; }
        public TradeCtrl TradeCtrl { get; }
        public SessionManager.SessionManager[] SessionManagers { get; }
        public SessionPageViewModel[] SessionPages { get; }
        public KChartBridge ChartBridge { get; }
        public CoreEventHub EventHub { get; }
        private Task? _ddeReconnectTask;
        public bool IsDdeConnected => DdeService.IsConnected;
        public bool IsDdeQuoteAvailable => DdePrice.IsQuoteAvailable;

        public string ProgramName => "台指二號";
        public string Version1 => "V2.0.0";
        public string Version2 => "(beta)";
        public bool IsProgramStopped => ClientCtrl.IsProgramStopped;
        public bool IsBrokerConnected { get; private set; }
        public bool IsBrokerLoginSuccess => ClientCtrl.IsBrokerLoginSuccess;
        public bool? IsMarketOpen { get; private set; }
        public string CurContract { get; set; } = "";
        public int ContractYear { get; set; }
        public int ContractMonth { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<List<KChartCore.FunctionKBar>>? HistoryLoaded;
        public event Action<PriceMonitor.PriceStallInfo>? PriceStalled;
        public event Action<PriceMonitor.PriceStallInfo>? PriceAutoSwitched;

        private readonly ConcurrentQueue<CoreQueueItem> _queue = new();
        private readonly SemaphoreSlim _queueSignal = new(0);
        private readonly CancellationTokenSource _queueCts = new();
        private Task? _queueTask;
        private int _historyFetchToken;
        private readonly PriceSwitchCoordinator _priceSwitchCoordinator;

        public Core()
        {
            // 使用者登入/權限/帳號資料
            ClientCtrl = new UserInfoCtrl(Client);
            // 交易所時間與開盤狀態
            TimeCtrl = new TimeCtrl(new Exchanges.Taifex());
            // 期貨商連線與下單通道
            Broker = new Concord();
            // DDE 底層通訊與解析
            DdePrice = new DdePrice.DdePrice();
            // UI 執行緒 DDE 連線/重連/訂閱管理
            DdeService = new DdeService(DdePrice);
            // 報價來源切換與訂閱管理
            PriceManager = new PriceManager(Client, DdeService);
            // RichText log 控制
            LogCtrl = new LogCtrl();
            // 目前顯示的主 log 資料來源
            MainLog = new LogModel();
            // 交易控制 Gate
            TradeCtrl = new TradeCtrl();
            // 報價/時間轉送到 KChartCore
            ChartBridge = new KChartBridge();
            // 多策略管理器
            SessionManagers = CreateSessionManagers(10, TradeCtrl);
            foreach (var manager in SessionManagers)
            {
                manager.FetchHistoryBars = period => ChartBridge.GetHistoryList(period);
                manager.RegisterKBarPeriod = period => ChartBridge.RegisterPeriod(period);
                manager.UnregisterKBarPeriod = period => ChartBridge.UnregisterPeriod(period);
                manager.IsHistoryReady = () => ChartBridge.HistoryReady;
            }
            SessionManager.SessionStateStore.LoadAll(SessionManagers);
            foreach (var manager in SessionManagers)
            {
                if (manager.IsStrategyRunning)
                {
                    manager.Log.Add("程式開啟");
                }
            }
            SessionPages = CreateSessionPages(SessionManagers, Broker, ClientCtrl);
            // Queue 分派中心
            EventHub = new CoreEventHub(SessionManagers, ChartBridge, TradeCtrl);
            _priceSwitchCoordinator = new PriceSwitchCoordinator(
                () => IsBrokerConnected,
                () => ClientCtrl.LastUserData?.User?.Email,
                () => ProgramName,
                () => DateTime.UtcNow,
                text =>
                {
                    foreach (var manager in SessionManagers)
                    {
                        manager.Log.Add(text, text, ZenPlatform.LogText.LogTxtColor.黃色);
                    }
                },
                async (email, subject, body) => await ClientCtrl.SendMailAsync(email, subject, body).ConfigureAwait(false));
            _priceSwitchCoordinator.MarkStartup();
            // 監控報價是否中斷並觸發事件
            PriceMonitor = new PriceMonitor(
                TimeCtrl,
                EventHub,
                PriceManager,
                () => (CurContract, ContractYear, ContractMonth),
                () => TimeCtrl.IsMarketOpenNow,
                () => IsDdeConnected,
                UpdatePriceSubscription);
            DdePrice.OnDdePrice += OnDdePrice;
            DdePrice.QuoteStatusChanged += _ => OnPropertyChanged(nameof(IsDdeQuoteAvailable));
            DdeService.ConnectionLost += OnDdeConnectionLost;
            DdeService.HealthCheckLog += message =>
            {
                foreach (var manager in SessionManagers)
                {
                    manager.Log.Add(message, message, ZenPlatform.LogText.LogTxtColor.黃色);
                }
            };
            DdeService.HealthCheckRecovered += key =>
            {
                var currentKey = $"{CurContract}_{ContractYear:D4}_{ContractMonth:D2}";
                if (string.Equals(key, currentKey, StringComparison.Ordinal))
                {
                    PriceManager.SetTemporaryNetworkFallback(false);
                    UpdatePriceSubscription();
                }
            };
            IsMarketOpen = TimeCtrl.IsMarketOpenNow;
            IsBrokerConnected = Broker.IsDomesticConnected;
            if (string.IsNullOrWhiteSpace(CurContract))
            {
                CurContract = "大型台指";
            }

            ChartBridge.HistoryReady = false;

            if (ContractYear <= 0 || ContractMonth <= 0)
            {
                var contract = TimeCtrl.GetCurrentContractMonth();
                ContractYear = contract.Year;
                ContractMonth = contract.Month;
            }
            Client.PriceSnapshotReceived += OnNetworkPriceSnapshot;
            Client.PriceUpdateReceived += OnNetworkPriceUpdate;
            Client.Reconnected += OnClientReconnected;
            ClientCtrl.LoginStateChanged += UpdatePriceSubscription;
            ClientCtrl.ProgramStoppedChanged += _ => OnPropertyChanged(nameof(IsProgramStopped));
            ClientCtrl.UserAccountInfoFetched += OnUserAccountInfoFetched;
            Broker.OnBrokerNotify += OnBrokerNotify;
            TimeCtrl.MarketOpenChanged += OnMarketOpenChanged;
            TimeCtrl.TimeTick += OnTimeTick;
            PriceMonitor.PriceStalled += info => PriceStalled?.Invoke(info);
            PriceMonitor.AutoSwitchedToNetwork += info => PriceAutoSwitched?.Invoke(info);
            PriceMonitor.AutoSwitchedToNetwork += info => _priceSwitchCoordinator.HandleAutoSwitchedToNetwork(info);
            PriceManager.SourceChanged += _priceSwitchCoordinator.HandleSourceChanged;
            _queueTask = Task.Run(() => ProcessQueueAsync(_queueCts.Token));
        }

        private void OnDdeConnectionLost(IntPtr hConv)
        {
            UpdatePriceSubscription();
            PriceMonitor.HandleDdeDisconnected();
        }

        private void OnClientReconnected()
        {
            PriceManager.ResetNetworkSubscription();
            UpdatePriceSubscription();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnMarketOpenChanged(object? sender, bool isOpen)
        {
            IsMarketOpen = isOpen;
            OnPropertyChanged(nameof(IsMarketOpen));
        }

        private void OnDdePrice(string productName, string fieldName, int year, int month, string data, bool isRequest)
        {
            EnqueueQuote(productName, fieldName, year, month, data, isRequest, TimeCtrl.ExchangeTime, QuoteSource.Dde);
        }

        private DateTime? _lastQueueSecond;

        private void OnTimeTick(object? sender, DateTime time)
        {
            var stamp = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, time.Kind);
            if (_lastQueueSecond == null || _lastQueueSecond.Value != stamp)
            {
                _lastQueueSecond = stamp;
                EnqueueTime(time);
            }
        }

        private async void OnUserAccountInfoFetched(UserAccountInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Permission.BrokerPassword) ||
                string.IsNullOrWhiteSpace(info.Permission.Account))
            {
                return;
            }

            await Broker.LoginAsync(
                info.LoginId,
                info.Permission.BrokerPassword,
                info.Permission.BranchName,
                info.Permission.Account);
        }

        private void OnBrokerNotify(BrokerNotifyType type, string message)
        {
            if (type == BrokerNotifyType.DomesticLoginSuccess || type == BrokerNotifyType.ForeignLoginSuccess)
            {
                ClientCtrl.SetBrokerLoginSuccess(true);
                OnPropertyChanged(nameof(IsBrokerLoginSuccess));
            }
            else if (type == BrokerNotifyType.DomesticLoginFailed || type == BrokerNotifyType.ForeignLoginFailed)
            {
                ClientCtrl.SetBrokerLoginSuccess(false);
                OnPropertyChanged(nameof(IsBrokerLoginSuccess));
            }

            var connected = Broker.IsDomesticConnected;
            if (IsBrokerConnected != connected)
            {
                IsBrokerConnected = connected;
                OnPropertyChanged(nameof(IsBrokerConnected));
                UpdatePriceSubscription();
            }
        }

        public Task StartupAsync()
        {
            return StartupInternalAsync();
        }


        public void SetContract(string product, int year, int month)
        {
            CurContract = product ?? "";
            ContractYear = year;
            ContractMonth = month;
            ChartBridge.HistoryReady = false;
            UpdatePriceSubscription();
            _ = FetchServerHistoryAsync();
        }

        public void EnqueueTime(DateTime time)
        {
            _queue.Enqueue(CoreQueueItem.FromTime(time));
            _queueSignal.Release();
        }

        public void EnqueueQuote(string product, string field, int year, int month, string value, bool isRequest, DateTime time, QuoteSource source)
        {
            if (!string.IsNullOrWhiteSpace(CurContract) &&
                ContractYear > 0 &&
                ContractMonth > 0 &&
                (!string.Equals(product, CurContract, StringComparison.Ordinal) ||
                 year != ContractYear ||
                 month != ContractMonth))
            {
                return;
            }

            var update = new QuoteUpdate(
                product,
                QuoteFieldMapper.FromChineseName(field),
                year,
                month,
                value,
                isRequest,
                time,
                source);
            _queue.Enqueue(CoreQueueItem.FromQuote(update));
            _queueSignal.Release();
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await _queueSignal.WaitAsync(token);
                    while (_queue.TryDequeue(out var item))
                    {
                        EventHub.Dispatch(item);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task StartupInternalAsync()
        {
            _priceSwitchCoordinator.MarkStartup();
            await ClientCtrl.StartupAsync(ProgramName, $"{Version1} {Version2}".Trim());
            StartDdeReconnectLoop();
            UpdatePriceSubscription();
            _ = FetchServerHistoryAsync();
        }

        private void StartDdeReconnectLoop()
        {
            if (_ddeReconnectTask != null)
            {
                return;
            }

            _ddeReconnectTask = Task.Run(async () =>
            {
                while (true)
                {
                    if (!DdeService.IsConnected)
                    {
                        try
                        {
                            await DdeService.TryConnectAsync("MMSDDE", "FUSA");
                            UpdatePriceSubscription();
                        }
                        catch (System.Exception)
                        {
                        }
                    }

                    await Task.Delay(5000);
                }
            });
        }

        private void UpdatePriceSubscription()
        {
            PriceManager.Update(IsBrokerConnected, IsDdeConnected, ClientCtrl.IsLoggedIn, CurContract, ContractYear, ContractMonth);
        }

        private void OnNetworkPriceSnapshot(ZClient.PriceSnapshot snapshot)
        {
            if (!TryParseContractKey(snapshot.Contract, out var product, out var year, out var month))
            {
                return;
            }

            EnqueueQuote(product, "成交價", year, month, snapshot.Last ?? "", true, TimeCtrl.ExchangeTime, QuoteSource.Network);
            EnqueueQuote(product, "買價", year, month, snapshot.Bid ?? "", true, TimeCtrl.ExchangeTime, QuoteSource.Network);
            EnqueueQuote(product, "賣價", year, month, snapshot.Ask ?? "", true, TimeCtrl.ExchangeTime, QuoteSource.Network);
            EnqueueQuote(product, "成交量", year, month, snapshot.Volume ?? "", true, TimeCtrl.ExchangeTime, QuoteSource.Network);
            EnqueueQuote(product, "漲跌", year, month, snapshot.Change ?? "", true, TimeCtrl.ExchangeTime, QuoteSource.Network);
            if (!string.IsNullOrWhiteSpace(snapshot.Time))
            {
                var timeValue = NormalizeTimeValue(snapshot.Time);
                EnqueueQuote(product, "時間", year, month, timeValue, true, TimeCtrl.ExchangeTime, QuoteSource.Network);
            }
        }

        private void OnNetworkPriceUpdate(ZClient.PriceUpdate update)
        {
            if (!TryParseContractKey(update.Contract, out var product, out var year, out var month))
            {
                return;
            }

            if (!DdeItemCatalog.TryParseDdeItem(update.Item, out _, out _, out _, out var fieldName))
            {
                return;
            }

            var value = update.Value ?? "";
            if (fieldName == "時間")
            {
                value = NormalizeTimeValue(value);
            }

            EnqueueQuote(product, fieldName, year, month, value, false, TimeCtrl.ExchangeTime, QuoteSource.Network);
        }

        private static bool TryParseContractKey(string contract, out string product, out int year, out int month)
        {
            product = "";
            year = 0;
            month = 0;
            if (string.IsNullOrWhiteSpace(contract))
            {
                return false;
            }

            var parts = contract.Split('_');
            if (parts.Length < 3)
            {
                return false;
            }

            product = parts[0];
            if (!int.TryParse(parts[1], out year))
            {
                return false;
            }

            if (!int.TryParse(parts[2], out month))
            {
                return false;
            }

            return true;
        }

        private async Task FetchServerHistoryAsync()
        {
            var token = Interlocked.Increment(ref _historyFetchToken);
            try
            {
                ChartBridge.HistoryReady = false;
                foreach (var manager in SessionManagers)
                {
                    manager.SuppressIndicatorLog = true;
                }

                var result = await Client.FetchKBarHistory(CurContract, ContractYear, ContractMonth, 3000).ConfigureAwait(false);
                if (token != _historyFetchToken)
                {
                    foreach (var manager in SessionManagers)
                    {
                        manager.SuppressIndicatorLog = false;
                    }
                    return;
                }

                if (result == null || result.Bars.Count == 0)
                {
                    HistoryLoaded?.Invoke(new List<KChartCore.FunctionKBar>());
                    foreach (var manager in SessionManagers)
                    {
                        manager.SuppressIndicatorLog = false;
                    }
                    return;
                }

                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_history.csv");
                WriteHistoryCsvUtf8(path, result);
                var history = ChartBridge.ImportHistory(path, TimeCtrl.ExchangeTime);
                HistoryLoaded?.Invoke(history);
                foreach (var manager in SessionManagers)
                {
                    if (manager.IsStrategyRunning)
                    {
                        manager.RebuildIndicators();
                    }
                    manager.SuppressIndicatorLog = false;
                }
            }
            catch
            {
                foreach (var manager in SessionManagers)
                {
                    manager.SuppressIndicatorLog = false;
                }
                // Ignore history fetch failures.
            }
        }

        private static void WriteHistoryCsvUtf8(string path, ZClient.KBarHistoryResult result)
        {
            var orderedBars = result.Bars.OrderBy(b => b.Time).ToList();
            var lines = new List<string>(orderedBars.Count + 2)
            {
                result.Header,
                "日期,開盤價,最高價,最低價,收盤價,成交量,IsFloating,"
            };

            for (var i = 0; i < orderedBars.Count; i++)
            {
                var bar = orderedBars[i];
                var volume = bar.Volume;

                var line = string.Join(",",
                    bar.Time.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    bar.Open.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bar.High.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bar.Low.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bar.Close.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    volume.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bar.IsFloating ? "1" : "0",
                    "");
                lines.Add(line);
            }

            System.IO.File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string NormalizeTimeValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            var timeValue = raw.Trim();
            var spaceIndex = timeValue.IndexOf(' ');
            if (spaceIndex >= 0 && spaceIndex < timeValue.Length - 1)
            {
                timeValue = timeValue[(spaceIndex + 1)..];
            }

            if (timeValue.Length == 6 && int.TryParse(timeValue, out var numeric))
            {
                var hh = numeric / 10000;
                var mm = (numeric / 100) % 100;
                var ss = numeric % 100;
                if (hh >= 0 && hh <= 23 && mm >= 0 && mm <= 59 && ss >= 0 && ss <= 59)
                {
                    return $"{hh:D2}:{mm:D2}:{ss:D2}";
                }
            }

            return timeValue;
        }

        private static SessionManager.SessionManager[] CreateSessionManagers(int count, TradeCtrl tradeCtrl)
        {
            var managers = new SessionManager.SessionManager[count];
            for (var i = 0; i < count; i++)
            {
                var manager = new SessionManager.SessionManager(i, tradeCtrl);
                manager.LogRequested += message => manager.Log.Add(message);
                managers[i] = manager;
            }
            return managers;
        }

        private static SessionPageViewModel[] CreateSessionPages(SessionManager.SessionManager[] managers, IBroker broker, UserInfoCtrl userInfoCtrl)
        {
            var pages = new SessionPageViewModel[managers.Length];
            for (var i = 0; i < managers.Length; i++)
            {
                pages[i] = new SessionPageViewModel(managers[i], broker, userInfoCtrl);
            }
            return pages;
        }
    }
}
