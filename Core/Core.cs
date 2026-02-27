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
        // 安裝包版本號（例如 V2.0.3），供更新機制比較版本先後。
        public string BuildSerial => "V2.0.3";
        public string Version1 => BuildSerial;
        public string Version2 => "";
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
            try
            {
                TaifexHisDbManager.MagistockStoragePaths.EnsureInitialized();
            }
            catch
            {
                // Best-effort initialization; downstream logic falls back to weekend rule if needed.
            }

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
            TradeCtrl.BindBroker(Broker);
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
                manager.GetCurrentContract = () => (CurContract, ContractYear, ContractMonth);
                manager.ResolveContractPrice = ResolveContractPrice;
                manager.SwitchContract = SetContract;
                manager.SessionTradeFailed += OnSessionTradeFailed;
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
                () =>
                {
                    PriceManager.ResetNetworkSubscription();
                    UpdatePriceSubscription();
                });
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
            ClientCtrl.ProgramStoppedChanged += OnProgramStoppedChanged;
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

        private void OnProgramStoppedChanged(bool stopped)
        {
            OnPropertyChanged(nameof(IsProgramStopped));
            if (!stopped)
            {
                return;
            }

            _ = PriceManager.StopAsync();
            while (_queue.TryDequeue(out _))
            {
            }

            foreach (var manager in SessionManagers)
            {
                if (!manager.IsStrategyRunning)
                {
                    continue;
                }

                manager.Log.Add("授權已停用，策略自動停止");
                manager.StopStrategySilently();
            }
        }

        private void OnMarketOpenChanged(object? sender, bool isOpen)
        {
            IsMarketOpen = isOpen;
            OnPropertyChanged(nameof(IsMarketOpen));
        }

        private void OnDdePrice(string productName, string fieldName, int year, int month, string data, bool isRequest)
        {
            PriceManager.NotifyDdeTick();
            EnqueueQuote(productName, fieldName, year, month, data, isRequest, TimeCtrl.ExchangeTime, QuoteSource.Dde);
        }

        private DateTime? _lastQueueSecond;

        private void OnTimeTick(object? sender, DateTime time)
        {
            // Drive quote source state machine (debounce/stale checks) on a fixed cadence.
            UpdatePriceSubscription();

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
                foreach (var manager in SessionManagers)
                {
                    manager.Log.Add("期貨商自動登入略過：缺少期貨帳號或期貨密碼。");
                }
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

                var market = type == BrokerNotifyType.DomesticLoginFailed ? "國內" : "國外";
                var text = string.IsNullOrWhiteSpace(message)
                    ? $"期貨商登入失敗({market})"
                    : $"期貨商登入失敗({market})：{message}";
                foreach (var manager in SessionManagers)
                {
                    manager.Log.Add(text, text, ZenPlatform.LogText.LogTxtColor.黃色);

                    if (!string.IsNullOrWhiteSpace(message) && message.Contains('|'))
                    {
                        var lines = message.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var line in lines)
                        {
                            var detail = $"期貨商登入回報({market})：{line}";
                            manager.Log.Add(detail, detail, ZenPlatform.LogText.LogTxtColor.黃色);
                        }
                    }
                }
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
            if (IsProgramStopped)
            {
                return;
            }

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
            if (IsProgramStopped)
            {
                _ = PriceManager.StopAsync();
                return;
            }

            PriceManager.Update(IsBrokerConnected, IsDdeConnected, ClientCtrl.IsLoggedIn, IsMarketOpen ?? TimeCtrl.IsMarketOpenNow, CurContract, ContractYear, ContractMonth);
        }

        private void OnNetworkPriceSnapshot(ZClient.PriceSnapshot snapshot)
        {
            if (!TryParseContractKey(snapshot.Contract, out var product, out var year, out var month))
            {
                return;
            }

            PriceManager.NotifyNetworkTick();
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

            PriceManager.NotifyNetworkTick();
            var value = update.Value ?? "";
            if (fieldName == "時間")
            {
                value = NormalizeTimeValue(value);
            }

            EnqueueQuote(product, fieldName, year, month, value, false, TimeCtrl.ExchangeTime, QuoteSource.Network);
        }

        private void OnSessionTradeFailed(Strategy.Session session, string detail)
        {
            _ = SendSessionTradeFailedMailAsync(session, detail);
        }

        private decimal? ResolveContractPrice(string product, int year, int month, bool isBuy, TimeSpan timeout)
        {
            try
            {
                if (string.Equals(product, CurContract, StringComparison.Ordinal) &&
                    year == ContractYear &&
                    month == ContractMonth)
                {
                    var live = isBuy
                        ? (TradeCtrl.Ask ?? TradeCtrl.Last ?? TradeCtrl.Bid)
                        : (TradeCtrl.Bid ?? TradeCtrl.Last ?? TradeCtrl.Ask);
                    if (live.HasValue && live.Value > 0m)
                    {
                        return live.Value;
                    }
                }

                var started = DateTime.UtcNow;
                while ((DateTime.UtcNow - started) < timeout)
                {
                    var snapshot = Client.SubscribePrice(product, year, month).GetAwaiter().GetResult();
                    if (snapshot != null)
                    {
                        var parsed = ParseSnapshotPrice(snapshot, isBuy);
                        if (parsed.HasValue && parsed.Value > 0m)
                        {
                            _ = Client.UnsubscribePrice(product, year, month);
                            return parsed.Value;
                        }
                    }

                    Thread.Sleep(200);
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static decimal? ParseSnapshotPrice(ZClient.PriceSnapshot snapshot, bool isBuy)
        {
            if (snapshot == null)
            {
                return null;
            }

            var primary = isBuy ? snapshot.Ask : snapshot.Bid;
            if (decimal.TryParse(primary, out var px) && px > 0m)
            {
                return px;
            }

            if (decimal.TryParse(snapshot.Last, out var last) && last > 0m)
            {
                return last;
            }

            var secondary = isBuy ? snapshot.Bid : snapshot.Ask;
            if (decimal.TryParse(secondary, out var alt) && alt > 0m)
            {
                return alt;
            }

            return null;
        }

        private async Task SendSessionTradeFailedMailAsync(Strategy.Session session, string detail)
        {
            var email = ClientCtrl.LastUserData?.User?.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var manager = session.Manager;
            var strategyText = manager != null ? $"策略{manager.Index + 1}" : "策略";
            var now = manager?.CurrentTime;
            var notifyTime = now.HasValue && now.Value.Year >= 2000 ? now.Value : DateTime.Now;
            var sideText = session.StartPosition > 0 ? "多單" : session.StartPosition < 0 ? "空單" : "未知方向";
            var reason = string.IsNullOrWhiteSpace(detail) ? "任務執行中發生交易失敗，此任務停止運作" : detail;

            var subject = "【緊急系統通知】系統發生交易失敗";
            var body =
                $"親愛的使用者您好：\n\n" +
                $"{ProgramName} 系統偵測到任務執行期間發生交易失敗，\n" +
                $"系統已將該任務停止，以避免後續風險擴大。\n\n" +
                $"事件資訊如下：\n\n" +
                $"策略：{strategyText}\n" +
                $"任務編號：[{session.Id}]\n" +
                $"任務方向：{sideText}\n" +
                $"發生時間：{notifyTime:yyyy/MM/dd HH:mm:ss}\n" +
                $"失敗原因：{reason}\n\n" +
                $"請您確認期貨商連線、帳號權限與下單條件是否正常，\n" +
                $"必要時可手動重新建立任務。\n\n" +
                $"祝\n交易順利\n\n" +
                $"Magistock 系統自動通知";

            var ok = await ClientCtrl.SendMailAsync(email, subject, body).ConfigureAwait(false);
            if (!ok && manager != null)
            {
                var error = string.IsNullOrWhiteSpace(ClientCtrl.LastMailError) ? "未知錯誤" : ClientCtrl.LastMailError;
                manager.Log.Add($"交易失敗通知寄信失敗：{error}", $"交易失敗通知寄信失敗：{error}", ZenPlatform.LogText.LogTxtColor.黃色);
            }
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

                var result = await Client.FetchKBarHistory(CurContract, ContractYear, ContractMonth, 6000).ConfigureAwait(false);
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
                        manager.InitializeTrendSideFromLatestBar();
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
