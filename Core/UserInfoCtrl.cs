using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ZClient;

namespace ZenPlatform.Core
{
    public sealed record UserAccountInfo(string LoginId, PermissionData Permission);

    [SupportedOSPlatform("windows")]
    public class UserInfoCtrl
    {
        public const string DefaultGuestId = "A123456789";
        public const string DefaultGuestPassword = "A123456789";
        private const string SnapshotFileName = "UserData.json";
        private const string RegistryRoot = "Software\\Magistock\\TxNo2";
        private const string FirstRunKey = "FirstRunDate";
        private const int TrialDays = 30;

        private readonly ZClient.ZClient _client;
        private string? _targetUrl;
        private string? _loginId;
        private string? _password;
        private UserInfo? _userInfo;
        private bool _isGuest = true;
        private bool _isMagistockLoginSuccess;
        private bool _isBrokerLoginSuccess;
        private bool _purchaseReminderShown;
        private bool _isProgramStopped;
        private string? _lastUserAccountInfoKey;

        public event Action<string>? PurchaseReminderNeeded;
        public event Action<bool>? ProgramStoppedChanged;
        public event Action<UserAccountInfo>? UserAccountInfoFetched;
        public event Action? LoginStateChanged;

        public bool IsConnected => _client.IsConnected;
        public bool IsLoggedIn => _client.IsLoggedIn;
        public bool IsGuest => _isGuest;
        public bool IsGuestLogin => _isGuest;
        public bool IsUserLogin => !_isGuest;
        public bool IsRealUserLogin => !_isGuest && IsLoggedIn;
        public bool IsMagistockLoginSuccess => _isMagistockLoginSuccess;
        public bool IsBrokerLoginSuccess => _isBrokerLoginSuccess;
        public bool IsBrokerLoginAccept => _isMagistockLoginSuccess && _isBrokerLoginSuccess;
        public bool IsProgramStopped => _isProgramStopped;
        public UserDataResult? LastUserData { get; private set; }
        public string? LoginId => _loginId;

        public Task SendMarginBalanceAsync(double marginBalance)
        {
            return _client.SendMarginBalance(_loginId, marginBalance);
        }

        public string TargetUrl
        {
            get => _targetUrl ?? DefaultTargetUrl;
            set => _targetUrl = value;
        }

        public UserInfoCtrl(ZClient.ZClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.UserDataReceived += OnUserDataReceived;
        }

        public static string DefaultTargetUrl => ResolveDefaultTargetUrl();

        private static string ResolveDefaultTargetUrl()
        {
#if DEBUG
            var overrideUrl = Environment.GetEnvironmentVariable("ZSERVER_TARGET_URL");
            if (!string.IsNullOrWhiteSpace(overrideUrl))
                return overrideUrl.Trim();
#endif
            return "wss://zserver.magistock.com/ws";
        }

        public void SetBrokerLoginSuccess(bool isSuccess)
        {
            _isBrokerLoginSuccess = isSuccess;
        }

        public void UseGuestProfile(string programName, string programVersion)
        {
            _loginId = DefaultGuestId;
            _password = DefaultGuestPassword;
            _userInfo = new UserInfo
            {
                ProgName = programName ?? string.Empty,
                ProgVersion = programVersion ?? string.Empty
            };
            _client.SetUserInfo(_userInfo);
            _isGuest = true;
        }

        public void SetUserProfile(string id, string password, string programName, string programVersion)
        {
            _loginId = id?.Trim();
            _password = password;
            _userInfo = new UserInfo
            {
                ProgName = programName ?? string.Empty,
                ProgVersion = programVersion ?? string.Empty
            };
            _client.SetUserInfo(_userInfo);
        }

        public Task<bool> InitializeGuestAsync(string programName, string programVersion, CancellationToken cancellationToken = default)
        {
            UseGuestProfile(programName, programVersion);
            return ConnectAndLoginAsync(cancellationToken);
        }

        public Task<bool> StartupAsync(string programName, string programVersion, CancellationToken cancellationToken = default)
        {
            return StartupInternalAsync(programName, programVersion, cancellationToken);
        }

        public Task<bool> LoginUserAsync(string id, string password, string programName, string programVersion, CancellationToken cancellationToken = default)
        {
            return LoginUserInternalAsync(id, password, programName, programVersion, cancellationToken);
        }

        private async Task<bool> StartupInternalAsync(string programName, string programVersion, CancellationToken cancellationToken)
        {
            EnsureFirstRunDate();
            if (TryLoadSnapshot(out var snapshot) && snapshot != null)
            {
                var ok = await LoginUserInternalAsync(snapshot.LoginId, snapshot.LoginPassword, programName, programVersion, cancellationToken)
                    .ConfigureAwait(false);
                if (ok)
                {
                    return true;
                }

                if (snapshot.Data != null)
                {
                    ApplySnapshotData(snapshot.Data, programName, programVersion);
                    return true;
                }
            }

            return await InitializeGuestAsync(programName, programVersion, cancellationToken).ConfigureAwait(false);
        }

