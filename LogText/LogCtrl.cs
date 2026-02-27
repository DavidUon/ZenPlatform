using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Windows.Threading;
using ZenPlatform.Utility;

namespace ZenPlatform.LogText
{
    public class LogCtrl
    {
        private const int DefaultMaxLines = 5000;
        private const int FlushIntervalMs = 100;
        private const int MaxBatchSize = 200;
        private const int MaxUiLines = 5000;
        private RichTextBox? _target;
        private DateTime _currentTime = DateTime.Now;
        private LogModel? _model;
        private DateTime? _lastEntryTime;
        private string? _lastEntryText;
        private readonly ConcurrentQueue<LogEntry> _pending = new();
        private DispatcherTimer? _flushTimer;
        private bool _suspendRendering;
        private bool _consoleOnly;

        public void SetTarget(RichTextBox target)
        {
            _target = target;
            _target.IsReadOnly = true;
            _target.FontSize = 15;
            _target.Document.Blocks.Clear();
            _lastEntryTime = null;
            _lastEntryText = null;
            _target.Document.PageWidth = 8192;
            _target.Document.PagePadding = new Thickness(8, 0, 0, 8);
            EnsureTimer();
        }

        public bool TryGetTargetScreenRect(out Rect rect)
        {
            rect = default;
            if (_target == null)
            {
                return false;
            }

            if (!_target.Dispatcher.CheckAccess())
            {
                return false;
            }

            var topLeft = _target.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(_target);
            var width = _target.ActualWidth * dpi.DpiScaleX;
            var height = _target.ActualHeight * dpi.DpiScaleY;
            rect = new Rect(topLeft.X, topLeft.Y, width, height);
            return true;
        }

        public void SetModel(LogModel model)
        {
            if (_model != null)
            {
                _model.Changed -= OnModelChanged;
                _model.EntryAdded -= OnEntryAdded;
            }

            _model = model;
            _model.Changed += OnModelChanged;
            _model.EntryAdded += OnEntryAdded;
            Replay();
        }

        public void SetRenderSuspended(bool suspended)
        {
            _suspendRendering = suspended;
            if (!suspended)
            {
                Replay();
            }
        }

        public void SetConsoleOnly(bool enabled)
        {
            _consoleOnly = enabled;
            SetRenderSuspended(enabled);
        }

        public void Clear()
        {
            if (_model == null)
            {
                return;
            }

            _model.Clear();
            _lastEntryTime = null;
        }

        public void SetCurrentTime(DateTime time)
        {
            _currentTime = time;
        }

        public void AddToModel(string message)
        {
            if (_model == null)
            {
                return;
            }

            _model.Add(message);
        }

        public void Log(string message)
        {
            if (_model == null)
            {
                return;
            }

            AddToModel(message);
        }

