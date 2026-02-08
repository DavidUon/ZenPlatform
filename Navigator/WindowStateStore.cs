using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Navigator
{
    public sealed class WindowStateStore
    {
        private const string FileName = "window-state.json";

        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public WindowState WindowState { get; set; }
        public int? SelectedYear { get; set; }
        public int? SelectedMonth { get; set; }
        public int? SelectedPeriod { get; set; }

        public static void Apply(Window window)
        {
            if (window == null)
            {
                return;
            }

            var data = Load();
            if (data == null)
            {
                return;
            }

            if (data.Width > 0 && data.Height > 0)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Width = data.Width;
                window.Height = data.Height;
            }

            if (!double.IsNaN(data.Left) && !double.IsNaN(data.Top))
            {
                window.Left = data.Left;
                window.Top = data.Top;
            }

            if (data.WindowState != WindowState.Minimized)
            {
                window.WindowState = data.WindowState;
            }
        }

        public static void Save(Window window)
        {
            if (window == null)
            {
                return;
            }

            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.Width, window.Height)
                : window.RestoreBounds;

            var data = new WindowStateStore
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Left = bounds.Left,
                Top = bounds.Top,
                WindowState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState
            };

            if (window is MainWindow main)
            {
                data.SelectedYear = main.GetSelectedYear();
                data.SelectedMonth = main.GetSelectedMonth();
                data.SelectedPeriod = main.GetSelectedPeriod();
            }

            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static WindowStateStore? Load()
        {
            var path = GetPath();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<WindowStateStore>(json);
            }
            catch
            {
                return null;
            }
        }

        private static string GetPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Navigator");
            return Path.Combine(dir, FileName);
        }
    }
}
