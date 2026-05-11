using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Timer
{
    internal sealed class HistoryService
    {
        private static readonly Regex HistoryLineRegex = new(@"^(?<date>\d{2}\.\d{2}\.\d{4}\s+\d{1,2}:\d{2})\s+-\s+(?<duration>\d{2}:\d{2}:\d{2})$");

        private readonly string _historyPath = Path.Combine(AppContext.BaseDirectory, "History");

        public void Append(TimeSpan duration)
        {
            // Keep the existing History line format for old files and current UI parsing.
            string line = $"{DateTime.Now:dd.MM.yyyy H:mm} - {FormatTime(duration)}{Environment.NewLine}";
            File.AppendAllText(_historyPath, line);
        }

        public IEnumerable<HistoryEntry> ReadEntries()
        {
            if (!File.Exists(_historyPath)) yield break;

            foreach (string line in File.ReadLines(_historyPath))
            {
                var match = HistoryLineRegex.Match(line.Trim());
                if (!match.Success) continue;

                if (!DateTime.TryParseExact(match.Groups["date"].Value, "dd.MM.yyyy H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                {
                    continue;
                }

                if (!TimeSpan.TryParseExact(match.Groups["duration"].Value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var duration))
                {
                    continue;
                }

                yield return new HistoryEntry(start, duration);
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
    }

    internal sealed record HistoryEntry(DateTime Start, TimeSpan Duration);
}
