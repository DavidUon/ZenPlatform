using System;
using System.Windows;
using ZenPlatform.Core;
using ZClient;

namespace ZenPlatform
{
    public partial class ChangeBrokerPasswordWindow : Window
    {
        private readonly UserInfoCtrl _userInfoCtrl;
        private readonly PermissionData? _permission;
        private readonly string _userId;

        public ChangeBrokerPasswordWindow(UserInfoCtrl userInfoCtrl, string userId, PermissionData? permission)
        {
            _userInfoCtrl = userInfoCtrl;
            _permission = permission;
            _userId = userId;
            InitializeComponent();
            BrokerPasswordBox.Text = permission?.BrokerPassword ?? string.Empty;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_permission == null)
            {
                MessageBoxWindow.Show(this, "無法取得期貨帳號資訊。", "提示");
                return;
            }

            var oldPassword = _permission.BrokerPassword ?? string.Empty;
            var newPassword = BrokerPasswordBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(oldPassword))
            {
                MessageBoxWindow.Show(this, "無舊期貨密碼可供驗證。", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                MessageBoxWindow.Show(this, "請輸入期貨密碼。", "提示");
                BrokerPasswordBox.Focus();
                return;
            }

            var ok = await _userInfoCtrl.UpdateUserData(
                _permission,
                _userId,
                oldBrokerPassword: oldPassword,
                newBrokerPassword: newPassword);

            if (!ok)
            {
                var reason = _userInfoCtrl.LastUpdateError;
                var message = reason switch
                {
                    "invalid_broker_password" => "舊期貨密碼錯誤。",
                    "missing_broker_password" => "密碼欄位不足。",
                    _ => "更新失敗，請稍後再試。"
                };
                MessageBoxWindow.Show(this, message, "提示");
                return;
            }

            await _userInfoCtrl.FetchUserData();
            MessageBoxWindow.Show(this, "更新完成。", "提示");
            DialogResult = true;
            Close();
        }
    }
}
