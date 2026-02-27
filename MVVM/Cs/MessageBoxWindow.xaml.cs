using System.Windows;

namespace ZenPlatform
{
    public partial class MessageBoxWindow : Window
    {
        private System.Action? _extraAction;

        public MessageBoxWindow(string message, string title = "訊息", string okText = "確定")
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            OkButton.Content = string.IsNullOrWhiteSpace(okText) ? "確定" : okText;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnExtraClick(object sender, RoutedEventArgs e)
        {
            _extraAction?.Invoke();
        }

        public static void Show(Window owner, string message, string title = "訊息", string okText = "確定")
        {
            var window = new MessageBoxWindow(message, title, okText)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.ShowDialog();
        }

        public static void ShowWithExtra(Window owner, string message, string title, string okText, string extraText, System.Action extraAction)
        {
            var window = new MessageBoxWindow(message, title, okText)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                _extraAction = extraAction
            };
            window.ExtraButton.Content = extraText;
            window.ExtraButton.Visibility = Visibility.Visible;
            window.ShowDialog();
        }
    }
}
