using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ZClient;

public sealed class UserInfo
{
    public string ProgName { get; set; } = "";
    public string ProgVersion { get; set; } = "";
}

public sealed class UserData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string LineId { get; set; } = "";
    public string Memo { get; set; } = "";
    public double MarginBalance { get; set; }
    public string CreatedAt { get; set; } = "";
    public string LastOnlineAt { get; set; } = "";
}

public sealed class PermissionData
{
    public string Id { get; set; } = "";
    public string BrokerName { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string Account { get; set; } = "";
    public string BrokerPassword { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string ProgramExpireAt { get; set; } = "";
    public int PermissionCount { get; set; }
    public bool AllowConcurrent { get; set; }
    public bool IsBanned { get; set; }
    public bool UnlimitedExpiry { get; set; }
    public bool UnlimitedPermission { get; set; }
}

public sealed class UserDataResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public UserData? User { get; set; }
    public List<PermissionData> Permissions { get; set; } = new();
}

public class ZClient
{
    public event Action? Reconnected;
    private static readonly Regex CodeRegex = new(@"\((?<code>[A-Z0-9]+)\)", RegexOptions.Compiled);
    private static readonly Regex YmRegex = new(@"(?<ym>\d{4})", RegexOptions.Compiled);
    private static readonly Dictionary<char, int> MonthMap = new()
    {
        { 'F', 1 },
        { 'G', 2 },
        { 'H', 3 },
        { 'J', 4 },
        { 'K', 5 },
        { 'M', 6 },
        { 'N', 7 },
        { 'Q', 8 },
        { 'U', 9 },
        { 'V', 10 },
        { 'X', 11 },
        { 'Z', 12 }
    };

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly ConcurrentQueue<KBarImportBatch> _kbarImportQueue = new();
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _retryCts;
    private TaskCompletionSource<bool>? _loginTcs;
    private TaskCompletionSource<UserDataResult?>? _dataTcs;
    private TaskCompletionSource<bool>? _registerTcs;
    private TaskCompletionSource<bool>? _checkIdTcs;
    private TaskCompletionSource<bool>? _updateTcs;
    private TaskCompletionSource<bool?>? _activeCheckTcs;
    private TaskCompletionSource<KBarHistoryResult?>? _historyTcs;
    private TaskCompletionSource<PriceSnapshot?>? _priceSnapshotTcs;
    private TaskCompletionSource<KBarImportResult?>? _importKbarsTcs;

    private string? _lastUrl;
    private string? _loginId;
    private string? _password;
    private UserInfo? _userInfo;
    private bool _manualDisconnect;
    private bool _isLoggedIn;
    private bool _loginRejected;
    private bool _logoutSent;

    public event Action<string>? Log;
    public event Action<string>? Message;
    public event Action<UserDataResult>? UserDataReceived;
    public event Action<PriceSnapshot>? PriceSnapshotReceived;
    public event Action<PriceUpdate>? PriceUpdateReceived;
    public UserDataResult? LastUserData { get; private set; }

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
    public bool IsLoggedIn => _isLoggedIn;
    public bool AutoReconnectEnabled { get; set; } = true;
    public int RetryDelayMs { get; set; } = 5000;
    public int LoginTimeoutMs { get; set; } = 5000;

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUser { get; set; } = "magistockservice@gmail.com";
	public string SmtpPassword { get; set; } = "cwgbnweyvamvjhjr";
    public string MailFrom { get; set; } = "magistockservice@gmail.com";
    public string MailSubject { get; set; } = "Magistock";

    public async Task Connect(string url)
    {
        _manualDisconnect = false;
        _loginRejected = false;
        _lastUrl = url;
        var ok = await EnsureConnectedAsync(url);
        if (!ok)
            StartRetryLoop();
    }

