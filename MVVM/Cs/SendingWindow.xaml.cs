using System.Windows;

namespace ZenPlatform
{
    public partial class SendingWindow : Window
    {
        public SendingWindow(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }
    }
}
