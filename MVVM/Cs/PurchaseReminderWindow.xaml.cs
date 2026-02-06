using System.Windows;
using ZenPlatform.Core;

namespace ZenPlatform
{
    public partial class PurchaseReminderWindow : Window
    {
        private readonly UserInfoCtrl _userInfoCtrl;

        public PurchaseReminderWindow(string message, UserInfoCtrl userInfoCtrl)
        {
            _userInfoCtrl = userInfoCtrl;
            InitializeComponent();
            MessageText.Text = message;
        }

        private void OnSupportClick(object sender, RoutedEventArgs e)
        {
            var supportWindow = new SupportQrWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            supportWindow.ShowDialog();
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            _userInfoCtrl.RequestProgramStop();
            Close();
        }

        public static void Show(Window owner, string message, UserInfoCtrl userInfoCtrl)
        {
            var window = new PurchaseReminderWindow(message, userInfoCtrl)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.ShowDialog();
        }
    }
}
