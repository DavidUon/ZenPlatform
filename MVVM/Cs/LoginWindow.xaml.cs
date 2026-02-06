using System.Windows;
using System.Windows.Input;

namespace ZenPlatform
{
    public partial class LoginWindow : Window
    {
        public string UserId { get; private set; } = string.Empty;
        public string Password { get; private set; } = string.Empty;
        private readonly ZenPlatform.Core.UserInfoCtrl _userInfoCtrl;
        private readonly string _programName;
        private readonly string _programVersion;

        public LoginWindow(ZenPlatform.Core.UserInfoCtrl userInfoCtrl, string programName, string programVersion)
        {
            _userInfoCtrl = userInfoCtrl;
            _programName = programName;
            _programVersion = programVersion;
            InitializeComponent();
        }

        private void OnLoginClick(object sender, RoutedEventArgs e)
        {
            UserId = UserIdBox.Text?.Trim() ?? string.Empty;
            Password = PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnRegisterClick(object sender, MouseButtonEventArgs e)
        {
            var ownerWindow = Owner;
            Close();

            var registerWindow = new RegisterWindow(_userInfoCtrl, _programName, _programVersion)
            {
                Owner = ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            registerWindow.ShowDialog();
        }
    }
}
