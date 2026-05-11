using System;
using System.IO;
using System.Text.Json;

namespace Timer
{
    internal sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "Settings");

        public TimerSettings Load()
        {
            if (!File.Exists(_settingsPath))
            {
                return new TimerSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<TimerSettings>(File.ReadAllText(_settingsPath)) ?? new TimerSettings();
            }
            catch
            {
                return new TimerSettings();
            }
        }

        public void Save(TimerSettings settings)
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
    }

    internal sealed class TimerSettings
    {
        public int Hours { get; set; }
        public int Minutes { get; set; } = 5;
        public int Seconds { get; set; }
        public int BackgroundOpacityPercent { get; set; }
        public int ScreenIndex { get; set; } = -1;
        public string Position { get; set; } = "Top Center";
        public string PlayHotkeyModifier { get; set; } = "Win";
        public string PlayHotkeyKey { get; set; } = "F5";
        public string OverlayHotkeyModifier { get; set; } = "Win";
        public string OverlayHotkeyKey { get; set; } = "F7";
    }
}
