using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace TaifexHisDbManager
{
    internal partial class ImportRunWindow : Window
    {
        private string? _sourceFolder;
        private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "import_settings.json");

        public ImportRunWindow(string dbOutputFolder)
        {
            InitializeComponent();
            _ = dbOutputFolder; // 保留既有呼叫介面，實際輸出路徑統一由系統資料庫根目錄推導
            MagistockStoragePaths.EnsureFolders();
            LoadLastFolder();
        }

        private void OnBrowseSourceFolder(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "選擇期交所歷史資料資料夾",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
                return;

            _sourceFolder = dialog.SelectedPath;
            SourceFolderTextBox.Text = _sourceFolder;
            SaveLastFolder(_sourceFolder);
        }

        private async void OnStartImport(object sender, RoutedEventArgs e)
        {
            _sourceFolder = SourceFolderTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(_sourceFolder) || !Directory.Exists(_sourceFolder))
            {
                MessageBox.Show("請選擇有效的來源資料夾。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SaveLastFolder(_sourceFolder);

            StartImportButton.IsEnabled = false;
            LogTextBox.Clear();
            LogLine("開始匯入...");
            LogLine($"系統資料庫根目錄：{MagistockStoragePaths.RootFolder}");
            ImportProgressBar.Value = 0;
            ImportProgressBar.Visibility = Visibility.Visible;
            var importSucceeded = false;

            try
            {
                var dbOutputFolder = MagistockStoragePaths.MagistockLibPath;
                await Task.Run(() => ImportFromFolder(_sourceFolder, dbOutputFolder));
                LogLine("完成。");
                importSucceeded = true;
            }
            catch (Exception ex)
            {
                LogLine($"匯入失敗: {ex.Message}");
            }
            finally
            {
                StartImportButton.IsEnabled = true;
                if (importSucceeded)
                {
                    Close();
                }
            }
        }

        private void ImportFromFolder(string sourceFolder, string dbOutputFolder)
        {
            string tempCsvFolder = MagistockStoragePaths.CsvTempFolder;
            var dbPool = new Dictionary<int, YearDatabase>();
            try
            {
                string[] dbFilesInSource = Directory.GetFiles(sourceFolder, "歷史價格資料庫.*.db*", SearchOption.TopDirectoryOnly);
                string[] dbStatusFilesInSource = Directory.GetFiles(sourceFolder, "歷史價格資料庫.*.status.json", SearchOption.TopDirectoryOnly);
                bool hasDbFiles = dbFilesInSource.Length > 0 || dbStatusFilesInSource.Length > 0;

                if (hasDbFiles)
                {
                    Dispatcher.Invoke(() => LogLine("偵測到資料庫檔案，將直接覆蓋至輸出資料夾。"));
                    Directory.CreateDirectory(dbOutputFolder);

                    foreach (string file in dbFilesInSource.Concat(dbStatusFilesInSource))
                    {
                        string dest = Path.Combine(dbOutputFolder, Path.GetFileName(file));
                        File.Copy(file, dest, overwrite: true);
                        Dispatcher.Invoke(() => LogLine($"✓ 覆蓋 {Path.GetFileName(file)}"));
                    }

                    foreach (string file in dbFilesInSource.Concat(dbStatusFilesInSource))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => LogLine($"✗ 刪除失敗 {Path.GetFileName(file)} - {ex.Message}"));
                        }
                    }

                    return;
                }

                Dispatcher.Invoke(() => LogLine($"暫存 CSV 路徑: {tempCsvFolder}"));
                if (Directory.Exists(tempCsvFolder))
                {
                    Directory.Delete(tempCsvFolder, true);
                }

                Directory.CreateDirectory(tempCsvFolder);
                Directory.CreateDirectory(dbOutputFolder);

                string[] csvFilesInSource = Directory.GetFiles(sourceFolder, "*.csv", SearchOption.TopDirectoryOnly);
                foreach (string csvFile in csvFilesInSource)
                {
                    string dest = Path.Combine(tempCsvFolder, Path.GetFileName(csvFile));
                    File.Copy(csvFile, dest, overwrite: true);
                    Dispatcher.Invoke(() => LogLine($"✓ 複製 {Path.GetFileName(csvFile)}"));
                }

                string[] zipFiles = Directory.GetFiles(sourceFolder, "*.zip", SearchOption.TopDirectoryOnly);
                foreach (string zipFile in zipFiles)
                {
                    ZipExtractor.ExtractAll(zipFile, tempCsvFolder, overwriteFiles: true);
                    Dispatcher.Invoke(() => LogLine($"✓ 解壓 {Path.GetFileName(zipFile)}"));
                }

                string[] csvFiles = Directory.GetFiles(tempCsvFolder, "*.csv", SearchOption.AllDirectories);
                Dispatcher.Invoke(() =>
                {
                    ImportProgressBar.Maximum = csvFiles.Length;
                    ImportProgressBar.Value = 0;
                    LogLine($"找到 {csvFiles.Length} 個 CSV 檔，開始匯入...");
                });

                var importer = new TaifexCsvImporter();
                int processedCsv = 0;
                foreach (string csvFile in csvFiles)
                {
                    try
                    {
                        var summary = importer.ImportCsvToDatabase(csvFile, dbOutputFolder, dbPool);
                        Dispatcher.Invoke(() =>
                        {
                            string yearInfo = summary.Years.Count > 0
                                ? string.Join(",", summary.Years.OrderBy(y => y))
                                : "unknown";
                            LogLine($"✓ 匯入 {summary.FileName} -> {yearInfo}");
                            foreach (var stats in summary.Products)
                            {
                                string productName = stats.Product switch
                                {
                                    ProductCode.Tx => "TX",
                                    ProductCode.Mtx => "MTX",
                                    ProductCode.Tmf => "TMF",
                                    _ => stats.Product.ToString()
                                };
                                LogLine($"  {productName}：成交 {stats.TradeCount:N0}，開盤 {stats.OpenCount:N0}，收盤 {stats.CloseCount:N0}，補點 {stats.BoundaryCount:N0}，K棒 {stats.BarCount:N0}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => LogLine($"✗ 匯入失敗 {Path.GetFileName(csvFile)} - {ex.Message}"));
                    }
                    finally
                    {
                        processedCsv++;
                        Dispatcher.Invoke(() => ImportProgressBar.Value = processedCsv);
                    }
                }

                string archivedFolder = MagistockStoragePaths.ImportedFolder;
                Directory.CreateDirectory(archivedFolder);
                foreach (string csvFile in csvFilesInSource)
                {
                    string dest = Path.Combine(archivedFolder, Path.GetFileName(csvFile));
                    File.Move(csvFile, dest, overwrite: true);
                }

                foreach (string zipFile in zipFiles)
                {
                    string dest = Path.Combine(archivedFolder, Path.GetFileName(zipFile));
                    File.Move(zipFile, dest, overwrite: true);
                }
            }
            finally
            {
                foreach (var db in dbPool.Values)
                {
                    db.Dispose();
                }
                try
                {
                    if (Directory.Exists(tempCsvFolder))
                    {
                        Directory.Delete(tempCsvFolder, true);
                    }
                }
                catch
                {
                    // Ignore temp cleanup failures.
                }
            }
        }

        private void LogLine(string message)
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private void LoadLastFolder()
        {
            try
            {
                _sourceFolder = MagistockStoragePaths.DownloadZipFolder;
                SourceFolderTextBox.Text = _sourceFolder;

                if (!File.Exists(_settingsPath))
                    return;

                string json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ImportSettings>(json);
                if (settings == null || string.IsNullOrWhiteSpace(settings.SourceFolder))
                    return;

                var saved = settings.SourceFolder.Trim();
                if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                {
                    _sourceFolder = saved;
                    SourceFolderTextBox.Text = _sourceFolder;
                }
            }
            catch
            {
                // Ignore load failures.
            }
        }

        private void SaveLastFolder(string folder)
        {
            try
            {
                var settings = new ImportSettings
                {
                    SourceFolder = folder
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Ignore save failures.
            }
        }

        private sealed class ImportSettings
        {
            public string SourceFolder { get; set; } = string.Empty;
        }
    }
}
