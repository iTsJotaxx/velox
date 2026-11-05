using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;

namespace Velox.UI.Views
{
    public sealed class LogLine
    {
        public string TimeText { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    public partial class LogView : UserControl, IPage
    {
        static readonly Regex LineRx = new Regex(@"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d) \[(\w+)\] (.*)$", RegexOptions.Compiled);
        readonly ObservableCollection<LogLine> _lines = new ObservableCollection<LogLine>();

        public LogView()
        {
            InitializeComponent();
            Lines.ItemsSource = _lines;
            foreach (var l in Log.ReadTail(300)) Add(l);
            Log.Written += e => Dispatcher.BeginInvoke(new Action(() => { Add(e.Line); Scroll.ScrollToEnd(); }));
        }

        void Add(string line)
        {
            var m = LineRx.Match(line);
            _lines.Add(m.Success
                ? new LogLine { TimeText = m.Groups[1].Value, Level = m.Groups[2].Value, Message = m.Groups[3].Value }
                : new LogLine { TimeText = "", Level = "INFO", Message = line });
            while (_lines.Count > 600) _lines.RemoveAt(0);
        }

        public void OnShown() => Scroll.ScrollToEnd();
        public void RefreshTexts() { }

        void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(AppPaths.LogFile)) Process.Start("notepad.exe", "\"" + AppPaths.LogFile + "\"");
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
    }
}
