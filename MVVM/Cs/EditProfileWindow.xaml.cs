using System;
using System.Threading.Tasks;
using System.Windows;
using ZenPlatform.Core;
using ZClient;

namespace ZenPlatform
{
    public partial class EditProfileWindow : Window
    {
        private readonly UserInfoCtrl _userInfoCtrl;
        private readonly PermissionData? _permission;
        private readonly string _userId;

        public EditProfileWindow(UserInfoCtrl userInfoCtrl, string userId, PermissionData? permission, UserData? user)
        {
            _userInfoCtrl = userInfoCtrl;
            _permission = permission;
            _userId = userId;

            InitializeComponent();
            EmailBox.Text = user?.Email ?? string.Empty;
            NameBox.Text = user?.Name ?? string.Empty;
            PhoneBox.Text = user?.Phone ?? string.Empty;
            LineIdBox.Text = user?.LineId ?? string.Empty;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EmailBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入 Email。", "提示");
                EmailBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入姓名。", "提示");
                NameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(PhoneBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入電話。", "提示");
                PhoneBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(LineIdBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入 Line ID。", "提示");
                LineIdBox.Focus();
                return;
            }

            var oldPassword = OldPasswordBox.Password;
            var newPassword = NewPasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;

            if (!string.IsNullOrWhiteSpace(newPassword) || !string.IsNullOrWhiteSpace(confirmPassword))
            {
                if (string.IsNullOrWhiteSpace(oldPassword))
                {
                    MessageBoxWindow.Show(this, "請輸入舊密碼。", "提示");
                    OldPasswordBox.Focus();
                    return;
                }

                if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
                {
                    MessageBoxWindow.Show(this, "新密碼與確認密碼不一致。", "提示");
                    ConfirmPasswordBox.Focus();
                    ConfirmPasswordBox.SelectAll();
                    return;
                }
            }
            else
            {
                oldPassword = null;
                newPassword = null;
            }

            var ok = await _userInfoCtrl.UpdateUserData(
                _permission,
                _userId,
                NameBox.Text.Trim(),
                LineIdBox.Text.Trim(),
                PhoneBox.Text.Trim(),
                EmailBox.Text.Trim(),
                oldPassword,
                newPassword);

            if (!ok)
            {
                var reason = _userInfoCtrl.LastUpdateError;
                var message = reason switch
                {
                    "invalid_password" => "舊密碼錯誤。",
                    "missing_password" => "密碼欄位不足。",
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
