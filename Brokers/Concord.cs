using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Concord.API.Future.Client;
using Concord.API.Future.Client.OrderFormat;

namespace Brokers;

public sealed class Concord : IBroker
{
    public static readonly IReadOnlyDictionary<string, string> CodeDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["000"] = "成功(訊息內容需詳相關規格)",
            ["101"] = "登入開始",
            ["102"] = "登入成功",
            ["103"] = "登出成功",
            ["104"] = "未開期貨戶",
            ["105"] = "未授權API",
            ["106"] = "未授權憑證",
            ["110"] = "下單連線成功",
            ["111"] = "下單登入驗證成功",
            ["112"] = "下單重新連線",
            ["113"] = "下單連線結束",
            ["114"] = "回報連線成功",
            ["115"] = "回報登入驗證成功",
            ["116"] = "回報註冊完成",
            ["117"] = "回報重新連線",
            ["118"] = "回報連線結束",
            ["120"] = "簽章成功",
            ["121"] = "新單送出成功",
            ["122"] = "改單送出成功",
            ["123"] = "刪單送出成功",
            ["124"] = "回補送出成功",
            ["201"] = "登入主機無法連線",
            ["202"] = "尚未登入",
            ["203"] = "重複登入，請先登出",
            ["204"] = "版本過舊，請更新至最新版",
            ["210"] = "下單連線中斷",
            ["211"] = "下單登入驗證失敗",
            ["214"] = "回報連線中斷",
            ["215"] = "回報登入驗證失敗",
            ["220"] = "簽章失敗",
            ["221"] = "新單送出失敗",
            ["222"] = "改單送出失敗",
            ["223"] = "刪單送出失敗",
            ["224"] = "回補送出失敗",
            ["230"] = "解析XML錯誤",
            ["250"] = "帳號不能使用下單功能(母子帳新增)",
            ["998"] = "登入錯誤(會根據不同錯誤訊息做揭示)",
            ["999"] = "非預期錯誤"
        };
    private const string DefaultServerIp = "tradeapi.concords.com.tw";
    private readonly ucClient _api = new();
    private string _loginId = string.Empty;
    private string _password = string.Empty;
    private string _branchName = string.Empty;
    private string _account = string.Empty;
    private bool _isDomesticLoginSuccess;
    private bool _isForeignLoginSuccess;
    private bool _isDomesticConnected;
    private bool _isForeignConnected;
    private CancellationTokenSource? _loginTimeoutCts;
    private bool _loginInProgress;
    private bool _domesticLoginNotified;
    private bool _foreignLoginNotified;
    private readonly object _loginReportSync = new();
    private readonly List<string> _domesticLoginReports = new();
    private readonly List<string> _foreignLoginReports = new();
    private readonly ConcurrentDictionary<string, OrderState> _ordersById = new(StringComparer.Ordinal);
    private int _orderCounter;

    public event Action<string, string>? DomesticGeneralReport;
    public event Action<string, string>? DomesticErrorReport;
    public event Action<string, string>? DomesticOrderReport;
    public event Action<string, string>? ForeignGeneralReport;
    public event Action<string, string>? ForeignErrorReport;
    public event Action<string, string>? ForeignOrderReport;
    public event Action<BrokerNotifyType, string>? OnBrokerNotify;

    public bool IsDomesticLoginSuccess => _isDomesticLoginSuccess;
    public bool IsForeignLoginSuccess => _isForeignLoginSuccess;
    public bool IsDomesticConnected => _isDomesticConnected;
    public bool IsForeignConnected => _isForeignConnected;
    public Concord()
    {
        _api.OnFGeneralReport += (code, message) =>
        {
            UpdateDomesticLoginState(code);
            UpdateDomesticConnectionState(code);
            BufferLoginReport(isDomestic: true, category: "G", code, message);
            DomesticGeneralReport?.Invoke(code, message);
            OnBrokerNotify?.Invoke(BrokerNotifyType.DomesticGeneralReport, $"{code} {message}");
        };
        _api.OnFErrorReport += (code, message) =>
        {
            BufferLoginReport(isDomestic: true, category: "E", code, message);
            DomesticErrorReport?.Invoke(code, message);
        };
        _api.OnFOrderReport += (code, message) =>
        {
            BufferLoginReport(isDomestic: true, category: "O", code, message);
            DomesticOrderReport?.Invoke(code, message);
            OnBrokerNotify?.Invoke(BrokerNotifyType.DomesticOrderReport, $"{code} {message}");
            HandleOrderReport(message);
        };
        _api.OnFFGeneralReport += (code, message) =>
        {
            UpdateForeignLoginState(code);
            UpdateForeignConnectionState(code);
            BufferLoginReport(isDomestic: false, category: "G", code, message);
            ForeignGeneralReport?.Invoke(code, message);
            OnBrokerNotify?.Invoke(BrokerNotifyType.ForeignGeneralReport, $"{code} {message}");
        };
        _api.OnFFErrorReport += (code, message) =>
        {
            BufferLoginReport(isDomestic: false, category: "E", code, message);
            ForeignErrorReport?.Invoke(code, message);
        };
        _api.OnFFOrderReport += (code, message) =>
        {
            BufferLoginReport(isDomestic: false, category: "O", code, message);
            ForeignOrderReport?.Invoke(code, message);
            OnBrokerNotify?.Invoke(BrokerNotifyType.ForeignOrderReport, $"{code} {message}");
            HandleOrderReport(message);
        };
    }

    public string Login(string loginId, string password, string branchName, string account)
    {
        _loginId = loginId?.Trim() ?? string.Empty;
        _password = password ?? string.Empty;
        _branchName = branchName?.Trim() ?? string.Empty;
        _account = account?.Trim() ?? string.Empty;

        StartLoginTimeout();
        try
        {
            var result = _api.Login(_loginId, _password, DefaultServerIp, out var message);
            return $"{result ?? string.Empty}|{message ?? string.Empty}";
        }
        catch (Exception ex)
        {
            return $"|Login 函數擲出例外: {ex.Message}";
        }
    }

    public Task<string> LoginAsync(string loginId, string password, string branchName, string account)
    {
        return Task.Run(() => Login(loginId, password, branchName, account));
    }

    public string Logout()
    {
        try
        {
            StopLoginTimeout();
            var result = _api.Logout(out _);
            if (string.Equals(result, "000", StringComparison.Ordinal))
            {
                _isDomesticLoginSuccess = false;
                _isForeignLoginSuccess = false;
                _isDomesticConnected = false;
                _isForeignConnected = false;
            }
            return result ?? string.Empty;
        }
        catch
        {
            return "999";
        }
    }

    public string CheckConnect(bool isDomestic, out string message)
    {
        try
        {
            var result = isDomestic
                ? _api.FCheckConnect(out message)
                : _api.FFCheckConnect(out message);

            var isConnected = string.Equals(result, "102", StringComparison.Ordinal);
            if (isDomestic)
            {
                _isDomesticConnected = isConnected;
            }
            else
            {
                _isForeignConnected = isConnected;
            }

            return result ?? string.Empty;
        }
        catch
        {
            message = string.Empty;
            if (isDomestic)
            {
                _isDomesticConnected = false;
            }
            else
            {
                _isForeignConnected = false;
            }
            return "999";
        }
    }

    public decimal QueryDomesticAvailableMargin(out string resultCode, out string message)
    {
        return QueryDomesticMarginValue(isAvailable: true, out resultCode, out message);
    }

    public decimal QueryDomesticTotalEquity(out string resultCode, out string message)
    {
        return QueryDomesticMarginValue(isAvailable: false, out resultCode, out message);
    }

    public decimal QueryForeignAvailableMargin(out string resultCode, out string message)
    {
        return QueryForeignMarginValue(isAvailable: true, out resultCode, out message);
    }

    public decimal QueryForeignTotalEquity(out string resultCode, out string message)
    {
        return QueryForeignMarginValue(isAvailable: false, out resultCode, out message);
    }

    public string QueryDomesticCommodities(out string message)
    {
        try
        {
            return _api.FQueryCommo(out message);
        }
        catch (Exception ex)
        {
            message = $"QueryDomesticCommodities 例外: {ex.Message}";
            return "999";
        }
    }

    public string QueryForeignCommodities(out string message)
    {
        try
        {
            return _api.FFQueryCommo(out message);
        }
        catch (Exception ex)
        {
            message = $"QueryForeignCommodities 例外: {ex.Message}";
            return "999";
        }
    }

    public string GetCertStatus(string loginId, out string startDate, out string expireDate, out string message)
    {
        try
        {
            return _api.GetCertStatus(loginId, out startDate, out expireDate, out message);
        }
        catch (Exception ex)
        {
            startDate = string.Empty;
            expireDate = string.Empty;
            message = $"GetCertStatus 例外: {ex.Message}";
            return "999";
        }
    }

    public async Task<decimal> SendOrderAsync(OrderForm form, TimeSpan? timeout = null)
    {
        var sessionId = GenerateOrderId();
        var state = new OrderState(sessionId, form.Quantity);
        _ordersById[sessionId] = state;

        var apiResult = SendDomesticOrder(sessionId, form);
        if (!IsOrderAccepted(apiResult))
        {
            _ordersById.TryRemove(sessionId, out _);
            var code = TryParseCode(apiResult);
            return -code;
        }

        try
        {
            var waitTask = state.Completion.Task;
            var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var completed = await Task.WhenAny(waitTask, Task.Delay(waitTimeout)).ConfigureAwait(false);
            if (completed != waitTask)
            {
                throw new TimeoutException("Order not fully filled within timeout.");
            }

            return await waitTask.ConfigureAwait(false);
        }
        finally
        {
            _ordersById.TryRemove(sessionId, out _);
        }
    }

    public string PlaceOrder(
        string sessionId,
        string product,
        int year,
        int month,
        bool isBuy,
        decimal price,
        bool isMarket,
        bool isDayTrading)
    {
        throw new NotImplementedException();
    }

    public bool CancelOrder(string sessionId)
    {
        throw new NotImplementedException();
    }

    private void UpdateDomesticLoginState(string code)
    {
        if (string.Equals(code, "102", StringComparison.Ordinal))
        {
            _isDomesticLoginSuccess = true;
            NotifyLoginResult(isDomestic: true, success: true);
            StopLoginTimeoutIfComplete();
            return;
        }

        if (string.Equals(code, "210", StringComparison.Ordinal) ||
            string.Equals(code, "214", StringComparison.Ordinal) ||
            string.Equals(code, "215", StringComparison.Ordinal))
        {
            if (_loginInProgress)
            {
                return;
            }
            if (_isDomesticLoginSuccess)
            {
                _isDomesticLoginSuccess = false;
            }
        }
    }

    private void UpdateForeignLoginState(string code)
    {
        if (string.Equals(code, "102", StringComparison.Ordinal))
        {
            _isForeignLoginSuccess = true;
            NotifyLoginResult(isDomestic: false, success: true);
            StopLoginTimeoutIfComplete();
            return;
        }

        if (string.Equals(code, "210", StringComparison.Ordinal) ||
            string.Equals(code, "214", StringComparison.Ordinal) ||
            string.Equals(code, "215", StringComparison.Ordinal))
        {
            if (_loginInProgress)
            {
                return;
            }
            if (_isForeignLoginSuccess)
            {
                _isForeignLoginSuccess = false;
            }
        }
    }

    private void UpdateDomesticConnectionState(string code)
    {
        if (string.Equals(code, "110", StringComparison.Ordinal) ||
            string.Equals(code, "114", StringComparison.Ordinal))
        {
            _isDomesticConnected = true;
            return;
        }

        if (string.Equals(code, "210", StringComparison.Ordinal) ||
            string.Equals(code, "214", StringComparison.Ordinal))
        {
            _isDomesticConnected = false;
        }
    }

    private void UpdateForeignConnectionState(string code)
    {
        if (string.Equals(code, "110", StringComparison.Ordinal) ||
            string.Equals(code, "114", StringComparison.Ordinal))
        {
            _isForeignConnected = true;
            return;
        }

        if (string.Equals(code, "210", StringComparison.Ordinal) ||
            string.Equals(code, "214", StringComparison.Ordinal))
        {
            _isForeignConnected = false;
        }
    }

    private static string ExtractBranchNo(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return string.Empty;

        var start = branchName.IndexOf('(');
        var end = branchName.IndexOf(')');
        if (start >= 0 && end > start)
        {
            var inside = branchName.Substring(start + 1, end - start - 1).Trim();
            if (!string.IsNullOrWhiteSpace(inside))
                return inside;
        }

        var digits = new string(branchName.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? branchName.Trim() : digits;
    }

    private decimal QueryDomesticMarginValue(bool isAvailable, out string resultCode, out string message)
    {
        message = string.Empty;
        var branchNo = ExtractBranchNo(_branchName);
        if (string.IsNullOrWhiteSpace(branchNo) || string.IsNullOrWhiteSpace(_account))
        {
            resultCode = "999";
            return 0m;
        }

        try
        {
            resultCode = _api.FQueryMargin(branchNo, _account, "**", "", "", out message);
            if (!string.Equals(resultCode, "000", StringComparison.Ordinal))
            {
                return 0m;
            }

            return ParseDomesticMarginValue(message, isAvailable);
        }
        catch
        {
            resultCode = "999";
            return 0m;
        }
    }

    private decimal QueryForeignMarginValue(bool isAvailable, out string resultCode, out string message)
    {
        message = string.Empty;
        var branchNo = ExtractBranchNo(_branchName);
        if (string.IsNullOrWhiteSpace(branchNo) || string.IsNullOrWhiteSpace(_account))
        {
            resultCode = "999";
            return 0m;
        }

        try
        {
            resultCode = _api.FFQueryMargin(branchNo, _account, out message);
            if (!string.Equals(resultCode, "000", StringComparison.Ordinal))
            {
                return 0m;
            }

            return ParseForeignMarginValue(message, isAvailable);
        }
        catch
        {
            resultCode = "999";
            return 0m;
        }
    }

    private static decimal ParseDomesticMarginValue(string data, bool isAvailable)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return 0m;
        }

        var lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return 0m;
        }

        var fields = lines[0].Split(',');
        var available = fields.Length > 20 ? ParseDecimal(fields[20]) : 0m;
        var totalEquity = fields.Length > 17 ? ParseDecimal(fields[17]) : 0m;
        return isAvailable ? available : totalEquity;
    }

    private static decimal ParseForeignMarginValue(string data, bool isAvailable)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return 0m;
        }

        var lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var fields = line.Split(',');
            if (fields.Length > 19 && fields.Length > 2 && fields[2] == "***")
            {
                var available = ParseDecimal(fields[19]);
                var totalEquity = fields.Length > 15 ? ParseDecimal(fields[15]) : 0m;
                return isAvailable ? available : totalEquity;
            }
        }

        return 0m;
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value?.Trim(), out var result) ? result : 0m;
    }

    private string SendDomesticOrder(string sessionId, OrderForm form)
    {
        var branchNo = ExtractBranchNo(_branchName);
        if (string.IsNullOrWhiteSpace(branchNo) || string.IsNullOrWhiteSpace(_account))
        {
            return "999";
        }

        var apiSymbol = GetApiSymbol(form.ContractName);
        if (string.IsNullOrWhiteSpace(apiSymbol))
        {
            return "998";
        }

        var monthCode = GetMonthCode(form.Month);
        if (string.IsNullOrWhiteSpace(monthCode))
        {
            return "997";
        }

        var contractCode = $"{apiSymbol}{monthCode}{form.Year % 10}";
        var timeInForce = form.IsMarketOrder ? "I" : "R";
        var orderPrice = form.IsMarketOrder
            ? 0m
            : form.LimitPrice ?? 0m;
        if (orderPrice < 0)
        {
            orderPrice = 0;
        }
        var orderStruct = new FOrderNew
        {
            bhno = branchNo,
            cseq = _account,
            mtype = "F",
            sflag = "1",
            commo = contractCode,
            fir = timeInForce,
            rtype = form.IsDayTrading ? "2" : "",
            otype = form.IsMarketOrder ? "P" : "L",
            bs = form.IsBuy ? "B" : "S",
            qty = form.Quantity,
            price = orderPrice
        };

        string message;
        var guid = sessionId;
        var result = _api.FOrderNew(orderStruct, out message, ref guid);
        if (!string.IsNullOrWhiteSpace(guid) && !string.Equals(guid, sessionId, StringComparison.Ordinal))
        {
            if (_ordersById.TryGetValue(sessionId, out var state))
            {
                _ordersById.TryAdd(guid, state);
            }
        }

        return result ?? string.Empty;
    }

    private static bool IsOrderAccepted(string code)
    {
        return string.Equals(code, "000", StringComparison.Ordinal) ||
               string.Equals(code, "121", StringComparison.Ordinal);
    }

    private static int TryParseCode(string code)
    {
        return int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 1;
    }

    private string GenerateOrderId()
    {
        var counter = Interlocked.Increment(ref _orderCounter);
        return $"{DateTime.Now:MMdd-HHmmss}-{counter}";
    }

    private void HandleOrderReport(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var fields = ParseReportMessage(message);
        if (!TryResolveOrder(fields, out var state) || state == null)
        {
            return;
        }

        if (fields.TryGetValue("150", out var statusCode) && string.Equals(statusCode, "4", StringComparison.Ordinal))
        {
            state.TrySetException(new InvalidOperationException("Order canceled."));
            return;
        }

        if (fields.TryGetValue("150", out statusCode) && string.Equals(statusCode, "8", StringComparison.Ordinal))
        {
            var reason = fields.TryGetValue("58", out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : "Order rejected.";
            state.TrySetException(new InvalidOperationException(reason));
            return;
        }

        // Only execution reports should be treated as fills.
        // Domestic FIX: 150=F(部分成交) / D(全部成交).
        // Prevent treating send-status(150=I) as fills.
        if (!fields.TryGetValue("150", out statusCode) ||
            (!string.Equals(statusCode, "F", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(statusCode, "D", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Use execution fields (LastQty/LastPx) for real filled price.
        // 38/44 are order fields (OrderQty/Price) and can differ from actual fill.
        if (!fields.TryGetValue("32", out var qtyText) || !fields.TryGetValue("31", out var priceText))
        {
            return;
        }

        if (!int.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var execQty))
        {
            return;
        }

        if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var execPrice))
        {
            return;
        }

        if (execQty <= 0 || execPrice <= 0)
        {
            return;
        }

        state.AddFill(execQty, execPrice);
    }

    private bool TryResolveOrder(Dictionary<string, string> fields, out OrderState? state)
    {
        state = null;

        if (fields.TryGetValue("20103", out var guid) && _ordersById.TryGetValue(guid, out state))
        {
            if (fields.TryGetValue("11", out var exchangeId) && !string.IsNullOrWhiteSpace(exchangeId))
            {
                _ordersById.TryAdd(exchangeId, state);
            }
            return true;
        }

        if (fields.TryGetValue("41", out var originalId) && _ordersById.TryGetValue(originalId, out state))
        {
            return true;
        }

        if (fields.TryGetValue("11", out var currentId) && _ordersById.TryGetValue(currentId, out state))
        {
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> ParseReportMessage(string message)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = message.Split('|');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2)
            {
                result[parts[0]] = parts[1];
            }
        }
        return result;
    }

    private static string GetApiSymbol(ContractName contractName)
    {
        return contractName switch
        {
            ContractName.大型台指 => "TXF",
            ContractName.小型台指 => "MXF",
            ContractName.微型台指 => "TMF",
            _ => ""
        };
    }

    private static string GetMonthCode(int month)
    {
        return month switch
        {
            1 => "A",
            2 => "B",
            3 => "C",
            4 => "D",
            5 => "E",
            6 => "F",
            7 => "G",
            8 => "H",
            9 => "I",
            10 => "J",
            11 => "K",
            12 => "L",
            _ => ""
        };
    }


    private sealed class OrderState
    {
        private readonly object _gate = new();
        private readonly int _targetQty;
        private int _cumQty;
        private decimal _avgPrice;

        public OrderState(string sessionId, int targetQty)
        {
            SessionId = sessionId;
            _targetQty = targetQty;
            Completion = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string SessionId { get; }
        public TaskCompletionSource<decimal> Completion { get; }

        public void AddFill(int qty, decimal price)
        {
            lock (_gate)
            {
                var totalQty = _cumQty + qty;
                if (totalQty <= 0)
                {
                    return;
                }

                _avgPrice = ((_avgPrice * _cumQty) + (price * qty)) / totalQty;
                _cumQty = totalQty;

                if (_cumQty >= _targetQty)
                {
                    Completion.TrySetResult(_avgPrice);
                }
            }
        }

        public void TrySetException(Exception ex)
        {
            Completion.TrySetException(ex);
        }
    }

    private void StartLoginTimeout()
    {
        _loginTimeoutCts?.Cancel();
        _loginTimeoutCts = new CancellationTokenSource();
        var token = _loginTimeoutCts.Token;
        _loginInProgress = true;
        _domesticLoginNotified = false;
        _foreignLoginNotified = false;
        lock (_loginReportSync)
        {
            _domesticLoginReports.Clear();
            _foreignLoginReports.Clear();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (!_isDomesticLoginSuccess)
                {
                    NotifyLoginResult(isDomestic: true, success: false);
                }

                if (!_isForeignLoginSuccess)
                {
                    NotifyLoginResult(isDomestic: false, success: false);
                }
                _loginInProgress = false;
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }

    private void StopLoginTimeout()
    {
        _loginTimeoutCts?.Cancel();
        _loginTimeoutCts = null;
        _loginInProgress = false;
    }

    private void StopLoginTimeoutIfComplete()
    {
        if (_isDomesticLoginSuccess && _isForeignLoginSuccess)
        {
            StopLoginTimeout();
        }
    }

    private void NotifyLoginResult(bool isDomestic, bool success)
    {
        var loginTrace = BuildAndClearLoginTrace(isDomestic, success);
        if (isDomestic)
        {
            if (_domesticLoginNotified)
            {
                return;
            }
            _domesticLoginNotified = true;
            OnBrokerNotify?.Invoke(
                success ? BrokerNotifyType.DomesticLoginSuccess : BrokerNotifyType.DomesticLoginFailed,
                loginTrace);
        }
        else
        {
            if (_foreignLoginNotified)
            {
                return;
            }
            _foreignLoginNotified = true;
            OnBrokerNotify?.Invoke(
                success ? BrokerNotifyType.ForeignLoginSuccess : BrokerNotifyType.ForeignLoginFailed,
                loginTrace);
        }
    }

    private void BufferLoginReport(bool isDomestic, string category, string code, string message)
    {
        if (!_loginInProgress)
        {
            return;
        }

        var content = string.IsNullOrWhiteSpace(message)
            ? ResolveCodeDescription(code)
            : message.Trim();
        var line = $"[{category}] {(string.IsNullOrWhiteSpace(code) ? "---" : code.Trim())} {content}".Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_loginReportSync)
        {
            var target = isDomestic ? _domesticLoginReports : _foreignLoginReports;
            target.Add(line);
            const int maxReports = 30;
            if (target.Count > maxReports)
            {
                target.RemoveAt(0);
            }
        }
    }

    private string BuildAndClearLoginTrace(bool isDomestic, bool success)
    {
        lock (_loginReportSync)
        {
            var target = isDomestic ? _domesticLoginReports : _foreignLoginReports;

            if (success)
            {
                target.Clear();
                return "success";
            }

            if (target.Count == 0)
            {
                return "failed(no_general_report)";
            }

            var text = string.Join(" | ", target);
            target.Clear();
            return text;
        }
    }

    private static string ResolveCodeDescription(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        return CodeDescriptions.TryGetValue(code.Trim(), out var description)
            ? description
            : string.Empty;
    }
}
