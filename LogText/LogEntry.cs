using System;

namespace ZenPlatform.LogText
{
    public sealed class LogEntry
    {
        public LogEntry(DateTime time, string text, string? highlightText = null, LogTxtColor highlightColor = LogTxtColor.預設)
        {
            Time = time;
            Text = text;
            HighlightText = highlightText;
            HighlightColor = highlightColor;
        }

        public DateTime Time { get; }
        public string Text { get; }
        public string? HighlightText { get; }
        public LogTxtColor HighlightColor { get; }
    }
}
