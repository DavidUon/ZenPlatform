using System.Windows;

namespace ZenPlatform
{
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow(string message, string title = "確認")
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
        }

        private void OnYesClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnNoClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public static bool Show(Window owner, string message, string title = "確認")
        {
            var window = new ConfirmWindow(message, title)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            return window.ShowDialog() == true;
        }
    }
}
