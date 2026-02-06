using System.Windows;

namespace ZenPlatform
{
    public partial class InfoWindow : Window
    {
        public bool LogoutRequested { get; private set; }
        public bool ModifyRequested { get; private set; }
        public bool ChangePasswordRequested { get; private set; }

        public InfoWindow(string title, string header, string body, bool canChangePassword, bool canLogout)
        {
            InitializeComponent();
            Title = title;
            HeaderText.Text = header;
            BodyText.Text = body;
            ChangePasswordButton.IsEnabled = canChangePassword;
            LogoutButton.Visibility = canLogout ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            LogoutRequested = true;
            DialogResult = true;
            Close();
        }

        private void OnModifyProfileClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ModifyRequested = true;
            DialogResult = true;
            Close();
        }

        private void OnChangePasswordClick(object sender, RoutedEventArgs e)
        {
            ChangePasswordRequested = true;
            DialogResult = true;
            Close();
        }

        public static bool Show(Window owner, string title, string header, string body, bool canChangePassword, bool canLogout, out bool logoutRequested, out bool modifyRequested, out bool changePasswordRequested)
        {
            var window = new InfoWindow(title, header, body, canChangePassword, canLogout)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.ShowDialog();
            logoutRequested = window.LogoutRequested;
            modifyRequested = window.ModifyRequested;
            changePasswordRequested = window.ChangePasswordRequested;
            return window.DialogResult == true;
        }
    }
}
