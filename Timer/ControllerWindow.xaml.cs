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
        private const uint MOD_NOREPEAT = 0x4000;
        private const double CompactWindowHeight = 220;
        private const double HistoryPanelDefaultMaxHeight = 640;
        private const double HistoryScrollViewerDefaultMaxHeight = 570;
        private const double HistoryPanelMinimumHeight = 180;
        private const double HistoryScrollViewerMinimumHeight = 120;
        private const double OverlayBaseScale = 0.7;

        private static readonly string[] ModifierOptions = { "Win", "Ctrl", "Alt", "Shift", "None" };

        private static readonly string[] KeyOptions =
        {
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
            "Q","E","C","V"
        };

        private static readonly string[] DragKeyOptions =
        {
            "Alt","Ctrl","Shift",
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
            "Q","E","C","V"
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
        private bool _isUpdatingDurationInputs;
        private bool _isSavingSettings;
        private bool _hasSettingsPreviewSnapshot;
        private readonly HashSet<int> _activeHotkeys = new();
        private Point? _overlayManualPosition;
        private Point? _settingsPreviewManualPosition;
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
            if (Keyboard.FocusedElement is not TextBox focusedTextBox)
                return;

            if (e.OriginalSource is DependencyObject source && IsInsideTextBox(source))
                return;

            if (focusedTextBox == TaskDescriptionTextBox)
                _taskDescriptionEditor.MarkFocusCleared();

            Keyboard.ClearFocus();
            RootGrid.Focus();

            if (focusedTextBox == TaskDescriptionTextBox)
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
            InitializeSvgIcons();
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
                ToastNotificationService.ShowCompleted(_timer.Duration, _taskDescriptionEditor.SavedText);
                _historyService.Append(_timer.Duration, _taskDescriptionEditor.SavedText);
                _taskDescriptionEditor.ClearSavedText();
                _historyPanel.Refresh();
            };

            _tickTimer.Tick += (_, _) =>
            {
                _timer.UpdateTick();
                ReleaseCompletedHotkeys();
                UpdateOverlayDragMode();
            };

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

        private void InitializeSvgIcons()
        {
            SetButtonSvg(ResetButton, "icon-reset.svg", 30);
            SetButtonSvg(EditButton, "icon-edit.svg", 30);
            SetButtonSvg(FinishButton, "icon-finish.svg", 30);
            SetButtonSvg(HistoryButton, "icon-history.svg", 20);
            SetButtonSvg(SettingsButton, "icon-settings.svg", 17);
            UpdateOverlayButtonState();
        }

        private static void SetButtonSvg(Button button, string fileName, double size)
        {
            string path = AppFile(Path.Combine("Icons", fileName));
            button.Content = new System.Windows.Controls.Image
            {
                Source = SvgIconRenderer.Render(path, (int)Math.Ceiling(size * 2)),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
        }

        private static void SetButtonPng(Button button, string fileName, double size)
        {
            button.Content = new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri($"pack://application:,,,/Icons/{fileName}", UriKind.Absolute)),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

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
            FillComboBox(OverlayDragHotkeyKeySelector, DragKeyOptions);
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
                OverlayScaleSlider.Value = settings.OverlayScalePercent;
                OverlayScaleLabel.Text = $"{(int)OverlayScaleSlider.Value}%";

                if (settings.ScreenIndex >= 0 && settings.ScreenIndex < WinForms.Screen.AllScreens.Length)
                {
                    ScreenSelector.SelectedIndex = settings.ScreenIndex;
                }

                SelectComboBoxItem(PositionSelector, settings.Position);
                SelectComboBoxItem(PlayHotkeyModifierSelector, settings.PlayHotkeyModifier);
                SelectComboBoxItem(PlayHotkeyKeySelector, settings.PlayHotkeyKey);
                SelectComboBoxItem(OverlayHotkeyModifierSelector, settings.OverlayHotkeyModifier);
                SelectComboBoxItem(OverlayHotkeyKeySelector, settings.OverlayHotkeyKey);
                SelectComboBoxItem(OverlayDragHotkeyKeySelector, settings.OverlayDragHotkeyKey);

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

            TimeSpan duration = IsTimerIdle
                ? GetDurationFromInputs()
                : _timer.Duration;

            var settings = new TimerSettings
            {
                Hours = (int)duration.TotalHours,
                Minutes = duration.Minutes,
                Seconds = duration.Seconds,
                BackgroundOpacityPercent = (int)BackgroundOpacitySlider.Value,
                OverlayScalePercent = (int)OverlayScaleSlider.Value,
                ScreenIndex = GetSelectedScreenIndex(),
                Position = GetSelectedText(PositionSelector, "Top Center"),
                PlayHotkeyModifier = GetSelectedText(PlayHotkeyModifierSelector, "Win"),
                PlayHotkeyKey = GetSelectedText(PlayHotkeyKeySelector, "F5"),
                OverlayHotkeyModifier = GetSelectedText(OverlayHotkeyModifierSelector, "Win"),
                OverlayHotkeyKey = GetSelectedText(OverlayHotkeyKeySelector, "F7"),
                OverlayDragHotkeyKey = GetSelectedText(OverlayDragHotkeyKeySelector, "Alt")
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

            int maxValue = tb == CountdownHours ? 99 : 60;
            if (value > maxValue) value = maxValue;
            
            tb.Text = value.ToString("D2");
        }

        private void RegisterConfiguredHotkeys()
        {
            if (_hwndSource == null) return;

            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_PLAY_PAUSE);
            UnregisterHotKey(hwnd, HOTKEY_TOGGLE_OVERLAY);

            RegisterHotKey(hwnd, HOTKEY_PLAY_PAUSE, GetSelectedModifier(PlayHotkeyModifierSelector) | MOD_NOREPEAT, GetSelectedKey(PlayHotkeyKeySelector));
            RegisterHotKey(hwnd, HOTKEY_TOGGLE_OVERLAY, GetSelectedModifier(OverlayHotkeyModifierSelector) | MOD_NOREPEAT, GetSelectedKey(OverlayHotkeyKeySelector));
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
            return GetVirtualKey(selected) ?? 0x74;
        }

        private static uint GetSelectedDragKey(ComboBox comboBox)
        {
            string selected = GetSelectedText(comboBox, "Alt");
            return GetVirtualKey(selected) ?? 0x12;
        }

        private static uint? GetVirtualKey(string selected)
        {
            string normalized = selected.ToUpperInvariant();

            if (normalized.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(normalized[1..], out int number)
                && number is >= 1 and <= 12)
            {
                return (uint)(0x70 + number - 1);
            }

            if (normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z')
            {
                return normalized[0];
            }

            return normalized switch
            {
                "CTRL" => 0x11,
                "ALT" => 0x12,
                "SHIFT" => 0x10,
                _ => null
            };
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
                    handled = true;
                    if (!TryBeginHotkeyPress(HOTKEY_PLAY_PAUSE))
                        break;

                    PlayPauseHotkey();
                    break;
                case HOTKEY_TOGGLE_OVERLAY:
                    handled = true;
                    if (!TryBeginHotkeyPress(HOTKEY_TOGGLE_OVERLAY))
                        break;

                    ToggleOverlayButton_Click(this, new RoutedEventArgs());
                    break;
            }

            return IntPtr.Zero;
        }

        private bool TryBeginHotkeyPress(int hotkeyId)
        {
            if (_activeHotkeys.Contains(hotkeyId))
                return false;

            _activeHotkeys.Add(hotkeyId);
            return true;
        }

        private void ReleaseCompletedHotkeys()
        {
            if (_activeHotkeys.Count == 0)
                return;

            if (_activeHotkeys.Contains(HOTKEY_PLAY_PAUSE) && !IsKeyDown(GetSelectedKey(PlayHotkeyKeySelector)))
                _activeHotkeys.Remove(HOTKEY_PLAY_PAUSE);

            if (_activeHotkeys.Contains(HOTKEY_TOGGLE_OVERLAY) && !IsKeyDown(GetSelectedKey(OverlayHotkeyKeySelector)))
                _activeHotkeys.Remove(HOTKEY_TOGGLE_OVERLAY);
        }

        private static bool IsKeyDown(uint virtualKey)
        {
            return (GetAsyncKeyState((int)virtualKey) & unchecked((short)0x8000)) != 0;
        }

        private void PlayPauseHotkey()
        {
            bool wasTimerRunning = IsTimerRunning;
            bool wasOverlayVisible = _overlayWindow != null;

            PlayPauseButton_Click(this, new RoutedEventArgs());

            if (wasTimerRunning || (!wasOverlayVisible && _overlayWindow == null))
            {
                ToggleOverlayButton_Click(this, new RoutedEventArgs());
            }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            bool shouldAutoShowOverlay =
                IsTimerIdle &&
                _overlayWindow == null;

            if (IsTimerRunning)
            {
                _timer.Pause();
                UpdateTimeDisplay();
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
            UpdateTimeDisplay();
            UpdateIcon(AppIconState.Running);
            UpdateButtonStates();
            UpdateMainPanelVisibility();

            if (shouldAutoShowOverlay && IsTimerRunning)
            {
                ShowOverlay();
            }
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Finish();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsTimerIdle)
            {
                ApplyDurationFromInputs(resetRemaining: true);
            }

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

            if (IsTimerRunning && _overlayWindow == null)
            {
                ShowOverlay();
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            CancelSettingsPreviewIfNeeded();
            SettingsPanel.Visibility = Visibility.Collapsed;
            ExpandWindowToContent();
            _taskDescriptionEditor.Toggle();

            if (HistoryPanel.Visibility == Visibility.Visible)
                UpdateHistoryViewportHeight();

            if (EditPanel.Visibility != Visibility.Visible && HistoryPanel.Visibility != Visibility.Visible)
                CompactWindow();
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
                UpdateOverlayButtonState();
                return;
            }

            ShowOverlay();
        }

        private void ShowOverlay()
        {
            if (_overlayWindow != null)
                return;

            _overlayWindow = new OverlayWindow();
            _overlayWindow.PlayPauseRequested += (_, _) => PlayPauseButton_Click(this, new RoutedEventArgs());
            _overlayWindow.FinishRequested += (_, _) => FinishButton_Click(this, new RoutedEventArgs());
            _overlayWindow.ResetRequested += (_, _) => ResetButton_Click(this, new RoutedEventArgs());
            _overlayWindow.ExitRequested += (_, _) => Close();
            _overlayWindow.DragCompleted += (_, _) => _overlayManualPosition = new Point(_overlayWindow.Left, _overlayWindow.Top);
            _overlayWindow.Closed += (_, _) =>
            {
                _overlayWindow = null;
                UpdateOverlayButtonState();
            };
            _overlayWindow.Show();
            RefreshOverlay();
            UpdateOverlayButtonState();
        }

        private void UpdateOverlayButtonState()
        {
            bool isOverlayVisible = _overlayWindow != null;
            string fileName = isOverlayVisible
                ? "icon-show.svg"
                : "icon-hide.svg";
            SetButtonSvg(ToggleOverlayButton, fileName, 17);
        }

