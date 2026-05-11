using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using ComboBox = System.Windows.Controls.ComboBox;
using Drawing = System.Drawing;
using TextBox = System.Windows.Controls.TextBox;
using WinForms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;

namespace Timer
{
    public partial class ControllerWindow : Window
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_PLAY_PAUSE = 1;
        private const int HOTKEY_TOGGLE_OVERLAY = 2;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly string[] ModifierOptions = { "Win", "Ctrl", "Alt", "Shift", "None" };

        private static readonly string[] KeyOptions =
        {
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"
        };

        private readonly TimerService _timer = new();

        private readonly DispatcherTimer _tickTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private bool IsTimerRunning => _timer.IsRunning;
        private bool IsTimerPaused => _timer.IsPaused;
        private bool IsTimerIdle => _timer.IsIdle;
        private bool IsTimerCompleted => IsTimerIdle && _timer.Remaining <= TimeSpan.Zero;

        private readonly List<OverlayWindow> _overlayWindows = new();
        private readonly Dictionary<TimerIconState, Drawing.Icon> _notifyIcons = new();

        private WinForms.NotifyIcon? _notifyIcon;
        private HwndSource? _hwndSource;

        private bool _uiReady;
        private bool _isLoadingSettings;


        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe &&
                fe is Button)
                return;

            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
        
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is not TextBox)
                return;

            if (e.OriginalSource is DependencyObject source && IsInsideTextBox(source))
                return;

            Keyboard.ClearFocus();
        }

        private static bool IsInsideTextBox(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is TextBox)
                    return true;

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        public ControllerWindow()
        {
            InitializeComponent();

            _uiReady = true;

            _timer.Tick += remaining =>
            {
                UpdateTimeDisplay();
                UpdateButtonStates();
            };

            _timer.Completed += () =>
            {
                UpdateTimeDisplay();
                UpdateButtonStates();
                UpdateMainPanelVisibility();
                UpdateIcon(TimerIconState.Finished);

                new System.Media.SoundPlayer(AppFile("Sounds/reminder.wav")).Play();
                AppendHistory(_timer.Duration);
                RefreshHistoryView();
            };

            _tickTimer.Tick += (_, _) => _timer.UpdateTick();

            InitializeTrayIcon();
            PopulateScreens();
            PopulateHotkeySelectors();
            LoadSettings();
            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateIcon(TimerIconState.Idle);
            UpdateMainPanelVisibility();

            _tickTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(HwndHook);

            RegisterConfiguredHotkeys();
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static string AppFile(string fileName)
            => Path.Combine(AppContext.BaseDirectory, fileName);

        private void InitializeTrayIcon()
        {
            LoadNotifyIcon(TimerIconState.Idle, "icon_idle.png");
            LoadNotifyIcon(TimerIconState.Paused, "icon_paused.png");
            LoadNotifyIcon(TimerIconState.Running, "icon_running.png");
            LoadNotifyIcon(TimerIconState.Finished, "icon_finished.png");

            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "Timer",
                Visible = true,
                Icon = GetNotifyIcon(TimerIconState.Idle),
                ContextMenuStrip = new WinForms.ContextMenuStrip()
            };
            _notifyIcon.ContextMenuStrip.Items.Add("Show", null, (_, _) => RestoreFromTray());

            _notifyIcon.ContextMenuStrip.Items.Add("Play / Pause", null,
                (_, _) => PlayPauseButton_Click(this, new RoutedEventArgs()));

            _notifyIcon.ContextMenuStrip.Items.Add("Reset", null,
                (_, _) => ResetButton_Click(this, new RoutedEventArgs()));

            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Close());
            _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        }

        private void LoadNotifyIcon(TimerIconState state, string fileName)
        {
            string path = AppFile(Path.Combine("Icons", fileName));
            if (!File.Exists(path)) return;

            using var bitmap = new Drawing.Bitmap(path);
            IntPtr handle = bitmap.GetHicon();
            _notifyIcons[state] = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
        }

        private Drawing.Icon GetNotifyIcon(TimerIconState state)
        {
            if (_notifyIcons.TryGetValue(state, out var icon)) return icon;
            if (_notifyIcons.TryGetValue(TimerIconState.Idle, out var idleIcon)) return idleIcon;
            return Drawing.SystemIcons.Application;
        }

        private void PopulateScreens()
        {
            ScreenSelector.Items.Clear();
            ScreenSelector.Items.Add(new ComboBoxItem { Content = "All Screens", Tag = -1 });

            var screens = WinForms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                string name = screen.Primary ? $"Screen {i + 1} (Primary)" : $"Screen {i + 1}";
                name += $" - {screen.Bounds.Width}x{screen.Bounds.Height}";
                ScreenSelector.Items.Add(new ComboBoxItem { Content = name, Tag = i });
            }

            ScreenSelector.SelectedIndex = screens.Length > 1 ? 1 : 0;
        }

        private void PopulateHotkeySelectors()
        {
            FillComboBox(PlayHotkeyModifierSelector, ModifierOptions);
            FillComboBox(OverlayHotkeyModifierSelector, ModifierOptions);
            FillComboBox(PlayHotkeyKeySelector, KeyOptions);
            FillComboBox(OverlayHotkeyKeySelector, KeyOptions);
        }

        private static void FillComboBox(ComboBox comboBox, IEnumerable<string> values)
        {
            comboBox.Items.Clear();
            foreach (string value in values)
            {
                comboBox.Items.Add(new ComboBoxItem { Content = value });
            }
        }

        private void LoadSettings()
        {
            _isLoadingSettings = true;
            try
            {
                TimerSettings settings = ReadSettings();
                CountdownHours.Text = settings.Hours.ToString("D2");
                CountdownMinutes.Text = settings.Minutes.ToString("D2");
                CountdownSeconds.Text = settings.Seconds.ToString("D2");
                BackgroundOpacitySlider.Value = settings.BackgroundOpacityPercent;
                BackgroundOpacityLabel.Text = $"{settings.BackgroundOpacityPercent}%";

                if (settings.ScreenIndex >= -1 && settings.ScreenIndex < WinForms.Screen.AllScreens.Length)
                {
                    ScreenSelector.SelectedIndex = settings.ScreenIndex + 1;
                }

                SelectComboBoxItem(PositionSelector, settings.Position);
                SelectComboBoxItem(PlayHotkeyModifierSelector, settings.PlayHotkeyModifier);
                SelectComboBoxItem(PlayHotkeyKeySelector, settings.PlayHotkeyKey);
                SelectComboBoxItem(OverlayHotkeyModifierSelector, settings.OverlayHotkeyModifier);
                SelectComboBoxItem(OverlayHotkeyKeySelector, settings.OverlayHotkeyKey);

                ApplyDurationFromInputs(resetRemaining: true);
                ApplyAllOverlaySettings();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private static TimerSettings ReadSettings()
        {
            string path = AppFile("Settings");
            if (!File.Exists(path)) return new TimerSettings();

            try
            {
                return JsonSerializer.Deserialize<TimerSettings>(File.ReadAllText(path)) ?? new TimerSettings();
            }
            catch
            {
                return new TimerSettings();
            }
        }

        private void SaveSettings()
        {
            if (_isLoadingSettings) return;

            var settings = new TimerSettings
            {
                Hours = GetInputNumber(CountdownHours),
                Minutes = GetInputNumber(CountdownMinutes),
                Seconds = GetInputNumber(CountdownSeconds),
                BackgroundOpacityPercent = (int)BackgroundOpacitySlider.Value,
                ScreenIndex = GetSelectedScreenIndex(),
                Position = GetSelectedText(PositionSelector, "Top Center"),
                PlayHotkeyModifier = GetSelectedText(PlayHotkeyModifierSelector, "Win"),
                PlayHotkeyKey = GetSelectedText(PlayHotkeyKeySelector, "F5"),
                OverlayHotkeyModifier = GetSelectedText(OverlayHotkeyModifierSelector, "Win"),
                OverlayHotkeyKey = GetSelectedText(OverlayHotkeyKeySelector, "F7")
            };

            File.WriteAllText(AppFile("Settings"), JsonSerializer.Serialize(settings, JsonOptions));
        }

        private void SelectComboBoxItem(ComboBox comboBox, string value)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem && comboBoxItem.Content?.ToString() == value)
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void TimeBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }

        private void TimeBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        private void CountdownDuration_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            if (!int.TryParse(tb.Text, out int value))
                value = 0;

            if (value > 60) value = 60;
            
            tb.Text = value.ToString("D2");
        }

        private void RegisterConfiguredHotkeys()
        {
            if (_hwndSource == null) return;

            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_PLAY_PAUSE);
            UnregisterHotKey(hwnd, HOTKEY_TOGGLE_OVERLAY);

            RegisterHotKey(hwnd, HOTKEY_PLAY_PAUSE, GetSelectedModifier(PlayHotkeyModifierSelector), GetSelectedKey(PlayHotkeyKeySelector));
            RegisterHotKey(hwnd, HOTKEY_TOGGLE_OVERLAY, GetSelectedModifier(OverlayHotkeyModifierSelector), GetSelectedKey(OverlayHotkeyKeySelector));
        }

        private static uint GetSelectedModifier(ComboBox comboBox)
        {
            return GetSelectedText(comboBox, "Win") switch
            {
                "Ctrl" => 0x0002,
                "Alt" => 0x0001,
                "Shift" => 0x0004,
                "None" => 0x0000,
                _ => 0x0008
            };
        }

        private static uint GetSelectedKey(ComboBox comboBox)
        {
            string selected = GetSelectedText(comboBox, "F5");
            if (selected.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(selected[1..], out int number)
                && number is >= 1 and <= 12)
            {
                return (uint)(0x70 + number - 1);
            }

            return 0x74;
        }

        private static string GetSelectedText(ComboBox comboBox, string fallback)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;

            switch (wParam.ToInt32())
            {
                case HOTKEY_PLAY_PAUSE:
                    PlayPauseButton_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;
                case HOTKEY_TOGGLE_OVERLAY:
                    ToggleOverlayButton_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;
            }

            return IntPtr.Zero;
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsTimerRunning)
            {
                _timer.Pause();
                UpdateIcon(TimerIconState.Paused);
                UpdateButtonStates();
                UpdateMainPanelVisibility();
                return;
            }

            if (IsTimerCompleted)
            {
                _timer.Reset();
            }

            _timer.Start();
            UpdateIcon(TimerIconState.Running);
            UpdateButtonStates();
            UpdateMainPanelVisibility();
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Finish();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyDurationFromInputs(resetRemaining: true);
            _timer.Reset();

            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateIcon(TimerIconState.Idle);
            UpdateMainPanelVisibility();
        }


        private void AppendHistory(TimeSpan duration)
        {
            string line = $"{DateTime.Now:dd.MM.yyyy H:mm} - {FormatTime(duration)}{Environment.NewLine}";
            File.AppendAllText(AppFile("History"), line);
        }

        private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindows.Count > 0)
            {
                foreach (var overlay in _overlayWindows) overlay.Close();
                _overlayWindows.Clear();
                return;
            }

            int selectedScreenIndex = GetSelectedScreenIndex();
            if (selectedScreenIndex == -1)
            {
                foreach (var screen in WinForms.Screen.AllScreens)
                {
                    CreateOverlayForScreen(screen);
                }
            }
            else if (selectedScreenIndex >= 0 && selectedScreenIndex < WinForms.Screen.AllScreens.Length)
            {
                CreateOverlayForScreen(WinForms.Screen.AllScreens[selectedScreenIndex]);
            }
        }

        private void CreateOverlayForScreen(WinForms.Screen screen)
        {
            var overlay = new OverlayWindow();
            overlay.PlayPauseRequested += (_, _) => PlayPauseButton_Click(this, new RoutedEventArgs());
            overlay.FinishRequested += (_, _) => FinishButton_Click(this, new RoutedEventArgs());
            overlay.ResetRequested += (_, _) => ResetButton_Click(this, new RoutedEventArgs());
            overlay.ExitRequested += (_, _) => Close();

            ApplyOverlaySettings(overlay);
            PositionOverlay(overlay, screen);
            overlay.Show();
            overlay.UpdateTime(GetFormattedTime());
            _overlayWindows.Add(overlay);
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyDurationFromInputs(resetRemaining: !IsTimerRunning);
            BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";
            SaveSettings();
            RegisterConfiguredHotkeys();
            ApplyAllOverlaySettings();
            ReopenOverlays();
            CollapsePanels();
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_uiReady) return;
            BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";
            ApplyAllOverlaySettings();
        }

        private void CountdownDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_uiReady || _isLoadingSettings || IsTimerRunning) return;

            ApplyDurationFromInputs(resetRemaining: true);
            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateIcon(TimerIconState.Idle);
        }

        private void DurationTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            bool shouldShow = SettingsPanel.Visibility != Visibility.Visible;
            StatisticsPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StatisticsButton_Click(object sender, RoutedEventArgs e)
        {
            bool shouldShow = StatisticsPanel.Visibility != Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Collapsed;
            StatisticsPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            if (shouldShow) RefreshHistoryView();
        }

        private void CollapsePanels()
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            StatisticsPanel.Visibility = Visibility.Collapsed;
        }

        private void RefreshHistoryView()
        {
            if (HistoryStack == null) return;

            HistoryStack.Children.Clear();
            var entries = ReadHistoryEntries().ToList();
            if (entries.Count == 0)
            {
                HistoryStack.Children.Add(new TextBlock
                {
                    Text = "История пуста",
                    Foreground = (Brush)FindResource("MutedTextBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            foreach (var group in entries.GroupBy(entry => entry.Start.Date).OrderByDescending(group => group.Key))
            {
                TimeSpan total = TimeSpan.FromTicks(group.Sum(entry => entry.Duration.Ticks));
                HistoryStack.Children.Add(new TextBlock
                {
                    Text = $"{group.Key:dd.MM.yyyy} наработано {total.TotalHours.ToString("0.00", CultureInfo.InvariantCulture)} часов",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                foreach (var entry in group.OrderByDescending(entry => entry.Start))
                {
                    HistoryStack.Children.Add(new TextBlock
                    {
                        Text = $"В {entry.Start:HH:mm} - {FormatDurationText(entry.Duration)}",
                        Foreground = (Brush)FindResource("MutedTextBrush"),
                        Margin = new Thickness(12, 0, 0, 4)
                    });
                }
            }
        }

        private static IEnumerable<HistoryEntry> ReadHistoryEntries()
        {
            string path = AppFile("History");
            if (!File.Exists(path)) yield break;

            var regex = new Regex(@"^(?<date>\d{2}\.\d{2}\.\d{4}\s+\d{1,2}:\d{2})\s+-\s+(?<duration>\d{2}:\d{2}:\d{2})$");
            foreach (string line in File.ReadLines(path))
            {
                var match = regex.Match(line.Trim());
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

        private static string FormatDurationText(TimeSpan duration)
        {
            int totalMinutes = (int)Math.Round(duration.TotalMinutes);
            if (duration > TimeSpan.Zero && totalMinutes == 0) totalMinutes = 1;

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            if (hours == 0)
            {
                return $"{minutes} {GetMinuteWord(minutes)}";
            }

            if (minutes == 0)
            {
                return $"{hours} {GetHourWord(hours)}";
            }

            return $"{hours} {GetHourWord(hours)} {minutes} {GetMinuteWord(minutes)}";
        }

        private static string GetHourWord(int value)
        {
            int lastTwo = value % 100;
            int last = value % 10;
            if (lastTwo is >= 11 and <= 14) return "часов";

            return last switch
            {
                1 => "час",
                >= 2 and <= 4 => "часа",
                _ => "часов"
            };
        }

        private static string GetMinuteWord(int value)
        {
            int lastTwo = value % 100;
            int last = value % 10;
            if (lastTwo is >= 11 and <= 14) return "минут";

            return last switch
            {
                1 => "минута",
                >= 2 and <= 4 => "минуты",
                _ => "минут"
            };
        }

        private void ReopenOverlays()
        {
            if (_isLoadingSettings || _overlayWindows.Count == 0) return;

            foreach (var overlay in _overlayWindows) overlay.Close();
            _overlayWindows.Clear();
            ToggleOverlayButton_Click(this, new RoutedEventArgs());
        }

        private void ApplyDurationFromInputs(bool resetRemaining)
        {
            int hours = GetInputNumber(CountdownHours);
            int minutes = GetInputNumber(CountdownMinutes);
            int seconds = GetInputNumber(CountdownSeconds);

            var duration = TimeSpan.FromHours(hours)
                         + TimeSpan.FromMinutes(minutes)
                         + TimeSpan.FromSeconds(seconds);

            _timer.SetDuration(duration, resetRemaining);
        }

        private static int GetInputNumber(TextBox textBox)
        {
            return int.TryParse(textBox.Text, out int value) ? value : 0;
        }

        private void UpdateTimeDisplay()
        {
            string timeText = GetFormattedTime();
            TimeDisplay.Text = timeText;

            foreach (var overlay in _overlayWindows)
            {
                overlay.UpdateTime(timeText);
            }
        }

        private string GetFormattedTime() => FormatTime(_timer.Remaining);

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        private void UpdateMainPanelVisibility()
        {
            DurationPanel.Visibility = IsTimerIdle
                ? Visibility.Visible
                : Visibility.Collapsed;

            TimeDisplay.Visibility = IsTimerIdle
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ApplyAllOverlaySettings()
        {
            foreach (var overlay in _overlayWindows)
            {
                ApplyOverlaySettings(overlay);
            }
        }

        private void ApplyOverlaySettings(OverlayWindow overlay)
        {
            overlay.ApplySettings((BackgroundOpacitySlider?.Value ?? 0) / 100.0);
        }

        private void PositionOverlay(OverlayWindow overlay, WinForms.Screen screen)
        {
            var bounds = screen.Bounds;
            string position = GetSelectedText(PositionSelector, "Top Center");

            overlay.UpdateLayout();
            double dpiScale = GetDpiScaleForScreen();

            double overlayWidth = overlay.ActualWidth > 0 ? overlay.ActualWidth : 170;
            double overlayHeight = overlay.ActualHeight > 0 ? overlay.ActualHeight : 50;

            double screenLeft = bounds.Left / dpiScale;
            double screenTop = bounds.Top / dpiScale;
            double screenWidth = bounds.Width / dpiScale;
            double screenHeight = bounds.Height / dpiScale;
            double screenRight = screenLeft + screenWidth;
            double screenBottom = screenTop + screenHeight;
            const int margin = 10;

            (overlay.Left, overlay.Top) = position switch
            {
                "Top Left" => (screenLeft + margin, screenTop + margin),
                "Top Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin),
                "Top Right" => (screenRight - overlayWidth - margin, screenTop + margin),
                "Bottom Left" => (screenLeft + margin, screenBottom - overlayHeight - margin),
                "Bottom Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenBottom - overlayHeight - margin),
                "Bottom Right" => (screenRight - overlayWidth - margin, screenBottom - overlayHeight - margin),
                _ => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin)
            };
        }

        private double GetDpiScaleForScreen()
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            }
            catch
            {
                return 1.0;
            }
        }

        private int GetSelectedScreenIndex()
        {
            if (ScreenSelector.SelectedItem is ComboBoxItem item && item.Tag is int index) return index;
            return -1;
        }

        private void UpdateButtonStates()
        {
            bool canRun = _timer.Duration > TimeSpan.Zero || _timer.Remaining > TimeSpan.Zero;

            PlayPauseButton.IsEnabled = canRun;

            FinishButton.IsEnabled =
                !IsTimerCompleted &&
                (IsTimerRunning || _timer.Remaining < _timer.Duration);

            PlayPauseIcon.Source = new BitmapImage(new Uri(IsTimerRunning
                ? "pack://application:,,,/Icons/icon-pause.png"
                : "pack://application:,,,/Icons/icon-play.png", UriKind.Absolute));
        }

        private void UpdateIcon(TimerIconState state)
        {
            string fileName = state switch
            {
                TimerIconState.Paused => "icon_paused.png",
                TimerIconState.Running => "icon_running.png",
                TimerIconState.Finished => "icon_finished.png",
                _ => "icon_idle.png"
            };

            var image = new BitmapImage(new Uri($"pack://application:,,,/Icons/{fileName}", UriKind.Absolute));
            Icon = image;

            if (_notifyIcon != null)
            {
                _notifyIcon.Icon = GetNotifyIcon(state);
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized) return;

            Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_PLAY_PAUSE);
            UnregisterHotKey(helper.Handle, HOTKEY_TOGGLE_OVERLAY);

            _tickTimer.Stop();
            foreach (var overlay in _overlayWindows) overlay.Close();
            _notifyIcon?.Dispose();

            foreach (var icon in _notifyIcons.Values)
            {
                icon.Dispose();
            }
        }

        private enum TimerIconState
        {
            Idle,
            Paused,
            Running,
            Finished
        }

        private sealed class TimerSettings
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

        private sealed record HistoryEntry(DateTime Start, TimeSpan Duration);
    }
}
