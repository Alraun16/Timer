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
            if (!File.Exists(_historyPath)) yield break;

            foreach (string line in File.ReadLines(_historyPath))
            {
                string trimmedLine = line.Trim();
                if (TryReadJsonEntry(trimmedLine, out var jsonEntry))
                {
                    yield return jsonEntry;
                }
            }
        }

        private static bool TryReadJsonEntry(string line, out HistoryEntry entry)
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

    internal sealed record HistoryEntry(DateTime FinishedAt, TimeSpan Duration, string Description);
}