private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
{
    TimerSettings savedSettings = _settingsService.Load();

    bool overlayScreenChanged =
        savedSettings.ScreenIndex != GetSelectedScreenIndex();

    bool overlayPlacementChanged =
        overlayScreenChanged
        || savedSettings.Position != GetSelectedText(PositionSelector, "Top Center");

    if (IsTimerIdle)
    {
        ApplyDurationFromInputs(resetRemaining: true);
    }

    BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";

    SaveSettings();
    RegisterConfiguredHotkeys();

    if (overlayScreenChanged)
    {
        _overlayManualPosition = null;
    }

    ApplyOverlayBackgroundOpacity();
    _overlayWindow?.UpdateTime(GetFormattedTime(), IsTimerCompleted, IsTimerRunning);

    if (overlayPlacementChanged)
    {
        PositionOverlay();
    }

    _isSavingSettings = true;
    try
    {
        CollapsePanels();
        _hasSettingsPreviewSnapshot = false;
    }
    finally
    {
        _isSavingSettings = false;
    }
}

        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_uiReady) return;
            BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";
            ApplyOverlayBackgroundOpacity();
        }

        private void OverlayScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_uiReady) return;
            OverlayScaleLabel.Text = $"{(int)OverlayScaleSlider.Value}%";
            ApplyOverlayScale();
            PositionOverlayAfterLayout();
        }

        private void ScreenSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady || _isLoadingSettings)
                return;

            _overlayManualPosition = null;
            PositionOverlay();
        }

        private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            CollapsePanels();
        }

        private void CountdownDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_uiReady || _isLoadingSettings || _isUpdatingDurationInputs || !IsTimerIdle) return;

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
            if (shouldShow)
            {
                BeginSettingsPreview();
                ExpandWindowToContent();
            }

            SettingsPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            if (!shouldShow)
                CompactWindow();
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            bool shouldShow = HistoryPanel.Visibility != Visibility.Visible;
            if (shouldShow)
                ExpandWindowToContent();

            CancelSettingsPreviewIfNeeded();
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            if (shouldShow)
            {
                _historyPanel.Refresh();
                _historyPanel.MarkOpened();
                UpdateHistoryViewportHeight();
            }
            else
            {
                if (EditPanel.Visibility == Visibility.Visible)
                    ExpandWindowToContent();
                else
                    CompactWindow();
            }
        }

        private void CollapsePanels()
        {
            CancelSettingsPreviewIfNeeded();
            _taskDescriptionEditor.Collapse();
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = Visibility.Collapsed;
            CompactWindow();
        }

        private void BeginSettingsPreview()
        {
            _settingsPreviewManualPosition = _overlayManualPosition;
            _hasSettingsPreviewSnapshot = true;
        }

        private void CancelSettingsPreviewIfNeeded()
        {
            if (_isSavingSettings || !_hasSettingsPreviewSnapshot || SettingsPanel.Visibility != Visibility.Visible)
                return;

            _overlayManualPosition = _settingsPreviewManualPosition;
            _hasSettingsPreviewSnapshot = false;
            LoadSettings();
        }

        private void CompactWindow()
        {
            MinHeight = CompactWindowHeight;
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;
            Dispatcher.BeginInvoke(() =>
            {
                InvalidateMeasure();
                UpdateLayout();
                SizeToContent = SizeToContent.Height;
            }, DispatcherPriority.Background);
        }

        private void ExpandWindowToContent()
        {
            Height = double.NaN;
            MinHeight = 220;
            SizeToContent = SizeToContent.Height;
            Dispatcher.BeginInvoke(() =>
            {
                InvalidateMeasure();
                UpdateLayout();
                SizeToContent = SizeToContent.Height;
            }, DispatcherPriority.Background);
        }

        private void UpdateHistoryViewportHeight()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (HistoryPanel.Visibility != Visibility.Visible)
                    return;

                double usedHeight = 0;
                for (int i = 0; i < 5 && i < RootGrid.RowDefinitions.Count; i++)
                {
                    usedHeight += RootGrid.RowDefinitions[i].ActualHeight;
                }

                double panelMaxHeight = Math.Min(HistoryPanelDefaultMaxHeight, MaxHeight - usedHeight - 12);
                if (double.IsNaN(panelMaxHeight) || panelMaxHeight <= 0)
                    return;

                panelMaxHeight = Math.Max(HistoryPanelMinimumHeight, panelMaxHeight);
                HistoryPanel.MaxHeight = panelMaxHeight;

                double headerHeight = HistoryHeader.ActualHeight
                                   + HistoryHeader.Margin.Top
                                   + HistoryHeader.Margin.Bottom;
                double verticalPadding = HistoryPanel.Padding.Top + HistoryPanel.Padding.Bottom;
                double scrollMaxHeight = panelMaxHeight - headerHeight - verticalPadding;

                HistoryScrollViewer.MaxHeight = Math.Min(
                    HistoryScrollViewerDefaultMaxHeight,
                    Math.Max(HistoryScrollViewerMinimumHeight, scrollMaxHeight));

                InvalidateMeasure();
                UpdateLayout();
                SizeToContent = SizeToContent.Height;
            }, DispatcherPriority.Background);
        }

        private void ApplyDurationFromInputs(bool resetRemaining)
        {
            _timer.SetDuration(GetDurationFromInputs(), resetRemaining);
        }

        private TimeSpan GetDurationFromInputs()
        {
            int hours = GetInputNumber(CountdownHours);
            int minutes = GetInputNumber(CountdownMinutes);
            int seconds = GetInputNumber(CountdownSeconds);

            return TimeSpan.FromHours(hours)
                 + TimeSpan.FromMinutes(minutes)
                 + TimeSpan.FromSeconds(seconds);
        }

        private static int GetInputNumber(TextBox textBox)
        {
            return int.TryParse(textBox.Text, out int value) ? value : 0;
        }

        private void UpdateTimeDisplay()
        {
            string timeText = GetFormattedTime();
            TimeDisplay.Text = timeText;

            if (!IsTimerIdle)
            {
                SetDurationInputs(_timer.Remaining);
            }

            _overlayWindow?.UpdateTime(timeText, IsTimerCompleted, IsTimerRunning);
        }

        private string GetFormattedTime() => FormatTime(_timer.Remaining);

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        private void UpdateMainPanelVisibility()
        {
            DurationPanel.Visibility = Visibility.Visible;
            SetDurationInputs(IsTimerIdle ? _timer.Duration : _timer.Remaining);
            SetDurationInputsEditable(IsTimerIdle);

            TimeDisplay.Visibility = Visibility.Collapsed;
        }

        private void SetDurationInputs(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;

            _isUpdatingDurationInputs = true;
            try
            {
                CountdownHours.Text = ((int)time.TotalHours).ToString("D2");
                CountdownMinutes.Text = time.Minutes.ToString("D2");
                CountdownSeconds.Text = time.Seconds.ToString("D2");
            }
            finally
            {
                _isUpdatingDurationInputs = false;
            }
        }

        private void SetDurationInputsEditable(bool isEditable)
        {
            SetDurationInputEditable(CountdownHours, isEditable);
            SetDurationInputEditable(CountdownMinutes, isEditable);
            SetDurationInputEditable(CountdownSeconds, isEditable);
        }

        private static void SetDurationInputEditable(TextBox textBox, bool isEditable)
        {
            textBox.IsReadOnly = !isEditable;
            textBox.Focusable = isEditable;
            textBox.IsHitTestVisible = isEditable;
        }

        private void RefreshOverlay()
        {
            if (_isLoadingSettings || _overlayWindow == null) return;

            ApplyOverlayBackgroundOpacity();
            ApplyOverlayScale();
            _overlayWindow.UpdateTime(GetFormattedTime(), IsTimerCompleted, IsTimerRunning);
            PositionOverlay();
            UpdateOverlayDragMode();
        }

        private void PositionOverlay()
        {
            if (_overlayWindow == null)
                return;

            if (_overlayManualPosition.HasValue)
            {
                _overlayWindow.Left = _overlayManualPosition.Value.X;
                _overlayWindow.Top = _overlayManualPosition.Value.Y;
                return;
            }

            _overlayWindow.PositionOnScreen(GetSelectedScreen(), GetSelectedText(PositionSelector, "Top Center"));
        }

        private void PositionOverlayAfterLayout()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _overlayWindow?.UpdateLayout();
                PositionOverlay();
            }, DispatcherPriority.Render);
        }

        private void UpdateOverlayDragMode()
        {
            if (_overlayWindow == null)
                return;

            _overlayWindow.SetDragMode(IsKeyDown(GetSelectedDragKey(OverlayDragHotkeyKeySelector)));
        }

        private void ApplyOverlayBackgroundOpacity()
        {
            if (_isLoadingSettings || _overlayWindow == null) return;

            _overlayWindow.ApplySettings((BackgroundOpacitySlider?.Value ?? 0) / 100.0);
        }

        private void ApplyOverlayScale()
        {
            if (_isLoadingSettings || _overlayWindow == null) return;

            _overlayWindow.ApplyScale(OverlayBaseScale * ((OverlayScaleSlider?.Value ?? 100) / 100.0));
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
                (IsTimerRunning || IsTimerPaused || _timer.Remaining < _timer.Duration);

            if (IsTimerRunning)
            {
                PlayPauseButton.Style = (Style)FindResource("PauseIconButton");
                SetButtonSvg(PlayPauseButton, "icon-pause.svg", 33);
            }
            else
            {
                PlayPauseButton.Style = (Style)FindResource("PlayIconButton");
                SetButtonSvg(PlayPauseButton, "icon-play.svg", 33);
            }
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
            RestoreFromExternalActivation();
        }

        internal void RestoreFromExternalActivation()
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