    public async Task Disconnect()
    {
        _manualDisconnect = true;
        _retryCts?.Cancel();
        _retryCts = null;

        await _connectLock.WaitAsync();
        try
        {
            if (_ws == null)
                return;

            try
            {
                if (_isLoggedIn)
                    await SendLogoutAsync();
                _cts?.Cancel();
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Disconnect error: {ex.Message}");
            }
            finally
            {
                CleanupSocket();
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public void SetUserInfo(UserInfo info)
    {
        _userInfo = new UserInfo
        {
            ProgName = info.ProgName,
            ProgVersion = info.ProgVersion
        };
    }

    public async Task<bool> Login(string id, string password)
    {
        _loginId = id;
        _password = password;
        _loginRejected = false;
        _logoutSent = false;

        var ok = await LoginOnceAsync();
        if (!ok)
            StartRetryLoop();

        return ok;
    }

    public async Task SendLogoutAsync()
    {
        if (_ws == null || !_isLoggedIn)
            return;

        try
        {
            var payload = new { type = "logout" };
            var json = JsonSerializer.Serialize(payload);
            await SendTextAsync(json);
            _logoutSent = true;
            Log?.Invoke("Sent logout.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Logout error: {ex.Message}");
        }
    }

    public async Task<bool> Register(string id, string password, string name = "", string phone = "", string email = "", string lineId = "")
    {
        if (_ws == null)
            return false;

        LastRegisterError = "";

        var payload = new
        {
            type = "register",
            id,
            password,
            name,
            phone,
            email,
            lineId
        };

        _registerTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent register for {id}.");

        var completed = await Task.WhenAny(_registerTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _registerTcs.Task)
        {
            Log?.Invoke("Register timeout.");
            _registerTcs.TrySetResult(false);
        }

        return await _registerTcs.Task;
    }

    public async Task<bool> CheckRegisterId(string id)
    {
        if (_ws == null)
            return false;

        LastCheckIdError = "";
        var payload = new
        {
            type = "check_id",
            id
        };

        _checkIdTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent check_id for {id}.");

        var completed = await Task.WhenAny(_checkIdTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _checkIdTcs.Task)
        {
            Log?.Invoke("Check ID timeout.");
            _checkIdTcs.TrySetResult(false);
        }

        return await _checkIdTcs.Task;
    }

    public Task<bool> UpdateUserData(PermissionData permission, string? id = null, string? lineId = null, string? phone = null, string? email = null)
    {
        return UpdateUserDataInternal(permission, id, null, lineId, phone, email, null, null, null, null);
    }

    public Task<bool> UpdateUserData(PermissionData permission, string? id = null, string? name = null, string? lineId = null, string? phone = null, string? email = null,
        string? oldPassword = null, string? newPassword = null, string? oldBrokerPassword = null, string? newBrokerPassword = null)
    {
        return UpdateUserDataInternal(permission, id, name, lineId, phone, email, oldPassword, newPassword, oldBrokerPassword, newBrokerPassword);
    }

    private async Task<bool> UpdateUserDataInternal(PermissionData permission, string? id, string? name, string? lineId, string? phone, string? email,
        string? oldPassword, string? newPassword, string? oldBrokerPassword, string? newBrokerPassword)
    {
        if (_ws == null)
            return false;

        LastUpdateError = "";
        var payload = new
        {
            type = "UpdateUserData",
            id,
            name,
            lineId,
            phone,
            email,
            oldPassword,
            newPassword,
            oldBrokerPassword,
            newBrokerPassword,
            brokerName = permission.BrokerName,
            branchName = permission.BranchName,
            account = permission.Account,
            brokerPassword = permission.BrokerPassword,
            programName = permission.ProgramName,
            programExpireAt = permission.ProgramExpireAt,
            permissionCount = permission.PermissionCount,
            allowConcurrent = permission.AllowConcurrent,
            isBanned = permission.IsBanned,
            unlimitedExpiry = permission.UnlimitedExpiry,
            unlimitedPermission = permission.UnlimitedPermission
        };

        _updateTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent UpdateUserData for {permission.Account}.");

        var completed = await Task.WhenAny(_updateTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _updateTcs.Task)
        {
            Log?.Invoke("Update user data timeout.");
            _updateTcs.TrySetResult(false);
        }

        return await _updateTcs.Task;
    }

    public async Task SendMarginBalance(string? id, double marginBalance)
    {
        if (_ws == null || !_isLoggedIn)
            return;

        var payload = new
        {
            type = "update_margin",
            id,
            marginBalance
        };

        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent update_margin for {id}.");
    }

    public async Task SendUserInfo(UserInfo info)
    {
        SetUserInfo(info);

        if (_ws == null || !_isLoggedIn)
            return;

        var payload = new
        {
            type = "user_info",
            info.ProgName,
            info.ProgVersion
        };

        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke("Sent user info.");
    }

    public async Task<bool> FetchUserData()
    {
        if (_ws == null)
            return false;

        var payload = new
        {
            type = "fetch_data"
        };

        _dataTcs = new TaskCompletionSource<UserDataResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke("Sent fetch_data.");

        var completed = await Task.WhenAny(_dataTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _dataTcs.Task)
        {
            Log?.Invoke("Fetch data timeout.");
            _dataTcs.TrySetResult(null);
        }

        var result = await _dataTcs.Task;
        return result != null && result.Ok;
    }

    public async Task<bool> CheckDuplicateConnection(string account, string programName)
    {
        if (_ws == null)
            return false;

        var payload = new
        {
            type = "active_check",
            account,
            programName
        };

        _activeCheckTcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent active_check for {account} {programName}.");

        var completed = await Task.WhenAny(_activeCheckTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _activeCheckTcs.Task)
        {
            Log?.Invoke("Active check timeout.");
            _activeCheckTcs.TrySetResult(null);
        }

        return await _activeCheckTcs.Task ?? false;
    }

    public string LastMailError { get; private set; } = "";
    public string LastRegisterError { get; private set; } = "";
    public string LastCheckIdError { get; private set; } = "";
    public string LastUpdateError { get; private set; } = "";

    public async Task<bool> SendMailAsync(string to, string subject, string body)
    {
        LastMailError = "";
        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(body))
        {
            LastMailError = "empty to/body";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SmtpUser) || string.IsNullOrWhiteSpace(SmtpPassword))
        {
            LastMailError = "smtp user/password missing";
            return false;
        }

        try
        {
        var finalSubject = string.IsNullOrWhiteSpace(subject) ? MailSubject : subject;
        var from = new MailAddress(MailFrom, "Magistock 程式交易");
        using var message = new MailMessage(from, new MailAddress(to))
        {
            Subject = finalSubject,
            Body = body
        };
            using var client = new SmtpClient(SmtpHost, SmtpPort)
            {
                EnableSsl = SmtpEnableSsl,
                Credentials = new NetworkCredential(SmtpUser, SmtpPassword)
            };

            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            LastMailError = ex.Message;
            return false;
        }
    }

    public async Task<KBarHistoryResult?> FetchKBarHistory(string product, int year, int month, int limit = 3000)
    {
        if (_ws == null)
            return null;

        var payload = new
        {
            type = "fetch_kbars",
            product,
            year,
            month,
            limit
        };

        _historyTcs = new TaskCompletionSource<KBarHistoryResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent fetch_kbars for {product} {year}/{month:00}.");

        var completed = await Task.WhenAny(_historyTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _historyTcs.Task)
        {
            Log?.Invoke("Fetch history timeout.");
            _historyTcs.TrySetResult(null);
        }

        return await _historyTcs.Task;
    }

    public async Task<PriceSnapshot?> SubscribePrice(string product, int year, int month)
    {
        if (_ws == null)
            return null;

        var payload = new
        {
            type = "subscribe_price",
            product,
            year,
            month
        };

        _priceSnapshotTcs = new TaskCompletionSource<PriceSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent subscribe_price for {product} {year}/{month:00}.");

        var completed = await Task.WhenAny(_priceSnapshotTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _priceSnapshotTcs.Task)
        {
            Log?.Invoke("Subscribe price timeout.");
            _priceSnapshotTcs.TrySetResult(null);
        }

        return await _priceSnapshotTcs.Task;
    }

    public async Task<bool> UnsubscribePrice(string product, int year, int month)
    {
        if (_ws == null)
            return false;

        var payload = new
        {
            type = "unsubscribe_price",
            product,
            year,
            month
        };

        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent unsubscribe_price for {product} {year}/{month:00}.");
        return true;
    }

    private async Task<bool> EnsureConnectedAsync(string url)
    {
        await _connectLock.WaitAsync();
        try
        {
            if (IsConnected)
                return true;

            CleanupSocket();
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            _isLoggedIn = false;

            var uri = new Uri(url);
            await _ws.ConnectAsync(uri, _cts.Token);
            Log?.Invoke($"Connected to {uri}.");

            _ = Task.Run(() => ReceiveLoopAsync(_ws, _cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Connect error: {ex.Message}");
            CleanupSocket();
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<bool> LoginOnceAsync()
    {
        if (_ws == null || string.IsNullOrWhiteSpace(_loginId) || _password == null)
            return false;

        await _loginLock.WaitAsync();
        try
        {
            if (_ws == null)
                return false;

            var payload = new
            {
                type = "login",
                id = _loginId,
                password = _password
            };

            _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var json = JsonSerializer.Serialize(payload);
            await SendTextAsync(json);
            Log?.Invoke($"Sent login for {_loginId}.");

            var completed = await Task.WhenAny(_loginTcs.Task, Task.Delay(LoginTimeoutMs));
            if (completed != _loginTcs.Task)
            {
                Log?.Invoke("Login timeout.");
                _loginTcs.TrySetResult(false);
            }

            return await _loginTcs.Task;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        try
        {
            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log?.Invoke("Server closed connection.");
                        return;
                    }

                    if (result.Count > 0)
                        ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                if (TryHandleLoginResponse(msg) || TryHandleUserDataResponse(msg) || TryHandleActiveCheckResponse(msg) || TryHandleHistoryResponse(msg) || TryHandleImportKBarResponse(msg) || TryHandlePriceResponse(msg))
                    continue;

                Message?.Invoke(msg);
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Receive error: {ex.Message}");
        }
        finally
        {
            if (_isLoggedIn && !_manualDisconnect && !_logoutSent)
            {
                Log?.Invoke("Disconnected without logout (unexpected).");
            }
            CleanupSocket();
            StartRetryLoop();
        }
    }

    private void StartRetryLoop()
    {
        if (!AutoReconnectEnabled || _manualDisconnect)
            return;

        if (_retryCts != null)
            return;

        if (_loginRejected)
            return;

        if (string.IsNullOrWhiteSpace(_lastUrl) || string.IsNullOrWhiteSpace(_loginId) || _password == null)
            return;

        _retryCts = new CancellationTokenSource();
        _ = Task.Run(() => RetryLoopAsync(_retryCts.Token));
    }

    private async Task RetryLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!IsConnected)
                await EnsureConnectedAsync(_lastUrl!);

            if (IsConnected)
            {
                var ok = await LoginOnceAsync();
                if (ok)
                {
                    if (_userInfo != null)
                        await SendUserInfo(_userInfo);

                    Reconnected?.Invoke();
                    _retryCts = null;
                    return;
                }
            }

            await Task.Delay(RetryDelayMs, token);
        }
    }

    private bool TryHandleLoginResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return false;

            var type = typeProp.GetString();
            if (string.Equals(type, "login_ok", StringComparison.OrdinalIgnoreCase))
            {
                _isLoggedIn = true;
                _loginTcs?.TrySetResult(true);
                return true;
            }

            if (string.Equals(type, "login_fail", StringComparison.OrdinalIgnoreCase))
            {
                _isLoggedIn = false;
                _loginRejected = true;
                _loginTcs?.TrySetResult(false);
                _retryCts?.Cancel();
                _retryCts = null;
                return true;
            }

            if (string.Equals(type, "register_ok", StringComparison.OrdinalIgnoreCase))
            {
                LastRegisterError = "";
                _registerTcs?.TrySetResult(true);
                return true;
            }

            if (string.Equals(type, "register_fail", StringComparison.OrdinalIgnoreCase))
            {
                LastRegisterError = doc.RootElement.TryGetProperty("reason", out var reasonProp)
                    ? reasonProp.GetString() ?? ""
                    : "";
                _registerTcs?.TrySetResult(false);
                return true;
            }

            if (string.Equals(type, "check_id_ok", StringComparison.OrdinalIgnoreCase))
            {
                LastCheckIdError = "";
                _checkIdTcs?.TrySetResult(true);
                return true;
            }

            if (string.Equals(type, "check_id_fail", StringComparison.OrdinalIgnoreCase))
            {
                LastCheckIdError = doc.RootElement.TryGetProperty("reason", out var reasonProp)
                    ? reasonProp.GetString() ?? ""
                    : "";
                _checkIdTcs?.TrySetResult(false);
                return true;
            }

            if (string.Equals(type, "update_ok", StringComparison.OrdinalIgnoreCase))
            {
                LastUpdateError = "";
                _updateTcs?.TrySetResult(true);
                return true;
            }

            if (string.Equals(type, "update_fail", StringComparison.OrdinalIgnoreCase))
            {
                LastUpdateError = doc.RootElement.TryGetProperty("reason", out var reasonProp)
                    ? reasonProp.GetString() ?? ""
                    : "";
                _updateTcs?.TrySetResult(false);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private bool TryHandleUserDataResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return false;

            if (!string.Equals(typeProp.GetString(), "user_data", StringComparison.OrdinalIgnoreCase))
                return false;

            var result = JsonSerializer.Deserialize<UserDataResult>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                LastUserData = result;
                _dataTcs?.TrySetResult(result);
                UserDataReceived?.Invoke(result);
            }
            else
            {
                _dataTcs?.TrySetResult(null);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryHandleActiveCheckResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return false;

            if (!string.Equals(typeProp.GetString(), "active_check_result", StringComparison.OrdinalIgnoreCase))
                return false;

            var ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (!ok)
            {
                _activeCheckTcs?.TrySetResult(null);
                return true;
            }

            var exists = doc.RootElement.TryGetProperty("exists", out var existsProp) && existsProp.GetBoolean();
            _activeCheckTcs?.TrySetResult(exists);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryHandleHistoryResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return false;

            if (!string.Equals(typeProp.GetString(), "kbar_history", StringComparison.OrdinalIgnoreCase))
                return false;

            var ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (!ok)
            {
                _historyTcs?.TrySetResult(null);
                return true;
            }

            var header = doc.RootElement.TryGetProperty("header", out var headerProp)
                ? headerProp.GetString() ?? ""
                : "";

            var contract = doc.RootElement.TryGetProperty("contract", out var contractProp)
                ? contractProp.GetString() ?? ""
                : "";

            var bars = new List<KBarHistoryRow>();
            if (doc.RootElement.TryGetProperty("bars", out var barsProp))
            {
                foreach (var barElem in barsProp.EnumerateArray())
                {
                    var timeText = barElem.GetProperty("time").GetString() ?? "";
                    if (!DateTime.TryParse(timeText, out var time))
                        continue;

                    var open = barElem.GetProperty("open").GetDecimal();
                    var high = barElem.GetProperty("high").GetDecimal();
                    var low = barElem.GetProperty("low").GetDecimal();
                    var close = barElem.GetProperty("close").GetDecimal();
                    var volume = barElem.GetProperty("volume").GetInt32();
                    var isFloating = barElem.GetProperty("isFloating").GetBoolean();

                    bars.Add(new KBarHistoryRow(time, open, high, low, close, volume, isFloating));
                }
            }

            _historyTcs?.TrySetResult(new KBarHistoryResult(contract, header, bars));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryHandleImportKBarResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return false;

            var type = typeProp.GetString();
            if (!string.Equals(type, "import_kbars", StringComparison.OrdinalIgnoreCase))
                return false;

            var ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            var contract = doc.RootElement.TryGetProperty("contract", out var contractProp)
                ? contractProp.GetString() ?? ""
                : "";
            var inserted = doc.RootElement.TryGetProperty("inserted", out var insertedProp)
                ? insertedProp.GetInt32()
                : 0;
            var messageText = doc.RootElement.TryGetProperty("message", out var messageProp)
                ? messageProp.GetString() ?? ""
                : "";

            _importKbarsTcs?.TrySetResult(new KBarImportResult(contract, inserted, messageText, ok));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryHandlePriceResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return false;

            var type = typeProp.GetString();
            if (string.Equals(type, "price_snapshot", StringComparison.OrdinalIgnoreCase))
            {
                var ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
                if (!ok)
                {
                    _priceSnapshotTcs?.TrySetResult(null);
                    return true;
                }

                var contract = doc.RootElement.TryGetProperty("contract", out var contractProp)
                    ? contractProp.GetString() ?? ""
                    : "";

                if (!doc.RootElement.TryGetProperty("snapshot", out var snapProp))
                    return true;

                var snapshot = new PriceSnapshot
                {
                    Contract = contract,
                    Time = snapProp.TryGetProperty("time", out var timeProp) ? timeProp.GetString() ?? "" : "",
                    Last = snapProp.TryGetProperty("last", out var lastProp) ? lastProp.GetString() ?? "" : "",
                    Bid = snapProp.TryGetProperty("bid", out var bidProp) ? bidProp.GetString() ?? "" : "",
                    Ask = snapProp.TryGetProperty("ask", out var askProp) ? askProp.GetString() ?? "" : "",
                    Volume = snapProp.TryGetProperty("volume", out var volProp) ? volProp.GetString() ?? "" : "",
                    Change = snapProp.TryGetProperty("change", out var chgProp) ? chgProp.GetString() ?? "" : ""
                };

                _priceSnapshotTcs?.TrySetResult(snapshot);
                PriceSnapshotReceived?.Invoke(snapshot);
                return true;
            }

            if (string.Equals(type, "ontick", StringComparison.OrdinalIgnoreCase))
            {
                var update = new PriceUpdate
                {
                    Contract = doc.RootElement.TryGetProperty("contract", out var cProp) ? cProp.GetString() ?? "" : "",
                    Item = doc.RootElement.TryGetProperty("item", out var itemProp) ? itemProp.GetString() ?? "" : "",
                    Value = doc.RootElement.TryGetProperty("value", out var valueProp) ? valueProp.GetString() ?? "" : "",
                    Time = doc.RootElement.TryGetProperty("time", out var timeProp) ? timeProp.GetString() ?? "" : ""
                };

                PriceUpdateReceived?.Invoke(update);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private async Task SendTextAsync(string text)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CleanupSocket()
    {
        _loginTcs?.TrySetResult(false);
        _registerTcs?.TrySetResult(false);
        _dataTcs?.TrySetResult(null);
        _activeCheckTcs?.TrySetResult(null);
        _historyTcs?.TrySetResult(null);
        _priceSnapshotTcs?.TrySetResult(null);
        _importKbarsTcs?.TrySetResult(null);
        _isLoggedIn = false;
        LastUserData = null;
        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
    }

    public UserDataResult? QueryUserData()
    {
        return LastUserData;
    }

    public UserData? QueryUser()
    {
        return LastUserData?.User;
    }

    public IReadOnlyList<PermissionData> QueryPermissions()
    {
        return LastUserData?.Permissions ?? new List<PermissionData>();
    }

    public bool QueueKBarImportFromFile(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = "file not found";
            return false;
        }

        if (!TryReadCsvLines(path, out var lines, out error))
            return false;

        if (lines.Count < 2)
        {
            error = "empty file";
            return false;
        }

        if (!TryParseContract(lines[0], out var contractKey))
        {
            error = "cannot parse contract";
            return false;
        }

        var bars = new List<KBarHistoryRow>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (i <= 1)
                continue;

            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("日期", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 6)
                continue;

            if (!TryParseTime(parts[0].Trim(), out var time))
                continue;

            if (!decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var open))
                continue;
            if (!decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
                continue;
            if (!decimal.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var low))
                continue;
            if (!decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                continue;
            if (!int.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
                volume = 0;

            bars.Add(new KBarHistoryRow(time, open, high, low, close, volume, false));
        }

        if (bars.Count == 0)
        {
            error = "no bars";
            return false;
        }

        var sortedBars = bars.OrderBy(b => b.Time).ToList();
        if (!ValidateOneMinuteBars(sortedBars, out error, out var dedupedBars))
            return false;

        _kbarImportQueue.Enqueue(new KBarImportBatch(contractKey, lines[0], dedupedBars, Path.GetFileName(path)));
        return true;
    }

    public bool TryDequeueKBarImport(out KBarImportBatch batch)
    {
        return _kbarImportQueue.TryDequeue(out batch!);
    }

    public int PendingKBarImportCount => _kbarImportQueue.Count;

    public async Task<KBarImportResult?> UploadQueuedKBarImportAsync()
    {
        if (!TryDequeueKBarImport(out var batch))
            return null;

        return await UploadKBarImportAsync(batch);
    }

    public async Task<KBarImportResult?> UploadKBarImportAsync(KBarImportBatch batch)
    {
        if (_ws == null)
        {
            Log?.Invoke("Import kbars skipped: not connected.");
            return null;
        }

        var payload = new
        {
            type = "import_kbars",
            contract = batch.Contract,
            header = batch.Header,
            source = batch.SourceFile,
            bars = batch.Bars.Select(b => new
            {
                time = b.Time,
                open = b.Open,
                high = b.High,
                low = b.Low,
                close = b.Close,
                volume = b.Volume,
                isFloating = b.IsFloating
            })
        };

        _importKbarsTcs = new TaskCompletionSource<KBarImportResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var json = JsonSerializer.Serialize(payload);
        await SendTextAsync(json);
        Log?.Invoke($"Sent import_kbars for {batch.Contract} rows={batch.Bars.Count}.");

        var completed = await Task.WhenAny(_importKbarsTcs.Task, Task.Delay(LoginTimeoutMs));
        if (completed != _importKbarsTcs.Task)
        {
            Log?.Invoke("Import kbars timeout.");
            _importKbarsTcs.TrySetResult(null);
        }

        return await _importKbarsTcs.Task;
    }

    private static bool TryReadCsvLines(string path, out List<string> lines, out string? error)
    {
        error = null;
        lines = new List<string>();
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var utf8Text = Encoding.UTF8.GetString(bytes);
        lines = SplitLines(utf8Text);
        if (lines.Count > 0 && TryParseContract(lines[0], out _))
            return true;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var big5Text = Encoding.GetEncoding(950).GetString(bytes);
        lines = SplitLines(big5Text);
        return lines.Count > 0;
    }

    private static List<string> SplitLines(string text)
    {
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static bool TryParseTime(string text, out DateTime time)
    {
        return DateTime.TryParseExact(text, "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out time)
               || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static bool ValidateOneMinuteBars(List<KBarHistoryRow> bars, out string? error, out List<KBarHistoryRow> dedupedBars)
    {
        error = null;
        dedupedBars = new List<KBarHistoryRow>(bars.Count);
        for (var i = 0; i < bars.Count; i++)
        {
            var time = bars[i].Time;
            if (time.Second != 0 || time.Millisecond != 0)
            {
                error = "bar time is not on minute boundary";
                return false;
            }

            if (i == 0)
            {
                dedupedBars.Add(bars[i]);
                continue;
            }

            var diff = time - bars[i - 1].Time;
            if (diff.Ticks == 0)
                continue;

            if (diff.TotalSeconds <= 0 || diff.TotalSeconds % 60 != 0)
            {
                error = "bar interval is not 1-minute aligned";
                return false;
            }

            dedupedBars.Add(bars[i]);
        }

        return true;
    }

    private static bool TryParseContract(string header, out string contractKey)
    {
        contractKey = "";
        var codeMatch = CodeRegex.Match(header);
        if (!codeMatch.Success)
            return false;

        var code = codeMatch.Groups["code"].Value;
        if (string.IsNullOrWhiteSpace(code) || code.Length < 4)
            return false;

        var prefix = code.Substring(0, 3).ToUpperInvariant();
        var name = prefix switch
        {
            "WTX" => "大型台指",
            "WMT" => "小型台指",
            "WTM" => "微型台指",
            _ => "未知商品"
        };

        var ymMatch = YmRegex.Match(header);
        int year;
        int month;
        if (ymMatch.Success)
        {
            var ym = ymMatch.Groups["ym"].Value;
            if (ym.Length == 4 && int.TryParse(ym[..2], out var yy) && int.TryParse(ym[2..], out var mm))
            {
                year = 2000 + yy;
                month = mm;
            }
            else
            {
                return false;
            }
        }
        else
        {
            var monthCode = code[3];
            if (!MonthMap.TryGetValue(monthCode, out month))
                return false;

            var yearDigit = code[^1];
            if (!char.IsDigit(yearDigit))
                return false;

            var decade = DateTime.Now.Year / 10 * 10;
            year = decade + (yearDigit - '0');
            if (year < DateTime.Now.Year - 1)
                year += 10;
        }

        contractKey = $"{name}_{year:D4}_{month:D2}";
        return true;
    }
}

public sealed class PriceSnapshot
{
    public string Contract { get; set; } = "";
    public string Time { get; set; } = "";
    public string Last { get; set; } = "";
    public string Bid { get; set; } = "";
    public string Ask { get; set; } = "";
    public string Volume { get; set; } = "";
    public string Change { get; set; } = "";
}

public sealed class PriceUpdate
{
    public string Contract { get; set; } = "";
    public string Item { get; set; } = "";
    public string Value { get; set; } = "";
    public string Time { get; set; } = "";
}

public sealed record KBarHistoryRow(
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    int Volume,
    bool IsFloating
);

public sealed record KBarHistoryResult(
    string Contract,
    string Header,
    List<KBarHistoryRow> Bars
);

public sealed record KBarImportBatch(
    string Contract,
    string Header,
    List<KBarHistoryRow> Bars,
    string SourceFile
);

public sealed record KBarImportResult(
    string Contract,
    int Inserted,
    string Message,
    bool Ok
);
