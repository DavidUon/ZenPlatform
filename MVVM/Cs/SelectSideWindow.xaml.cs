using System.Windows;
namespace ZenPlatform
{
    public partial class SelectSideWindow : Window
    {
        public SelectSideWindow()
        {
            InitializeComponent();
        }

        public bool? SelectedIsBuy { get; private set; }

        private void OnLongClick(object sender, RoutedEventArgs e)
        {
            SelectedIsBuy = true;
            DialogResult = true;
            Close();
        }

        private void OnShortClick(object sender, RoutedEventArgs e)
        {
            SelectedIsBuy = false;
            DialogResult = true;
            Close();
        }

    }
}
