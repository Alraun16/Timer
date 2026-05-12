using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using ComboBox = System.Windows.Controls.ComboBox;
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

        private static readonly string[] ModifierOptions = { "Win", "Ctrl", "Alt", "Shift", "None" };

        private static readonly string[] KeyOptions =
        {
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"
        };

        private readonly TimerService _timer = new();
        private readonly SettingsService _settingsService = new();
        private readonly HistoryService _historyService = new();

        private readonly DispatcherTimer _tickTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private bool IsTimerRunning => _timer.IsRunning;
        private bool IsTimerPaused => _timer.IsPaused;
        private bool IsTimerIdle => _timer.IsIdle;
        private bool IsTimerCompleted => IsTimerIdle && _timer.Remaining <= TimeSpan.Zero;

        private HwndSource? _hwndSource;
        private OverlayWindow? _overlayWindow;

        private bool _uiReady;
        private bool _isLoadingSettings;
        private TaskDescriptionEditor _taskDescriptionEditor = null!;
        private HistoryPanel _historyPanel = null!;
        private AppIconController _appIconController = null!;


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

            _taskDescriptionEditor.MarkFocusCleared();
            Keyboard.ClearFocus();
            _taskDescriptionEditor.UpdateStateLater();
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
            _taskDescriptionEditor = new TaskDescriptionEditor(
                EditPanel,
                TaskDescriptionTextBox,
                TaskDescriptionPlaceholder,
                TaskDescriptionCounter,
                SaveTaskDescriptionButton);
            _historyPanel = new HistoryPanel(
                _historyService,
                HistoryStack,
                HistoryTotalTextBlock,
                this);

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
                UpdateIcon(AppIconState.Idle);

                new System.Media.SoundPlayer(AppFile("Sounds/reminder.wav")).Play();
                TimerNotificationService.ShowCompleted();
                _historyService.Append(_timer.Duration, _taskDescriptionEditor.SavedText);
                _taskDescriptionEditor.ClearSavedText();
                _historyPanel.Refresh();
            };

            _tickTimer.Tick += (_, _) => _timer.UpdateTick();

            _appIconController = new AppIconController(
                this,
                AppFile("Icons"),
                RestoreFromTray,
                () => PlayPauseButton_Click(this, new RoutedEventArgs()),
                () => ResetButton_Click(this, new RoutedEventArgs()),
                Close);
            PopulateScreens();
            PopulateHotkeySelectors();
            LoadSettings();
            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateIcon(AppIconState.Idle);
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

        private static string AppFile(string fileName)
            => Path.Combine(AppContext.BaseDirectory, fileName);

        private void PopulateScreens()
        {
            ScreenSelector.Items.Clear();

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
                TimerSettings settings = _settingsService.Load();
                CountdownHours.Text = settings.Hours.ToString("D2");
                CountdownMinutes.Text = settings.Minutes.ToString("D2");
                CountdownSeconds.Text = settings.Seconds.ToString("D2");
                BackgroundOpacitySlider.Value = settings.BackgroundOpacityPercent;
                BackgroundOpacityLabel.Text = $"{settings.BackgroundOpacityPercent}%";

                if (settings.ScreenIndex >= 0 && settings.ScreenIndex < WinForms.Screen.AllScreens.Length)
                {
                    ScreenSelector.SelectedIndex = settings.ScreenIndex;
                }

                SelectComboBoxItem(PositionSelector, settings.Position);
                SelectComboBoxItem(PlayHotkeyModifierSelector, settings.PlayHotkeyModifier);
                SelectComboBoxItem(PlayHotkeyKeySelector, settings.PlayHotkeyKey);
                SelectComboBoxItem(OverlayHotkeyModifierSelector, settings.OverlayHotkeyModifier);
                SelectComboBoxItem(OverlayHotkeyKeySelector, settings.OverlayHotkeyKey);

                ApplyDurationFromInputs(resetRemaining: true);
                RefreshOverlay();
            }
            finally
            {
                _isLoadingSettings = false;
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

            _settingsService.Save(settings);
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
                UpdateIcon(AppIconState.Paused);
                UpdateButtonStates();
                UpdateMainPanelVisibility();
                return;
            }

            if (IsTimerCompleted)
            {
                _timer.Reset();
            }

            _timer.Start();
            UpdateIcon(AppIconState.Running);
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
            UpdateIcon(AppIconState.Idle);
            UpdateMainPanelVisibility();
        }

        internal void RepeatTimerFromNotification()
        {
            if (IsTimerRunning)
                return;

            _timer.Reset();
            _timer.Start();
            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateIcon(IsTimerRunning ? AppIconState.Running : AppIconState.Idle);
            UpdateMainPanelVisibility();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            _taskDescriptionEditor.Toggle();
        }

        private void CancelTaskDescriptionButton_Click(object sender, RoutedEventArgs e)
        {
            _taskDescriptionEditor.Cancel();
        }

        private void SaveTaskDescriptionButton_Click(object sender, RoutedEventArgs e)
        {
            _taskDescriptionEditor.Save();
        }

        private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                _overlayWindow = null;
                return;
            }

            _overlayWindow = new OverlayWindow();
            _overlayWindow.PlayPauseRequested += (_, _) => PlayPauseButton_Click(this, new RoutedEventArgs());
            _overlayWindow.FinishRequested += (_, _) => FinishButton_Click(this, new RoutedEventArgs());
            _overlayWindow.ResetRequested += (_, _) => ResetButton_Click(this, new RoutedEventArgs());
            _overlayWindow.ExitRequested += (_, _) => Close();
            _overlayWindow.Closed += (_, _) => _overlayWindow = null;
            RefreshOverlay();
            _overlayWindow.Show();
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyDurationFromInputs(resetRemaining: !IsTimerRunning);
            BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";
            SaveSettings();
            RegisterConfiguredHotkeys();
            RefreshOverlay();
            CollapsePanels();
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_uiReady) return;
            BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";
            RefreshOverlay();
        }

        private void CountdownDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_uiReady || _isLoadingSettings || IsTimerRunning) return;

            ApplyDurationFromInputs(resetRemaining: true);
            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateIcon(AppIconState.Idle);
        }

        private void DurationTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            bool shouldShow = SettingsPanel.Visibility != Visibility.Visible;
            CollapsePanels();
            SettingsPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            bool shouldShow = HistoryPanel.Visibility != Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            if (shouldShow)
            {
                _historyPanel.Refresh();
                _historyPanel.MarkOpened();
            }
        }

        private void CollapsePanels()
        {
            _taskDescriptionEditor.Collapse();
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = Visibility.Collapsed;
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

            _overlayWindow?.UpdateTime(timeText, IsTimerCompleted);
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

        private void RefreshOverlay()
        {
            if (_isLoadingSettings || _overlayWindow == null) return;

            _overlayWindow.ApplySettings((BackgroundOpacitySlider?.Value ?? 0) / 100.0);
            _overlayWindow.UpdateTime(GetFormattedTime(), IsTimerCompleted);
            _overlayWindow.PositionOnScreen(GetSelectedScreen(), GetSelectedText(PositionSelector, "Top Center"));
        }

        private int GetSelectedScreenIndex()
        {
            if (ScreenSelector.SelectedItem is ComboBoxItem item && item.Tag is int index) return index;
            return 0;
        }

        private WinForms.Screen GetSelectedScreen()
        {
            int selectedScreenIndex = GetSelectedScreenIndex();
            if (selectedScreenIndex >= 0 && selectedScreenIndex < WinForms.Screen.AllScreens.Length)
                return WinForms.Screen.AllScreens[selectedScreenIndex];

            return WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
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

        private void UpdateIcon(AppIconState state)
        {
            _appIconController.SetState(state);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized) return;

            Hide();
            _appIconController.ShowTrayIcon();
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
            _overlayWindow?.Close();
            _appIconController.Dispose();
        }
    }
}
