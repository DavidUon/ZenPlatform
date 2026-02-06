using System.Windows;
using ZenPlatform.Strategy;

namespace ZenPlatform
{
    public partial class SessionEntryWindow : Window
    {
        public SessionEntryWindow()
        {
            InitializeComponent();
        }

        public SessionSide? SelectedSide { get; private set; }

        private void OnLongClick(object sender, RoutedEventArgs e)
        {
            SelectedSide = SessionSide.Long;
            DialogResult = true;
        }

        private void OnShortClick(object sender, RoutedEventArgs e)
        {
            SelectedSide = SessionSide.Short;
            DialogResult = true;
        }
    }
}
