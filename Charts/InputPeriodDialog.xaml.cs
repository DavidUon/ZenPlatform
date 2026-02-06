using System;
using System.Windows;

namespace Charts
{
    public partial class InputPeriodDialog : Window
    {
        public int Period { get; private set; } = 0;
        public InputPeriodDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PeriodBox.Text, out var p) || p <= 0)
            {
                MessageBox.Show("期間需為正整數");
                return;
            }
            Period = p; DialogResult = true;
        }
    }
}

