using System.Windows;

namespace ZenPlatform
{
    public partial class MessageBoxWindow : Window
    {
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

        public static void Show(Window owner, string message, string title = "訊息", string okText = "確定")
        {
            var window = new MessageBoxWindow(message, title, okText)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.ShowDialog();
        }
    }
}
