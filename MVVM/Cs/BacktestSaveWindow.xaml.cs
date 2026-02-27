using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ZenPlatform
{
    public partial class BacktestSaveWindow : Window
    {
        public BacktestSaveWindow(string folderPath, string fileName)
        {
            InitializeComponent();
            FolderTextBox.Text = folderPath ?? string.Empty;
            FileNameTextBox.Text = fileName ?? string.Empty;
        }

        public string FolderPath => FolderTextBox.Text.Trim();
        public string FileName => FileNameTextBox.Text.Trim();

        public string FullPath
        {
            get
            {
                var folder = FolderPath;
                var file = FileName;
                return Path.Combine(folder, file);
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "選擇存放資料夾",
                InitialDirectory = Directory.Exists(FolderPath) ? FolderPath : null
            };

            if (dialog.ShowDialog() == true)
            {
                FolderTextBox.Text = dialog.FolderName;
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                MessageBox.Show(this, "請輸入存放路徑。", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(FileName))
            {
                MessageBox.Show(this, "請輸入檔名。", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "檔名包含不合法字元。", "回測結果", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!FileName.EndsWith(".btdb", StringComparison.OrdinalIgnoreCase))
            {
                FileNameTextBox.Text = FileName + ".btdb";
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
