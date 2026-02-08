using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Collections.Concurrent;
using System.Threading;

namespace ZenPlatform.Utility
{
    public static class ConsoleLog
    {
        private const int SwpNoZOrder = 0x0004;
        private const int SwpNoActivate = 0x0010;
        private const int SwShow = 5;
        private const int StdInputHandle = -10;
        private const int EnableQuickEdit = 0x0040;
        private const int EnableExtendedFlags = 0x0080;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int mode);

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
                    var stdout = Console.OpenStandardOutput();
                    var writer = new System.IO.StreamWriter(stdout) { AutoFlush = true };
                    Console.SetOut(writer);
                    Console.SetError(writer);
                    _streamsBound = true;
                }

                DisableQuickEdit();
                EnsureWorker();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    Console.Title = title;
                }

                var hwnd = GetConsoleWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                ShowWindow(hwnd, SwShow);
                var width = Math.Max(200, (int)Math.Round(screenRect.Width * 2));
                var x = (int)Math.Round(screenRect.X + screenRect.Width - width);
                var y = (int)Math.Round(screenRect.Y);
                var height = Math.Max(120, (int)Math.Round(screenRect.Height));
                SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
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

        private static void DisableQuickEdit()
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
            newMode &= ~EnableQuickEdit;
            newMode |= EnableExtendedFlags;
            SetConsoleMode(handle, newMode);
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
