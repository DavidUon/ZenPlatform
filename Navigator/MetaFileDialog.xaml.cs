using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Navigator
{
    public partial class MetaFileDialog : Window
    {
        public enum DialogMode
        {
            Export,
            Import
        }

        public DialogMode Mode { get; }
        public string DirectoryPath => PathBox.Text.Trim();
        public string FileName => FileBox.Text.Trim();
        public string FullPath => Path.Combine(DirectoryPath, FileName);

        public MetaFileDialog(DialogMode mode, string? defaultDir, string? defaultFile)
        {
            InitializeComponent();
            Mode = mode;
            Title = mode == DialogMode.Export ? "匯出中繼檔" : "匯入中繼檔";
            OkButton.Content = mode == DialogMode.Export ? "匯出" : "匯入";
            PathBox.Text = defaultDir ?? string.Empty;
            FileBox.Text = defaultFile ?? string.Empty;

            BrowseButton.Click += (_, __) => Browse();
            OkButton.Click += (_, __) => Confirm();
        }

        private void Browse()
        {
            var open = new OpenFileDialog
            {
                Title = "選擇資料夾",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "選擇資料夾"
            };
            if (open.ShowDialog(this) == true)
            {
                PathBox.Text = Path.GetDirectoryName(open.FileName) ?? string.Empty;
            }
        }

        private void Confirm()
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath) || string.IsNullOrWhiteSpace(FileName))
            {
                MessageBox.Show(this, "請輸入路徑與檔案名稱", "Navigator");
                return;
            }

            if (Mode == DialogMode.Export)
            {
                Directory.CreateDirectory(DirectoryPath);
            }

            DialogResult = true;
        }
    }
}
