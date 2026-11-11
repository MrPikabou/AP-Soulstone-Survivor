using System.Collections.Generic;
using BepInEx.Logging;

namespace SoulstoneAP
{
    /// <summary>
    /// Mirrors BepInEx output into memory so the F8 panel can show the same lines
    /// the console does. Bounded, and fed from whatever thread logged the line, so
    /// every read and write takes the lock.
    /// </summary>
    internal sealed class ConsoleLog : ILogListener
    {
        private const int MaxLines = 500;
        private static readonly List<string> Lines = new List<string>();

        public LogLevel LogLevelFilter { get; set; } = LogLevel.All;

        /// <summary>Bumped whenever a line is added, so the panel knows to repaint.</summary>
        public static int Version { get; private set; }

        public static string Text
        {
            get
            {
                lock (Lines)
                {
                    return Lines.Count == 0 ? "" : string.Join("\n", Lines);
                }
            }
        }

        public static void Install()
        {
            BepInEx.Logging.Logger.Listeners.Add(new ConsoleLog());
        }

        // ILogListener is IDisposable; nothing to release, the list is static.
        public void Dispose() { }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            var data = eventArgs?.Data?.ToString();
            if (string.IsNullOrEmpty(data)) return;

            var source = eventArgs.Source?.SourceName ?? "BepInEx";
            lock (Lines)
            {
                foreach (var line in data.Split('\n'))
                {
                    Lines.Add("[" + source + "] " + line.TrimEnd('\r'));
                    while (Lines.Count > MaxLines) Lines.RemoveAt(0);
                }
                Version++;
            }
        }
    }
}