        private void OnModelChanged()
        {
            if (_target == null || _model == null)
            {
                return;
            }

            var snapshot = _model.GetSnapshot();
            if (snapshot.Length == 0)
            {
                while (_pending.TryDequeue(out _)) { }
                if (_target.Dispatcher.CheckAccess())
                {
                    _target.Document.Blocks.Clear();
                    _lastEntryTime = null;
                    _lastEntryText = null;
                }
                else
                {
                    _target.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _target.Document.Blocks.Clear();
                        _lastEntryTime = null;
                        _lastEntryText = null;
                    }));
                }
                return;
            }
            // If model was trimmed/replaced, rebuild UI.
            if (_target.Dispatcher.CheckAccess())
            {
                ReplaySnapshot(snapshot);
            }
            else
            {
                _target.Dispatcher.BeginInvoke(new Action(() => ReplaySnapshot(snapshot)));
            }
        }

        private void ApplySnapshot(LogEntry[] entries)
        {
            if (entries.Length == 0)
            {
                _target?.Document.Blocks.Clear();
                _lastEntryTime = null;
                _lastEntryText = null;
                return;
            }

            ReplaySnapshot(entries);
        }


        private void Replay()
        {
            if (_target == null || _model == null)
            {
                return;
            }

            if (_target.Dispatcher.CheckAccess())
            {
                ReplaySnapshot(_model.GetSnapshot());
                return;
            }

            _target.Dispatcher.BeginInvoke(new Action(Replay));
        }

        private void ReplaySnapshot(LogEntry[] entries)
        {
            if (_target == null)
            {
                return;
            }

            _target.Document.Blocks.Clear();
            _lastEntryTime = null;
            _lastEntryText = null;
            while (_pending.TryDequeue(out _)) { }
            _target.BeginChange();
            foreach (var entry in entries)
            {
                if (_lastEntryTime.HasValue &&
                    (entry.Time - _lastEntryTime.Value) > TimeSpan.FromMinutes(1))
                {
                    _target.Document.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
                }

                var paragraph = new Paragraph { Margin = new Thickness(0) };
                AppendTextRuns(paragraph, entry);
                paragraph.Inlines.Add(new Run("   "));
                paragraph.Inlines.Add(new Run($"{entry.Time:MM/dd HH:mm:ss}")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                    FontSize = 12
                });
                _target.Document.Blocks.Add(paragraph);
                _lastEntryTime = entry.Time;
                _lastEntryText = entry.Text;
            }
            _target.EndChange();
            _target.ScrollToEnd();
        }

        private void EnsureTimer()
        {
            if (_flushTimer != null)
            {
                return;
            }

            _flushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(FlushIntervalMs)
            };
            _flushTimer.Tick += (_, __) => FlushPending();
            _flushTimer.Start();
        }

        private void FlushPending()
        {
            if (_target == null)
            {
                return;
            }

            if (_suspendRendering)
            {
                return;
            }

            if (_pending.IsEmpty)
            {
                return;
            }

            var count = 0;
            _target.BeginChange();
            while (count < MaxBatchSize && _pending.TryDequeue(out var entry))
            {
                AppendLine(entry);
                count++;
            }
            _target.EndChange();

            TrimUiLines();
        }

        private void OnEntryAdded(LogEntry entry)
        {
            if (_consoleOnly && ConsoleLog.Enabled)
            {
                ConsoleLog.WriteLine($"{entry.Text}   {entry.Time:MM/dd HH:mm:ss}");
                return;
            }

            _pending.Enqueue(entry);
            EnsureTimer();
        }

        private void TrimUiLines()
        {
            if (_target == null)
            {
                return;
            }

            while (_target.Document.Blocks.Count > MaxUiLines)
            {
                _target.Document.Blocks.Remove(_target.Document.Blocks.FirstBlock);
            }
        }

        private void AppendLine(LogEntry entry)
        {
            if (_target == null)
            {
                return;
            }

            if (_lastEntryTime.HasValue &&
                _lastEntryTime.Value == entry.Time &&
                string.Equals(_lastEntryText, entry.Text, StringComparison.Ordinal))
            {
                return;
            }

            var shouldAutoScroll = IsNearBottom();
            if (_lastEntryTime.HasValue &&
                (entry.Time - _lastEntryTime.Value) > TimeSpan.FromMinutes(1))
            {
                _target.Document.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
            }

            var paragraph = new Paragraph { Margin = new Thickness(0) };
            AppendTextRuns(paragraph, entry);
            paragraph.Inlines.Add(new Run("   "));
            paragraph.Inlines.Add(new Run($"{entry.Time:MM/dd HH:mm:ss}")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                FontSize = 12
            });
            _target.Document.Blocks.Add(paragraph);
            _lastEntryTime = entry.Time;
            _lastEntryText = entry.Text;
            TrimLines();
            if (shouldAutoScroll)
            {
                _target.ScrollToEnd();
            }
        }

        private void TrimLines()
        {
            if (_target == null)
            {
                return;
            }

            var maxLines = _model?.MaxLines ?? DefaultMaxLines;
            if (maxLines <= 0)
            {
                return;
            }

            var threshold = maxLines == 500 ? 750 : maxLines;
            if (_target.Document.Blocks.Count <= threshold)
            {
                return;
            }

            while (_target.Document.Blocks.Count > maxLines)
            {
                _target.Document.Blocks.Remove(_target.Document.Blocks.FirstBlock);
            }
        }

        private void AppendLineFast(Paragraph paragraph, LogEntry entry)
        {
            if (_lastEntryTime.HasValue &&
                (entry.Time - _lastEntryTime.Value) > TimeSpan.FromMinutes(1))
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            AppendTextRuns(paragraph, entry);
            paragraph.Inlines.Add(new Run("   "));
            paragraph.Inlines.Add(new Run($"{entry.Time:MM/dd HH:mm:ss}")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                FontSize = 12
            });
            paragraph.Inlines.Add(new LineBreak());
            _lastEntryTime = entry.Time;
            _lastEntryText = entry.Text;
        }

        private static void AppendTextRuns(Paragraph paragraph, LogEntry entry)
        {
            if (string.IsNullOrEmpty(entry.HighlightText) || entry.HighlightColor == LogTxtColor.預設)
            {
                paragraph.Inlines.Add(new Run(entry.Text));
                return;
            }

            var text = entry.Text ?? string.Empty;
            var highlight = entry.HighlightText ?? string.Empty;
            if (highlight.Length == 0)
            {
                paragraph.Inlines.Add(new Run(text));
                return;
            }

            var brush = GetHighlightBrush(entry.HighlightColor);
            var index = 0;
            while (true)
            {
                var hit = text.IndexOf(highlight, index, StringComparison.Ordinal);
                if (hit < 0)
                {
                    if (index < text.Length)
                    {
                        paragraph.Inlines.Add(new Run(text.Substring(index)));
                    }
                    break;
                }

                if (hit > index)
                {
                    paragraph.Inlines.Add(new Run(text.Substring(index, hit - index)));
                }

                var run = new Run(text.Substring(hit, highlight.Length))
                {
                    Foreground = brush
                };
                paragraph.Inlines.Add(run);
                index = hit + highlight.Length;
            }
        }

        private static Brush GetHighlightBrush(LogTxtColor color)
        {
            return color switch
            {
                LogTxtColor.黃色 => new SolidColorBrush(Color.FromRgb(245, 200, 66)),
                LogTxtColor.藍色 => new SolidColorBrush(Color.FromRgb(80, 160, 255)),
                LogTxtColor.紅色 => new SolidColorBrush(Color.FromRgb(220, 60, 60)),
                LogTxtColor.綠色 => new SolidColorBrush(Color.FromRgb(80, 220, 120)),
                _ => Brushes.Black
            };
        }

        private bool IsNearBottom()
        {
            if (_target == null)
            {
                return true;
            }

            var scrollableHeight = _target.ExtentHeight - _target.ViewportHeight;
            if (scrollableHeight <= 0)
            {
                return true;
            }

            var verticalOffset = _target.VerticalOffset;
            return verticalOffset >= scrollableHeight - 1;
        }
    }
}
