using ZClient;

namespace ZClientCtrl;

public class ZClientCtrl
{
    private readonly ZClient.ZClient _client = new();
    private string? _targetUrl;
    private string? _loginId;
    private string? _password;
    private UserInfo? _userInfo;

    public event Action<string>? Log;
    public event Action<string>? Message;
    public event Action<PriceSnapshot>? PriceSnapshot;
    public event Action<PriceUpdate>? PriceUpdate;

    public bool IsConnected => _client.IsConnected;
    public bool IsLoggedIn => _client.IsLoggedIn;
    public global::ZClient.UserDataResult? LastUserData { get; private set; }

    public ZClientCtrl()
    {
        _client.Log += msg => Log?.Invoke(msg);
        _client.Message += msg => Message?.Invoke(msg);
        _client.UserDataReceived += data => LastUserData = data;
        _client.PriceSnapshotReceived += snapshot => PriceSnapshot?.Invoke(snapshot);
        _client.PriceUpdateReceived += update => PriceUpdate?.Invoke(update);
    }

    public string SmtpUser
    {
        get => _client.SmtpUser;
        set => _client.SmtpUser = value;
    }

    public string SmtpPassword
    {
        get => _client.SmtpPassword;
        set => _client.SmtpPassword = value;
    }

    public string MailFrom
    {
        get => _client.MailFrom;
        set => _client.MailFrom = value;
    }

    public string LastMailError => _client.LastMailError;
    public string LastRegisterError => _client.LastRegisterError;
    public string LastCheckIdError => _client.LastCheckIdError;
    public string LastUpdateError => _client.LastUpdateError;

    public void SetTargetUrl(string url)
    {
        _targetUrl = url;
    }

    public void SetProfile(string id, string password, UserInfo info)
    {
        _loginId = id;
        _password = password;
        _userInfo = info;
        _client.SetUserInfo(info);
    }

    public async Task Connect()
    {
        if (string.IsNullOrWhiteSpace(_targetUrl))
        {
            Log?.Invoke("Target URL is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_loginId) || _password == null)
        {
            Log?.Invoke("Login info is empty.");
            return;
        }

        await _client.Connect(_targetUrl);
        var ok = await _client.Login(_loginId, _password);
        if (!ok)
        {
            Log?.Invoke("Login failed, retrying in background.");
            return;
        }

        if (_userInfo != null)
            await _client.SendUserInfo(_userInfo);
    }

    public async Task<bool> FetchUserData()
    {
        var ok = await _client.FetchUserData();
        LastUserData = _client.QueryUserData();
        return ok;
    }

    public Task<KBarHistoryResult?> FetchKBarHistory(string product, int year, int month, int limit = 3000)
    {
        return _client.FetchKBarHistory(product, year, month, limit);
    }

    public async Task<bool> SubscribePrice(string product, int year, int month)
    {
        var snapshot = await _client.SubscribePrice(product, year, month);
        return snapshot != null;
    }

    public Task<bool> UnsubscribePrice(string product, int year, int month)
    {
        return _client.UnsubscribePrice(product, year, month);
    }

    public bool QueueKBarImportFromFile(string path, out string? error)
    {
        return _client.QueueKBarImportFromFile(path, out error);
    }

    public bool TryDequeueKBarImport(out KBarImportBatch batch)
    {
        return _client.TryDequeueKBarImport(out batch);
    }

    public int PendingKBarImportCount => _client.PendingKBarImportCount;

    public Task<KBarImportResult?> UploadQueuedKBarImportAsync()
    {
        return _client.UploadQueuedKBarImportAsync();
    }

    public Task<KBarImportResult?> UploadKBarImportAsync(KBarImportBatch batch)
    {
        return _client.UploadKBarImportAsync(batch);
    }

    public Task<bool> SendMailAsync(string to, string subject, string body)
    {
        return _client.SendMailAsync(to, subject, body);
    }

    public global::ZClient.UserDataResult? QueryUserData()
    {
        return _client.QueryUserData();
    }

    public Task<bool> CheckDuplicateConnection(string account, string programName)
    {
        return _client.CheckDuplicateConnection(account, programName);
    }

    public Task Disconnect()
    {
        return _client.Disconnect();
    }

    public async Task<bool> Register(string url, string id, string password, string name = "", string phone = "", string email = "", string lineId = "")
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log?.Invoke("Target URL is empty.");
            return false;
        }

        await _client.Connect(url);
        var ok = await _client.Register(id, password, name, phone, email, lineId);
        await _client.Disconnect();
        return ok;
    }

    public async Task<bool> CheckRegisterId(string url, string id)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log?.Invoke("Target URL is empty.");
            return false;
        }

        await _client.Connect(url);
        var ok = await _client.CheckRegisterId(id);
        await _client.Disconnect();
        return ok;
    }

    public Task<bool> UpdateUserData(PermissionData permission, string? id = null, string? lineId = null, string? phone = null, string? email = null)
    {
        return _client.UpdateUserData(permission, id, lineId, phone, email);
    }

    public Task SendMarginBalance(string? id, double marginBalance)
    {
        return _client.SendMarginBalance(id, marginBalance);
    }
}
