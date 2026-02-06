using System;
using System.Threading.Tasks;
using System.Windows;
using ZenPlatform.Core;

namespace ZenPlatform
{
    public partial class RegisterWindow : Window
    {
        private readonly UserInfoCtrl _userInfoCtrl;
        private readonly string _programName;
        private readonly string _programVersion;
        private const string VerificationSubject = "Magistock 註冊驗證碼";
        private static readonly TimeSpan VerificationValidFor = TimeSpan.FromMinutes(5);
        private string? _verificationCode;
        private DateTimeOffset? _verificationSentAt;

        public RegisterWindow(UserInfoCtrl userInfoCtrl, string programName, string programVersion)
        {
            _userInfoCtrl = userInfoCtrl;
            _programName = programName;
            _programVersion = programVersion;
            InitializeComponent();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(IdTextBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入身分證字號。", "提示");
                IdTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入姓名。", "提示");
                NameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(EmailTextBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入 Email。", "提示");
                EmailTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(VerificationCodeTextBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入驗證碼。", "提示");
                VerificationCodeTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_verificationCode) || _verificationSentAt == null)
            {
                MessageBoxWindow.Show(this, "請先發送驗證信。", "提示");
                return;
            }

            if (!string.Equals(_verificationCode, VerificationCodeTextBox.Text.Trim(), StringComparison.Ordinal))
            {
                MessageBoxWindow.Show(this, "驗證碼不正確。", "提示");
                VerificationCodeTextBox.Focus();
                return;
            }

            if (DateTimeOffset.Now - _verificationSentAt.Value > VerificationValidFor)
            {
                MessageBoxWindow.Show(this, "驗證碼已過期，請重新發送。", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(PhoneTextBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入電話。", "提示");
                PhoneTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(LineIdTextBox.Text))
            {
                MessageBoxWindow.Show(this, "請輸入 Line ID。", "提示");
                LineIdTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                MessageBoxWindow.Show(this, "請輸入密碼。", "提示");
                PasswordBox.Focus();
                return;
            }

            if (PasswordBox.Password != ConfirmPasswordBox.Password)
            {
                MessageBoxWindow.Show(this, "兩次輸入的密碼不相符。", "提示");
                ConfirmPasswordBox.Focus();
                ConfirmPasswordBox.SelectAll();
                return;
            }

            var confirmMessage =
                "請確認以下資訊是否正確：\n\n" +
                $"身分證字號：{IdTextBox.Text}\n" +
                $"姓名：{NameTextBox.Text}\n" +
                $"Email：{EmailTextBox.Text}\n" +
                $"電話：{PhoneTextBox.Text}\n" +
                $"Line ID：{LineIdTextBox.Text}\n" +
                "確認後將綁定帳號。\n是否繼續？";

            if (!ConfirmWindow.Show(this, confirmMessage, "確認註冊資訊"))
            {
                return;
            }

            var ok = await _userInfoCtrl.Register(
                UserInfoCtrl.DefaultTargetUrl,
                IdTextBox.Text.Trim(),
                PasswordBox.Password,
                NameTextBox.Text.Trim(),
                PhoneTextBox.Text.Trim(),
                EmailTextBox.Text.Trim(),
                LineIdTextBox.Text.Trim());

            if (!ok)
            {
                var errorMessage = string.Equals(_userInfoCtrl.LastRegisterError, "duplicate_id", StringComparison.OrdinalIgnoreCase)
                    ? "此身分證字號已經使用過。"
                    : "註冊失敗，請稍後再試。";
                MessageBoxWindow.Show(this, errorMessage, "提示");
                return;
            }

            var loginOk = await _userInfoCtrl.LoginUserAsync(
                IdTextBox.Text.Trim(),
                PasswordBox.Password,
                _programName,
                _programVersion);
            if (!loginOk)
            {
                MessageBoxWindow.Show(this, "註冊成功，但自動登入失敗，請手動登入。", "提示");
            }

            DialogResult = true;
            Close();
        }

        private void OnSendVerificationClick(object sender, RoutedEventArgs e)
        {
            _ = SendVerificationAsync();
        }

        private async Task SendVerificationAsync()
        {
            var sendingWindow = new SendingWindow("正在發送驗證信，請稍候...");
            sendingWindow.Owner = this;
            sendingWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            sendingWindow.Show();

            if (string.IsNullOrWhiteSpace(IdTextBox.Text))
            {
                sendingWindow.Close();
                MessageBoxWindow.Show(this, "請先輸入身分證字號。", "提示");
                IdTextBox.Focus();
                return;
            }

            var normalizedId = IdTextBox.Text.Trim().ToUpperInvariant();
            if (!IsValidTaiwanId(normalizedId))
            {
                sendingWindow.Close();
                MessageBoxWindow.Show(this, "身分證字號格式不正確。", "提示");
                IdTextBox.Focus();
                IdTextBox.SelectAll();
                return;
            }

            if (string.IsNullOrWhiteSpace(EmailTextBox.Text))
            {
                sendingWindow.Close();
                MessageBoxWindow.Show(this, "請先輸入 Email。", "提示");
                EmailTextBox.Focus();
                return;
            }

            var idAvailable = await _userInfoCtrl.CheckRegisterId(UserInfoCtrl.DefaultTargetUrl, IdTextBox.Text.Trim());
            if (!idAvailable)
            {
                sendingWindow.Close();
                var errorMessage = string.Equals(_userInfoCtrl.LastCheckIdError, "duplicate_id", StringComparison.OrdinalIgnoreCase)
                    ? "此身分證字號已經使用過。"
                    : "檢查身分證字號失敗，請稍後再試。";
                MessageBoxWindow.Show(this, errorMessage, "提示");
                IdTextBox.Focus();
                IdTextBox.SelectAll();
                return;
            }

            var code = Random.Shared.Next(0, 10000).ToString("D4");
            var body =
$@"您好，

這是一封由 Magistock 系統寄出的註冊驗證信，用於確認 Email 擁有權。

帳號（身分證字號）：{IdTextBox.Text}
驗證碼：

    {code}

請在註冊畫面輸入上方驗證碼以完成註冊。
驗證碼有效時間 5 分鐘，逾時需重新申請。

若您未申請註冊，請忽略此信。

適安科技";

            var ok = await _userInfoCtrl.SendMailAsync(EmailTextBox.Text.Trim(), VerificationSubject, body);
            if (!ok)
            {
                sendingWindow.Close();
                var error = string.IsNullOrWhiteSpace(_userInfoCtrl.LastMailError) ? "未知錯誤" : _userInfoCtrl.LastMailError;
                MessageBoxWindow.Show(this, $"驗證信發送失敗：{error}", "提示");
                return;
            }

            _verificationCode = code;
            _verificationSentAt = DateTimeOffset.Now;
            sendingWindow.Close();
            MessageBoxWindow.Show(this, "驗證信已送出，請至信箱收信。", "提示");
        }

        private static bool IsValidTaiwanId(string id)
        {
            if (id.Length != 10)
                return false;

            var letter = id[0];
            if (letter < 'A' || letter > 'Z')
                return false;

            var digits = id.AsSpan(1);
            if (digits[0] != '1' && digits[0] != '2')
                return false;

            for (var i = 0; i < digits.Length; i++)
            {
                if (!char.IsDigit(digits[i]))
                    return false;
            }

            const string map = "ABCDEFGHJKLMNPQRSTUVXYWZIO";
            var codeIndex = map.IndexOf(letter);
            if (codeIndex < 0)
                return false;

            var code = codeIndex + 10;
            var sum = (code / 10) + (code % 10) * 9;
            var weights = new[] { 8, 7, 6, 5, 4, 3, 2, 1, 1 };
            for (var i = 0; i < weights.Length; i++)
            {
                sum += (digits[i] - '0') * weights[i];
            }

            return sum % 10 == 0;
        }
    }
}
