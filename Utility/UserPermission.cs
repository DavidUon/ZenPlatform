using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ZClient;
using ZClientCtrl;

namespace Utility
{
    public sealed class UserProfile
    {
        public string LoginId { get; set; } = "";
        public string LoginPassword { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string LineId { get; set; } = "";
        public string BrokerName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string Account { get; set; } = "";
        public string BrokerPassword { get; set; } = "";
        public string PermissionText { get; set; } = "";
        public string ExpireText { get; set; } = "";
    }

    public class UserPermission
    {
        public const string DefaultGuestId = "A123456789";
        public const string DefaultGuestPassword = "A123456789";
        public static readonly string DefaultTargetUrl = ResolveDefaultTargetUrl();
        public const string SnapshotFileName = "UserProfileSnapshot.json";

        private readonly ZClientCtrl.ZClientCtrl _client = new();

        public event Action<string>? Log;

        public string TargetUrl { get; set; } = DefaultTargetUrl;
        public string ProgramName { get; set; } = "台指一號";
        public string ProgramVersion { get; set; } = "V2.1.1";

        public string LoginId { get; private set; } = DefaultGuestId;
        public string Password { get; private set; } = string.Empty;
        public bool IsGuest { get; private set; } = true;
        public bool IsConnected => _client.IsConnected;
        public bool IsLoggedIn => _client.IsLoggedIn;
        public UserProfile? LastProfile { get; private set; }
        public string LastUpdateError => _client.LastUpdateError;

        public UserPermission()
        {
            _client.Log += msg => Log?.Invoke(msg);
            _client.Message += msg => Log?.Invoke($"zclient: {msg}");
        }

        private static string ResolveDefaultTargetUrl()
        {
#if DEBUG
            return "ws://127.0.0.1:12362/ws";
#else
            return "wss://zserver.magistock.com/ws";
#endif
        }

        public void UseGuest()
        {
            LoginId = DefaultGuestId;
            Password = DefaultGuestPassword;
            IsGuest = true;
        }

        public void SetUserLogin(string id, string password)
        {
            LoginId = id?.Trim() ?? string.Empty;
            Password = password ?? string.Empty;
            IsGuest = false;
        }

        public bool TryUseSnapshotLogin(out UserProfile? snapshot)
        {
            snapshot = null;
            if (!TryLoadSnapshot(out var loaded))
                return false;

            if (string.IsNullOrWhiteSpace(loaded?.LoginId) || string.IsNullOrWhiteSpace(loaded?.LoginPassword))
            {
                snapshot = loaded;
                return false;
            }

            SetUserLogin(loaded.LoginId, loaded.LoginPassword);
            snapshot = loaded;
            return true;
        }

        public async Task<bool> LoginAsync()
        {
            _client.SetTargetUrl(TargetUrl);
            _client.SetProfile(LoginId, Password, new UserInfo
            {
                ProgName = ProgramName,
                ProgVersion = ProgramVersion
            });

            await _client.Connect();
            return _client.IsLoggedIn;
        }

        public async Task<UserProfile?> FetchUserProfileAsync()
        {
            var ok = await _client.FetchUserData();
            var data = _client.QueryUserData();
            if (!ok || data?.User == null)
            {
                if (IsGuest)
                {
                    LastProfile = new UserProfile
                    {
                        LoginId = LoginId,
                        LoginPassword = Password,
                        Id = "",
                        Name = "試用者",
                        BrokerName = "---",
                        BranchName = "---",
                        PermissionText = "---",
                        Account = "---",
                        ExpireText = "---"
                    };
                    return LastProfile;
                }

                if (TryLoadSnapshot(out var snapshot))
                {
                    LastProfile = snapshot;
                    return LastProfile;
                }

                LastProfile = null;
                return null;
            }

            var user = data.User;
            var perm = data.Permissions.FirstOrDefault(p =>
                string.Equals(p.ProgramName, ProgramName, StringComparison.OrdinalIgnoreCase))
                ?? data.Permissions.FirstOrDefault();

            string permissionText = "---";
            string expireText = "---";
            string account = "---";
            string brokerName = "---";
            string branchName = "---";
            string brokerPassword = "";
            if (perm != null)
            {
                brokerName = string.IsNullOrWhiteSpace(perm.BrokerName) ? "---" : perm.BrokerName;
                branchName = string.IsNullOrWhiteSpace(perm.BranchName) ? "---" : perm.BranchName;
                account = string.IsNullOrWhiteSpace(perm.Account) ? "---" : perm.Account;
                brokerPassword = perm.BrokerPassword ?? "";
                permissionText = perm.UnlimitedPermission ? "最高權限" : perm.PermissionCount.ToString();
                expireText = perm.UnlimitedExpiry
                    ? "永久"
                    : (string.IsNullOrWhiteSpace(perm.ProgramExpireAt) ? "---" : perm.ProgramExpireAt);
            }

            LastProfile = new UserProfile
            {
                LoginId = LoginId,
                LoginPassword = Password,
                Id = user.Id ?? "",
                Name = user.Name ?? "",
                Email = user.Email ?? "",
                Phone = user.Phone ?? "",
                LineId = user.LineId ?? "",
                BrokerName = brokerName,
                BranchName = branchName,
                Account = account,
                BrokerPassword = brokerPassword,
                PermissionText = permissionText,
                ExpireText = expireText
            };

            if (!IsGuest)
            {
                TrySaveSnapshot(LastProfile);
            }

            return LastProfile;
        }

        public async Task<bool> UpdatePermissionAsync(string branchName, string futuresAccount, string futuresPassword, string lineId, string phone, string email)
        {
            var permission = new PermissionData
            {
                BrokerName = "康和",
                BranchName = branchName?.Trim() ?? "",
                Account = futuresAccount?.Trim() ?? "",
                BrokerPassword = futuresPassword ?? "",
                ProgramName = ProgramName,
                ProgramExpireAt = "",
                PermissionCount = 10,
                AllowConcurrent = false,
                IsBanned = false,
                UnlimitedExpiry = true,
                UnlimitedPermission = false
            };

            return await _client.UpdateUserData(permission, LoginId, lineId, phone, email);
        }

        public Task SendMarginBalanceAsync(double marginBalance)
        {
            if (IsGuest || !IsLoggedIn || string.IsNullOrWhiteSpace(LoginId))
                return Task.CompletedTask;

            return _client.SendMarginBalance(LoginId, marginBalance);
        }

        public bool TryLoadSnapshot(out UserProfile? profile)
        {
            profile = null;
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SnapshotFileName);
                if (!File.Exists(path))
                    return false;

                var protectedBytes = File.ReadAllBytes(path);
                var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(jsonBytes);
                profile = JsonSerializer.Deserialize<UserProfile>(json);
                return profile != null;
            }
            catch
            {
                return false;
            }
        }

        public void ClearSnapshot()
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
                // ignore snapshot delete failures
            }
        }

        private void TrySaveSnapshot(UserProfile profile)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SnapshotFileName);
                var json = JsonSerializer.Serialize(profile);
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, protectedBytes);
            }
            catch
            {
                // ignore snapshot write failures
            }
        }

        public ZClientCtrl.ZClientCtrl Client => _client;
    }
}
