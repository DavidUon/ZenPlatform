using System;
using System.Collections.Generic;

namespace ZenPlatform.LogText
{
    public class LogModel
    {
        private readonly List<LogEntry> _entries = new();
        private DateTime _currentTime = DateTime.Now;
        private bool _hasManualTime;

        public int MaxLines { get; set; } = 5000;

        public IReadOnlyList<LogEntry> Entries => _entries;

        public event Action? Changed;

        public void Add(string text)
        {
            var resolvedTime = _hasManualTime ? _currentTime : DateTime.Now;
            var entry = new LogEntry(resolvedTime, text ?? string.Empty);
            _entries.Add(entry);
            TrimIfNeeded();
            Changed?.Invoke();
        }

        public void AddAt(DateTime time, string text)
        {
            var entry = new LogEntry(time, text ?? string.Empty);
            _entries.Add(entry);
            TrimIfNeeded();
            Changed?.Invoke();
        }

        public void Add(string text, string highlightText, LogTxtColor highlightColor)
        {
            var resolvedTime = _hasManualTime ? _currentTime : DateTime.Now;
            var entry = new LogEntry(resolvedTime, text ?? string.Empty, highlightText, highlightColor);
            _entries.Add(entry);
            TrimIfNeeded();
            Changed?.Invoke();
        }

        public void AddAt(DateTime time, string text, string highlightText, LogTxtColor highlightColor)
        {
            var entry = new LogEntry(time, text ?? string.Empty, highlightText, highlightColor);
            _entries.Add(entry);
            TrimIfNeeded();
            Changed?.Invoke();
        }

        public void SetCurrentTime(DateTime time)
        {
            _currentTime = time;
            _hasManualTime = true;
        }

        public void UseSystemTime()
        {
            _hasManualTime = false;
        }

        public void Clear()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            _entries.Clear();
            Changed?.Invoke();
        }

        public void SetMaxLines(int maxLines)
        {
            MaxLines = maxLines;
            TrimIfNeeded();
            Changed?.Invoke();
        }

        public void LoadEntries(IEnumerable<LogEntry> entries)
        {
            _entries.Clear();
            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }
            TrimIfNeeded();
            Changed?.Invoke();
        }

        private void TrimIfNeeded()
        {
            if (MaxLines <= 0)
            {
                return;
            }

            while (_entries.Count > MaxLines)
            {
                _entries.RemoveAt(0);
            }
        }
    }
}
