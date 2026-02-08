using System;
using System.Collections.Generic;
namespace ZenPlatform.LogText
{
    public class LogModel
    {
        private readonly object _sync = new();
        private readonly List<LogEntry> _entries = new();
        private DateTime _currentTime = DateTime.Now;
        private bool _hasManualTime;

        public int MaxLines { get; set; } = 5000;

        public IReadOnlyList<LogEntry> Entries => _entries;

        public event Action? Changed;
        public event Action<LogEntry>? EntryAdded;

        public void Add(string text)
        {
            var resolvedTime = _hasManualTime ? _currentTime : DateTime.Now;
            var entry = new LogEntry(resolvedTime, text ?? string.Empty);
            lock (_sync)
            {
                _entries.Add(entry);
                TrimIfNeededLocked();
            }
            EntryAdded?.Invoke(entry);
        }

        public void AddAt(DateTime time, string text)
        {
            var entry = new LogEntry(time, text ?? string.Empty);
            lock (_sync)
            {
                _entries.Add(entry);
                TrimIfNeededLocked();
            }
            EntryAdded?.Invoke(entry);
        }

        public void Add(string text, string highlightText, LogTxtColor highlightColor)
        {
            var resolvedTime = _hasManualTime ? _currentTime : DateTime.Now;
            var entry = new LogEntry(resolvedTime, text ?? string.Empty, highlightText, highlightColor);
            lock (_sync)
            {
                _entries.Add(entry);
                TrimIfNeededLocked();
            }
            EntryAdded?.Invoke(entry);
        }

        public void AddAt(DateTime time, string text, string highlightText, LogTxtColor highlightColor)
        {
            var entry = new LogEntry(time, text ?? string.Empty, highlightText, highlightColor);
            lock (_sync)
            {
                _entries.Add(entry);
                TrimIfNeededLocked();
            }
            EntryAdded?.Invoke(entry);
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
            lock (_sync)
            {
                if (_entries.Count == 0)
                {
                    return;
                }
                _entries.Clear();
            }
            Changed?.Invoke();
        }

        public void SetMaxLines(int maxLines)
        {
            MaxLines = maxLines;
            lock (_sync)
            {
                TrimIfNeededLocked();
            }
            Changed?.Invoke();
        }

        public void LoadEntries(IEnumerable<LogEntry> entries)
        {
            lock (_sync)
            {
                _entries.Clear();
                foreach (var entry in entries)
                {
                    _entries.Add(entry);
                }
                TrimIfNeededLocked();
            }
            Changed?.Invoke();
        }

        public LogEntry[] GetSnapshot()
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }

        private void TrimIfNeededLocked()
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
