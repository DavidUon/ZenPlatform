using System.Windows;

namespace TaifexHisDbManager
{
    internal partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        public void SetProgress(int current, int total, string? message = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProgress(current, total, message));
                return;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                MessageText.Text = message;
            }

            if (total <= 0)
            {
                MainProgressBar.Value = 0;
                PercentText.Text = "0%";
                return;
            }

            if (current < 0) current = 0;
            if (current > total) current = total;
            var percent = (int)System.Math.Round(current * 100.0 / total);
            MainProgressBar.Value = percent;
            PercentText.Text = $"{percent}%";
        }
    }
}
