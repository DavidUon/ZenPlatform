using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Utility
{
    public static class Db
    {
        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
        
        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
        
        [DllImport("user32.dll")]
        private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        
        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        
        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFO_EX lpConsoleCurrentFontEx);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CONSOLE_FONT_INFO_EX
        {
            public uint cbSize;
            public uint nFont;
            public COORD dwFontSize;
            public uint FontFamily;
            public uint FontWeight;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FaceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        private const int STD_OUTPUT_HANDLE = -11;
        
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        private const uint SC_CLOSE = 0xF060;
        private const uint MF_BYCOMMAND = 0x00000000;
        private const uint MF_GRAYED = 0x00000001;
        private const int STD_INPUT_HANDLE = -10;
        private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        private const uint ENABLE_MOUSE_INPUT = 0x0010;
        
        private static bool _consoleAllocated = false;
        private static bool _isMonitoring = false;
        private static bool _shouldBeVisible = false; // 追蹤視窗應該顯示還是隱藏
        private static CancellationTokenSource? _cancellationTokenSource;
        private static List<string> _bufferedMessages = new List<string>();
        private const int MAX_BUFFER_SIZE = 10000; // 最大緩衝 1 萬行
        private static bool _quickEditEnabled = true; // 預設開放快速編輯模式

        // 高頻輸出控制
        private static readonly object _msgLock = new object();
        private static Queue<string> _msgQueue = new Queue<string>();
        private static Task? _outputTask;
        private static CancellationTokenSource? _outputCancelToken;
        private static DateTime _lastOutputTime = DateTime.MinValue;
        private const int OUTPUT_THROTTLE_MS = 50; // 最小輸出間隔 50ms
        
        /// <summary>
        /// 初始化 Debug Console（延遲初始化）
        /// </summary>
        private static void Initialize()
        {
            if (!_consoleAllocated)
            {
                // 分配 Console 並立即隱藏
                AllocConsole();
                IntPtr consoleWindow = GetConsoleWindow();
                ShowWindow(consoleWindow, SW_HIDE); // 立即隱藏
                
                // 禁用 Console 視窗的關閉按鈕
                IntPtr systemMenu = GetSystemMenu(consoleWindow, false);
                EnableMenuItem(systemMenu, SC_CLOSE, MF_BYCOMMAND | MF_GRAYED);
                
                // 根據設定決定是否禁用快速編輯模式
                ApplyQuickEditMode();

                // 設定較大的字型和行距
                SetConsoleFontSize();

                _consoleAllocated = true;
                
                // 開始監控視窗狀態
                StartMonitoring();

                // 啟動非阻塞輸出任務
                StartOutputTask();
                
                Console.WriteLine("=== Debug Console 已準備就緒 ===");
                Console.WriteLine($"初始化時間: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
                Console.WriteLine("使用 dbmsg() 方法輸出 debug 訊息");
                Console.WriteLine("最小化此視窗會自動隱藏");
                
                // 輸出之前緩衝的訊息
                FlushBufferedMessages();
            }
        }
        
        /// <summary>
        /// 切換快速編輯模式
        /// </summary>
        /// <param name="enabled">true=開放快速編輯, false=禁用快速編輯</param>
        public static void SetQuickEditMode(bool enabled)
        {
            _quickEditEnabled = enabled;
            if (_consoleAllocated)
            {
                ApplyQuickEditMode();
            }
        }

        /// <summary>
        /// 設定 Console 字型大小和行距
        /// </summary>
        private static void SetConsoleFontSize()
        {
            try
            {
                IntPtr hConsoleOutput = GetStdHandle(STD_OUTPUT_HANDLE);
                if (hConsoleOutput != IntPtr.Zero)
                {
                    var fontInfo = new CONSOLE_FONT_INFO_EX
                    {
                        cbSize = (uint)Marshal.SizeOf<CONSOLE_FONT_INFO_EX>(),
                        nFont = 0,
                        dwFontSize = new COORD { X = 0, Y = 16 }, // 字型大小 20
                        FontFamily = 54, // FF_MODERN
                        FontWeight = 400, // FW_NORMAL
                        FaceName = "Cascadia Mono" // 使用 Cascadia Mono 字型
                    };

                    SetCurrentConsoleFontEx(hConsoleOutput, false, ref fontInfo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定 Console 字型失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 套用 Console 快速編輯模式設定
        /// </summary>
        private static void ApplyQuickEditMode()
        {
            try
            {
                IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);
                if (consoleHandle != IntPtr.Zero)
                {
                    if (GetConsoleMode(consoleHandle, out uint consoleMode))
                    {
                        if (_quickEditEnabled)
                        {
                            // 啟用快速編輯模式
                            consoleMode |= ENABLE_QUICK_EDIT_MODE;
                            consoleMode |= ENABLE_EXTENDED_FLAGS;
                            consoleMode |= ENABLE_MOUSE_INPUT;
                            Console.WriteLine("快速編輯模式已啟用，支援文字選取和滾輪滾動");
                        }
                        else
                        {
                            // 禁用快速編輯模式，但保留滑鼠輸入功能
                            consoleMode &= ~ENABLE_QUICK_EDIT_MODE;
                            consoleMode &= ~ENABLE_EXTENDED_FLAGS;
                            consoleMode |= ENABLE_MOUSE_INPUT;
                            Console.WriteLine("快速編輯模式已禁用，點擊不會暫停執行緒");
                        }

                        SetConsoleMode(consoleHandle, consoleMode);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定快速編輯模式失敗: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 輸出緩衝的訊息並清空緩衝區
        /// </summary>
        private static void FlushBufferedMessages()
        {
            if (_bufferedMessages.Count > 0)
            {
                Console.WriteLine($"=== 輸出緩衝的 {_bufferedMessages.Count} 條訊息 ===");
                foreach (var message in _bufferedMessages)
                {
                    Console.WriteLine(message);
                }
                _bufferedMessages.Clear();
                Console.WriteLine("=== 緩衝訊息輸出完成 ===");
            }
        }
        
        /// <summary>
        /// 開始監控 Console 視窗狀態
        /// </summary>
        private static void StartMonitoring()
        {
            if (_isMonitoring) return;
            
            _isMonitoring = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            Task.Run(async () =>
            {
                while (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        IntPtr consoleWindow = GetConsoleWindow();
                        if (consoleWindow != IntPtr.Zero && _shouldBeVisible)
                        {
                            if (IsIconic(consoleWindow))
                            {
                                // 視窗被最小化，立即隱藏避免看到動畫
                                ShowWindow(consoleWindow, SW_HIDE);
                                _shouldBeVisible = false; // 標記為隱藏狀態
                                Console.WriteLine("Console 視窗被最小化，已自動隱藏");
                            }
                        }
                        
                        await Task.Delay(50, _cancellationTokenSource?.Token ?? CancellationToken.None); // 每50ms檢查一次，更快響應
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, _cancellationTokenSource?.Token ?? CancellationToken.None);
        }
        
        /// <summary>
        /// 顯示 Debug Console 視窗
        /// </summary>
        public static void Show()
        {
            // 第一次顯示時才初始化 Console
            if (!_consoleAllocated)
            {
                Initialize();
            }
            
            if (_consoleAllocated)
            {
                _shouldBeVisible = true; // 先設定狀態
                IntPtr consoleWindow = GetConsoleWindow();
                
                // 如果視窗是最小化狀態，先恢復再顯示
                if (IsIconic(consoleWindow))
                {
                    ShowWindow(consoleWindow, SW_RESTORE); // 恢復視窗
                }
                else
                {
                    ShowWindow(consoleWindow, SW_SHOW); // 正常顯示
                }
                
                Console.WriteLine($"=== Debug Console 顯示於 {DateTime.Now:HH:mm:ss} ===");
            }
        }
        
        /// <summary>
        /// 隱藏 Debug Console 視窗
        /// </summary>
        public static void Hide()
        {
            if (_consoleAllocated)
            {
                IntPtr consoleWindow = GetConsoleWindow();
                ShowWindow(consoleWindow, SW_HIDE);
                _shouldBeVisible = false; // 標記為應該隱藏
            }
        }
        
        /// <summary>
        /// 啟動非阻塞輸出任務
        /// </summary>
        private static void StartOutputTask()
        {
            if (_outputTask != null) return;

            _outputCancelToken = new CancellationTokenSource();
            _outputTask = Task.Run(async () =>
            {
                while (!_outputCancelToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        List<string> batch = new List<string>();

                        lock (_msgLock)
                        {
                            // 批次處理訊息，避免高頻輸出
                            int batchSize = Math.Min(20, _msgQueue.Count);
                            for (int i = 0; i < batchSize; i++)
                            {
                                if (_msgQueue.Count > 0)
                                    batch.Add(_msgQueue.Dequeue());
                            }
                        }

                        // 輸出批次訊息
                        if (batch.Count > 0)
                        {
                            foreach (var msg in batch)
                            {
                                Console.WriteLine(msg);
                            }
                            _lastOutputTime = DateTime.Now;
                        }

                        // 控制輸出頻率，避免 Console 阻塞
                        await Task.Delay(OUTPUT_THROTTLE_MS, _outputCancelToken.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Console 輸出錯誤: {ex.Message}");
                    }
                }
            }, _outputCancelToken.Token);
        }

        /// <summary>
        /// 輸出 debug 訊息（非阻塞）
        /// </summary>
        /// <param name="message">訊息內容</param>
        /// <param name="IsHideMsg">是否隱藏訊息，預設 false</param>
        public static void Msg(string message, bool IsHideMsg = false)
        {
            if (IsHideMsg) return;

            string formattedMsg = $"[Debug {DateTime.Now:HH:mm:ss}] - {message}";

            if (_consoleAllocated)
            {
                // 使用非阻塞的佇列輸出
                lock (_msgLock)
                {
                    _msgQueue.Enqueue(formattedMsg);

                    // 控制佇列大小，避免記憶體耗盡
                    if (_msgQueue.Count > MAX_BUFFER_SIZE)
                    {
                        _msgQueue.Dequeue(); // 移除最舊的訊息
                    }
                }
            }
            else
            {
                // 緩衝訊息，控制最大大小
                _bufferedMessages.Add(formattedMsg);

                // 超過最大大小時，移除最舊的訊息
                if (_bufferedMessages.Count > MAX_BUFFER_SIZE)
                {
                    _bufferedMessages.RemoveAt(0);
                }
            }
        }
        
        /// <summary>
        /// 輸出格式化 debug 訊息
        /// </summary>
        /// <param name="format">格式字串</param>
        /// <param name="args">參數</param>
        public static void Msg(string format, params object[] args)
        {
            Msg(string.Format(format, args), false);
        }
        
        /// <summary>
        /// 釋放 Debug Console
        /// </summary>
        public static void Dispose()
        {
            // 停止輸出任務
            if (_outputTask != null)
            {
                _outputCancelToken?.Cancel();
                try
                {
                    _outputTask.Wait(1000); // 等待最多1秒
                }
                catch (AggregateException) { }
                _outputCancelToken?.Dispose();
                _outputTask = null;
            }

            if (_isMonitoring)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _isMonitoring = false;
            }

            if (_consoleAllocated)
            {
                FreeConsole();
                _consoleAllocated = false;
            }
        }
    }
}