        private void EnsureFirstRunDate()
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(RegistryRoot);
                if (key == null)
                {
                    return;
                }

                var existing = key.GetValue(FirstRunKey) as string;
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return;
                }

                key.SetValue(FirstRunKey, DateTime.Today.ToString("yyyy-MM-dd"), RegistryValueKind.String);
            }
            catch
            {
                // Ignore registry failures.
            }
        }

        private bool TryGetFirstRunDate(out DateTime date)
        {
            date = default;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegistryRoot);
                if (key == null)
                {
                    return false;
                }

                var value = key.GetValue(FirstRunKey) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                return DateTime.TryParse(value, out date);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> LoginUserInternalAsync(string id, string password, string programName, string programVersion, CancellationToken cancellationToken)
        {
            SetUserProfile(id, password, programName, programVersion);
            _isGuest = false;
            var ok = await ConnectAndLoginAsync(cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                return true;
            }

            if (TryLoadSnapshot(out var snapshot) && snapshot?.Data != null)
            {
                ApplySnapshotData(snapshot.Data, programName, programVersion);
                return true;
            }

            _isGuest = true;
            UseGuestProfile(programName, programVersion);
            if (!_client.IsLoggedIn)
            {
                await ConnectAndLoginAsync(cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private async Task<bool> ConnectAndLoginAsync(CancellationToken cancellationToken)
        {
            var url = TargetUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_loginId) || _password == null)
            {
                return false;
            }

            await _client.Connect(url).ConfigureAwait(false);
            var ok = await _client.Login(_loginId, _password).ConfigureAwait(false);
            _isMagistockLoginSuccess = ok;
            if (!ok)
            {
                LoginStateChanged?.Invoke();
                return false;
            }

            if (_userInfo != null)
            {
                await _client.SendUserInfo(_userInfo).ConfigureAwait(false);
            }

            if (!_isGuest)
            {
                await FetchUserData().ConfigureAwait(false);
            }

            EvaluatePurchaseReminder();
            LoginStateChanged?.Invoke();
            return true;
        }

        public async Task<bool> FetchUserData()
        {
            var ok = await _client.FetchUserData();
            LastUserData = _client.QueryUserData();
            if (LastUserData != null)
            {
                TryRaiseUserAccountInfoFetched();
                var json = JsonSerializer.Serialize(LastUserData, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
                if (!_isGuest)
                {
                    TrySaveSnapshot(json);
                    UpdateProgramStoppedFlag();
                }
            }
            else
            {
            }
            return ok;
        }

        private void OnUserDataReceived(UserDataResult data)
        {
            LastUserData = data;
            TryRaiseUserAccountInfoFetched();
        }

        private void TryRaiseUserAccountInfoFetched()
        {
            if (_isGuest || _userInfo == null || string.IsNullOrWhiteSpace(_loginId))
            {
                return;
            }

            var permission = LastUserData?.Permissions?.FirstOrDefault(p =>
                string.Equals(p.ProgramName, _userInfo.ProgName, StringComparison.OrdinalIgnoreCase));
            if (permission == null)
            {
                return;
            }

            var key = $"{_loginId}|{permission.Account}|{permission.BrokerPassword}|{permission.BranchName}";
            if (string.Equals(_lastUserAccountInfoKey, key, StringComparison.Ordinal))
            {
                return;
            }

            _lastUserAccountInfoKey = key;
            UserAccountInfoFetched?.Invoke(new UserAccountInfo(_loginId, permission));
        }

        private void TrySaveSnapshot(string json)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SnapshotFileName);
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, protectedBytes);
            }
            catch
            {
                // Ignore snapshot write failures.
            }
        }

        private void EvaluatePurchaseReminder()
        {
            if (_purchaseReminderShown)
            {
                return;
            }

            if (!TryGetFirstRunDate(out var firstRunDate))
            {
                return;
            }

            if ((DateTime.Today - firstRunDate).TotalDays < TrialDays)
            {
                return;
            }

            if (HasValidPermission())
            {
                return;
            }

            _purchaseReminderShown = true;
            PurchaseReminderNeeded?.Invoke("您的試用期已超過 30 天且尚未購買。為避免功能受限，請盡快完成購買並啟用授權。完成購買後即可啟用完整下單功能。");
        }

        private bool HasValidPermission()
        {
            var permission = LastUserData?.Permissions?.FirstOrDefault(p =>
                string.Equals(p.ProgramName, _userInfo?.ProgName, StringComparison.OrdinalIgnoreCase));
            if (permission == null)
            {
                return false;
            }

            if (permission.UnlimitedPermission)
            {
                return true;
            }

            return permission.PermissionCount > 0;
        }

        private void UpdateProgramStoppedFlag()
        {
            var permission = LastUserData?.Permissions?.FirstOrDefault(p =>
                string.Equals(p.ProgramName, _userInfo?.ProgName, StringComparison.OrdinalIgnoreCase));
            if (permission == null)
            {
                return;
            }

            if (permission.UnlimitedExpiry)
            {
                SetProgramStopped(false);
                return;
            }

            var expireText = permission.ProgramExpireAt ?? string.Empty;
            if (DateTime.TryParse(expireText, out var expireDate))
            {
                if (expireDate.Date >= DateTime.Today)
                {
                    SetProgramStopped(false);
                }
            }
        }

        private void SetProgramStopped(bool value)
        {
            if (_isProgramStopped == value)
            {
                return;
            }

            _isProgramStopped = value;
            ProgramStoppedChanged?.Invoke(value);
        }

        private void TryDeleteSnapshot()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SnapshotFileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore snapshot delete failures.
            }
        }

        private bool TryLoadSnapshot(out UserDataSnapshot? snapshot)
        {
            snapshot = null;
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SnapshotFileName);
                if (!File.Exists(path))
                {
                    return false;
                }

                var protectedBytes = File.ReadAllBytes(path);
                var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(jsonBytes);
                var data = JsonSerializer.Deserialize<UserDataResult>(json);
                if (data?.User == null)
                {
                    return false;
                }

                var loginId = data.User.Id ?? string.Empty;
                var loginPassword = data.User.Password ?? string.Empty;
                if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(loginPassword))
                {
                    return false;
                }

                snapshot = new UserDataSnapshot(loginId, loginPassword, data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ApplySnapshotData(UserDataResult data, string programName, string programVersion)
        {
            _isGuest = false;
            _isMagistockLoginSuccess = false;
            _lastUserAccountInfoKey = null;
            _loginId = data.User?.Id ?? _loginId;
            _password = data.User?.Password ?? _password;
            _userInfo = new UserInfo
            {
                ProgName = programName ?? string.Empty,
                ProgVersion = programVersion ?? string.Empty
            };
            _client.SetUserInfo(_userInfo);

            LastUserData = data;
            TryRaiseUserAccountInfoFetched();
            UpdateProgramStoppedFlag();
            EvaluatePurchaseReminder();
        }

        private sealed class UserDataSnapshot
        {
            public string LoginId { get; }
            public string LoginPassword { get; }
            public UserDataResult? Data { get; }

            public UserDataSnapshot(string loginId, string loginPassword, UserDataResult? data)
            {
                LoginId = loginId;
                LoginPassword = loginPassword;
                Data = data;
            }
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

        public UserDataResult? QueryUserData()
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

        public Task<bool> LogoutToGuestAsync(string programName, string programVersion, CancellationToken cancellationToken = default)
        {
            return LogoutToGuestInternalAsync(programName, programVersion, cancellationToken);
        }

        public void RequestProgramStop()
        {
            SetProgramStopped(true);
        }

        private async Task<bool> LogoutToGuestInternalAsync(string programName, string programVersion, CancellationToken cancellationToken)
        {
            try
            {
                await _client.Disconnect().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disconnect failures.
            }

            _isGuest = true;
            _isMagistockLoginSuccess = false;
            TryDeleteSnapshot();
            var ok = await InitializeGuestAsync(programName, programVersion, cancellationToken).ConfigureAwait(false);
            LoginStateChanged?.Invoke();
            return ok;
        }

        public async Task<bool> Register(string url, string id, string password, string name = "", string phone = "", string email = "", string lineId = "")
        {
            if (string.IsNullOrWhiteSpace(url))
            {
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
                return false;
            }

            await _client.Connect(url);
            var ok = await _client.CheckRegisterId(id);
            await _client.Disconnect();
            return ok;
        }

        public Task<bool> UpdateUserData(PermissionData? permission, string? id = null, string? name = null, string? lineId = null, string? phone = null, string? email = null, string? oldPassword = null, string? newPassword = null, string? oldBrokerPassword = null, string? newBrokerPassword = null)
        {
            if (permission == null)
            {
                return Task.FromResult(false);
            }
            return _client.UpdateUserData(permission, id, name, lineId, phone, email, oldPassword, newPassword, oldBrokerPassword, newBrokerPassword);
        }

        public Task SendMarginBalance(string? id, double marginBalance)
        {
            return _client.SendMarginBalance(id, marginBalance);
        }

        public string LastMailError => _client.LastMailError;
        public string LastRegisterError => _client.LastRegisterError;
        public string LastCheckIdError => _client.LastCheckIdError;
        public string LastUpdateError => _client.LastUpdateError;
    }
}
