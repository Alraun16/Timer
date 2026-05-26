using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Timer
{
    internal sealed class HistoryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _historyPath = Path.Combine(AppContext.BaseDirectory, "History");

        public void Append(TimeSpan duration, string description)
        {
            var record = new HistoryRecord
            {
                FinishedAt = DateTime.Now,
                DurationTicks = duration.Ticks,
                Description = description
            };

            string line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
            File.AppendAllText(_historyPath, line, Encoding.UTF8);
        }

        public IEnumerable<HistoryEntry> ReadEntries()
        {
            int index = 0;

            foreach (var entry in ReadEntryList())
            {
                yield return entry with { Index = index };
                index++;
            }
        }

        public void UpdateEntry(int index, DateTime finishedAt, TimeSpan duration, string description)
        {
            var entries = ReadEntryList();
            if (index < 0 || index >= entries.Count)
                return;

            entries[index] = new HistoryEntry(index, finishedAt, duration, description);
            WriteEntries(entries);
        }

        public void DeleteEntry(int index)
        {
            var entries = ReadEntryList();
            if (index < 0 || index >= entries.Count)
                return;

            entries.RemoveAt(index);
            WriteEntries(entries);
        }

        private List<HistoryEntry> ReadEntryList()
        {
            var entries = new List<HistoryEntry>();
            if (!File.Exists(_historyPath))
                return entries;

            foreach (string line in File.ReadLines(_historyPath))
            {
                string trimmedLine = line.Trim();
                if (TryReadJsonEntry(trimmedLine, entries.Count, out var jsonEntry))
                    entries.Add(jsonEntry);
            }

            return entries;
        }

        private void WriteEntries(IReadOnlyList<HistoryEntry> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries)
            {
                var record = new HistoryRecord
                {
                    FinishedAt = entry.FinishedAt,
                    DurationTicks = entry.Duration.Ticks,
                    Description = entry.Description
                };
                builder.AppendLine(JsonSerializer.Serialize(record, JsonOptions));
            }

            File.WriteAllText(_historyPath, builder.ToString(), Encoding.UTF8);
        }

        private static bool TryReadJsonEntry(string line, int index, out HistoryEntry entry)
        {
            entry = default!;

            if (!line.StartsWith("{", StringComparison.Ordinal))
                return false;

            try
            {
                var record = JsonSerializer.Deserialize<HistoryRecord>(line, JsonOptions);
                if (record == null)
                    return false;

                if (record.FinishedAt == default || record.DurationTicks < 0)
                    return false;

                entry = new HistoryEntry(
                    index,
                    record.FinishedAt,
                    TimeSpan.FromTicks(record.DurationTicks),
                    record.Description ?? string.Empty);

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private sealed class HistoryRecord
        {
            public DateTime FinishedAt { get; set; }
            public long DurationTicks { get; set; }
            public string Description { get; set; } = string.Empty;
        }
    }

    internal sealed record HistoryEntry(int Index, DateTime FinishedAt, TimeSpan Duration, string Description);
}
