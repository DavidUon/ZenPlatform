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

            var brokerPassword = BrokerPasswordBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(brokerPassword))
            {
                MessageBoxWindow.Show(this, "請輸入期貨密碼。", "提示");
                BrokerPasswordBox.Focus();
                return;
            }

            _permission.BrokerPassword = brokerPassword;
            var user = _userInfoCtrl.LastUserData?.User;
            var ok = await _userInfoCtrl.UpdateUserData(
                _permission,
                _userId,
                user?.Name,
                user?.LineId,
                user?.Phone,
                user?.Email);

            if (!ok)
            {
                var reason = _userInfoCtrl.LastUpdateError;
                var message = string.IsNullOrWhiteSpace(reason)
                    ? "更新失敗，請稍後再試。"
                    : $"更新失敗：{reason}";
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
