using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Color = System.Windows.Media.Color;

namespace TaifexHisDbManager
{
    internal partial class ImportHelpWindow : Window
    {
        public ImportHelpWindow(string helpText)
        {
            InitializeComponent();
            BuildHelpText(helpText);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BuildHelpText(string helpText)
        {
            HelpTextBlock.Inlines.Clear();

            const string linkText = "台灣期貨交易所成交簡檔";
            var linkUri = new Uri("https://www.taifex.com.tw/cht/3/dlFutPrevious30DaysSalesData");

            int index = helpText.IndexOf(linkText, StringComparison.Ordinal);
            if (index < 0)
            {
                AppendWithLineBreaks(HelpTextBlock.Inlines, helpText);
                return;
            }

            string before = helpText.Substring(0, index);
            string after = helpText.Substring(index + linkText.Length);

            AppendWithLineBreaks(HelpTextBlock.Inlines, before);

            var hyperlink = new Hyperlink(new Run(linkText))
            {
                NavigateUri = linkUri,
                Foreground = new SolidColorBrush(Color.FromRgb(78, 163, 255)),
                TextDecorations = TextDecorations.Underline
            };
            hyperlink.RequestNavigate += OnRequestNavigate;
            HelpTextBlock.Inlines.Add(hyperlink);

            AppendWithLineBreaks(HelpTextBlock.Inlines, after);
        }

        private static void AppendWithLineBreaks(InlineCollection inlines, string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    inlines.Add(new LineBreak());
                if (lines[i].Length > 0)
                    inlines.Add(new Run(lines[i]));
            }
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
