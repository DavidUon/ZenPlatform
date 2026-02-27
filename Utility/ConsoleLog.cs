using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Collections.Concurrent;
using System.Threading;
using System.Text;

namespace ZenPlatform.Utility
{
    public static class ConsoleLog
    {
        private const int SwpNoActivate = 0x0010;
        private const int SwpFrameChanged = 0x0020;
        private const int SwShow = 5;
        private static readonly IntPtr HwndTopMost = new(-1);
        private const int StdInputHandle = -10;
        private const int GwlStyle = -16;
        private const int WsCaption = 0x00C00000;
        private const int WsThickFrame = 0x00040000;
        private const int WsSysMenu = 0x00080000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;
        private const int EnableQuickEditFlag = 0x0040;
        private const int EnableExtendedFlags = 0x0080;
        private const int EnableInsertMode = 0x0020;
        private const uint Utf8CodePage = 65001;

        private static bool _enabled;
        private static bool _streamsBound;
        private static readonly ConcurrentQueue<string> Queue = new();
        private static readonly AutoResetEvent Signal = new(false);
        private static Thread? _worker;
        private static int _workerRunning;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleOutputCP(uint wCodePageID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCP(uint wCodePageID);

        public static bool Enabled => _enabled;

        public static bool OpenAt(Rect screenRect, string? title = null)
        {
            try
            {
                if (!_enabled)
                {
                    if (!AllocConsole())
                    {
                        return false;
                    }
                    _enabled = true;
                }

                if (!_streamsBound)
                {
                    ConfigureUtf8Console();
                    var stdout = Console.OpenStandardOutput();
                    var writer = new System.IO.StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };
                    Console.SetOut(writer);
                    Console.SetError(writer);
                    _streamsBound = true;
                }

                EnableQuickEdit();
                EnsureWorker();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    Console.Title = title;
                }

                var hwnd = GetConsoleWindow();
                if (hwnd == IntPtr.Zero)
                {
                    Thread.Sleep(50);
                    hwnd = GetConsoleWindow();
                    if (hwnd == IntPtr.Zero)
                    {
                        return false;
                    }
                }

                ShowWindow(hwnd, SwShow);
                var style = GetWindowLong(hwnd, GwlStyle);
                if (style != 0)
                {
                    style &= ~WsCaption;
                    style &= ~WsThickFrame;
                    style &= ~WsSysMenu;
                    style &= ~WsMinimizeBox;
                    style &= ~WsMaximizeBox;
                    SetWindowLong(hwnd, GwlStyle, style);
                }
                var width = Math.Max(200, (int)Math.Round(screenRect.Width));
                var x = (int)Math.Round(screenRect.X);
                var y = (int)Math.Round(screenRect.Y);
                var height = Math.Max(120, (int)Math.Round(screenRect.Height));
                SetWindowPos(hwnd, HwndTopMost, x, y, width, height, SwpNoActivate | SwpFrameChanged);
                return true;
            }
            catch
            {
                _enabled = false;
                _streamsBound = false;
                return false;
            }
        }

        public static void Close()
        {
            if (!_enabled)
            {
                return;
            }

            _enabled = false;
            Signal.Set();
            if (_worker != null && _worker.IsAlive)
            {
                _worker.Join(500);
            }

            FreeConsole();
            _streamsBound = false;
        }

        public static void UpdateBounds(Rect screenRect)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                var hwnd = GetConsoleWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var width = Math.Max(200, (int)Math.Round(screenRect.Width));
                var x = (int)Math.Round(screenRect.X);
                var y = (int)Math.Round(screenRect.Y);
                var height = Math.Max(120, (int)Math.Round(screenRect.Height));
                SetWindowPos(hwnd, HwndTopMost, x, y, width, height, SwpNoActivate);
            }
            catch
            {
                // ignore reposition failure
            }
        }

        private static void EnableQuickEdit()
        {
            var handle = GetStdHandle(StdInputHandle);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (!GetConsoleMode(handle, out var mode))
            {
                return;
            }

            var newMode = mode;
            newMode |= EnableExtendedFlags;
            newMode |= EnableQuickEditFlag;
            newMode |= EnableInsertMode;
            SetConsoleMode(handle, newMode);
        }

        private static void ConfigureUtf8Console()
        {
            try
            {
                SetConsoleOutputCP(Utf8CodePage);
                SetConsoleCP(Utf8CodePage);
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Best-effort: keep console usable even if encoding setup fails.
            }
        }

        public static void WriteLine(string message)
        {
            if (!_enabled)
            {
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Queue.Enqueue(message);
            Signal.Set();
        }

        private static void EnsureWorker()
        {
            if (Interlocked.Exchange(ref _workerRunning, 1) == 1)
            {
                if (_worker == null || !_worker.IsAlive)
                {
                    Interlocked.Exchange(ref _workerRunning, 0);
                }
                else
                {
                    return;
                }
            }

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "ConsoleLogWorker"
            };
            _worker.Start();
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                Signal.WaitOne(250);
                if (!_enabled && Queue.IsEmpty)
                {
                    break;
                }

                while (Queue.TryDequeue(out var line))
                {
                    try
                    {
                        Console.WriteLine(line);
                    }
                    catch
                    {
                        _enabled = false;
                        break;
                    }
                }
            }

            Interlocked.Exchange(ref _workerRunning, 0);
        }
    }
}
