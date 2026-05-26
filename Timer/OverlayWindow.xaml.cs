using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace Timer
{
    public partial class OverlayWindow : Window
    {
        private bool? _isIconRunning;
        private bool _isIconComplete;
        private bool _isDragModeEnabled;
        private double _backgroundOpacity;

        // Добавление 1: константа для доступа к расширенным стилям окна.
        private const int GWL_EXSTYLE = -20;

        // Добавление 2: флаг WS_EX_TRANSPARENT — делает окно пропускающим события мыши к окнам под ним.
        private const long WS_EX_TRANSPARENT = 0x00000020;

        // Добавление 2b: флаг WS_EX_TOOLWINDOW — помечает окно как "tool window" (не показывается в Alt+Tab
        // и влияет на поведение в списке окон). Явно сохранён по запросу разработчика, чтобы не нарушать
        // существующую визуальную/поведенческую логику окна.
        private const long WS_EX_TOOLWINDOW = 0x00000080;

        // Добавление 3: P/Invoke для 64-битного процесса — используем только Get/SetWindowLongPtr.
        // Причина: приложение разворачивается только в x64, упрощаем код.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // Добавление 4: переопределение OnSourceInitialized — выполняется после создания HWND.
        // Здесь прямо устанавливаем WS_EX_TRANSPARENT и не даём возможности менять это поведение.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Получаем текущие расширенные стили и добавляем WS_EX_TRANSPARENT.
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            // Устанавливаем WS_EX_TRANSPARENT для пропускания мыши и WS_EX_TOOLWINDOW, чтобы сохранить
            // поведение tool-window (как было до изменений).
            ex = _isDragModeEnabled
                ? ex & ~WS_EX_TRANSPARENT
                : ex | WS_EX_TRANSPARENT;
            ex |= WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        }

        public event EventHandler? PlayPauseRequested;
        public event EventHandler? FinishRequested;
        public event EventHandler? ResetRequested;
        public event EventHandler? ExitRequested;
        public event EventHandler? DragCompleted;

        public OverlayWindow()
        {
            InitializeComponent();
            ApplySettings(0);
            UpdateStateIcon(isComplete: false, isRunning: false);
        }

        public void SetDragMode(bool isEnabled)
        {
            if (_isDragModeEnabled == isEnabled)
                return;

            _isDragModeEnabled = isEnabled;
            Cursor = isEnabled ? System.Windows.Input.Cursors.SizeAll : null;
            SetBackgroundOpacity(isEnabled ? 1.0 : _backgroundOpacity);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            ex = isEnabled
                ? ex & ~WS_EX_TRANSPARENT
                : ex | WS_EX_TRANSPARENT;
            ex |= WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        }

        public void UpdateTime(string timeText, bool isComplete, bool isRunning)
        {
            TimeText.Text = isComplete ? "Good job!" : timeText;
            TimeText.Foreground = isComplete
                ? Brushes.LimeGreen
                : new SolidColorBrush(isRunning ? Color.FromRgb(0x76, 0xFF, 0x7A) : Color.FromRgb(0xC6, 0x28, 0x28));
            UpdateStateIcon(isComplete, isRunning);
        }

        private void UpdateStateIcon(bool isComplete, bool isRunning)
        {
            if (_isIconComplete == isComplete && _isIconRunning == isRunning)
                return;

            _isIconComplete = isComplete;
            _isIconRunning = isRunning;
            string relativePath = isComplete
                ? System.IO.Path.Combine("Icons", "Emojis", "emoji-shocked.svg")
                : System.IO.Path.Combine("Icons", isRunning ? "icon_running.svg" : "icon_paused.svg");
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(AppContext.BaseDirectory, "Icons", "icon_paused.svg");

            if (isComplete)
            {
                StateIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                StateIconRotate.Angle = 0;
            }

            double iconSize = isComplete ? 22 : 18;
            StateIcon.Width = iconSize;
            StateIcon.Height = iconSize;
            StateIcon.Source = SvgIconRenderer.Render(path, isComplete ? 43 : 36);

            if (!isComplete && isRunning)
            {
                double startAngle = StateIconRotate.Angle % 360;
                var animation = new DoubleAnimation(startAngle, startAngle + 360, TimeSpan.FromSeconds(4))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = null
                };
                StateIconRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
                return;
            }

            double currentAngle = StateIconRotate.Angle;
            StateIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            StateIconRotate.Angle = currentAngle;
        }

        public void ApplySettings(double backgroundOpacity)
        {
            _backgroundOpacity = Math.Clamp(backgroundOpacity, 0, 1);
            if (!_isDragModeEnabled)
            {
                SetBackgroundOpacity(_backgroundOpacity);
            }
        }

        public void ApplyScale(double scale)
        {
            scale = Math.Clamp(scale, 0.7, 2.1);
            OverlayScaleTransform.ScaleX = scale;
            OverlayScaleTransform.ScaleY = scale;
        }

        private void SetBackgroundOpacity(double backgroundOpacity)
        {
            byte alpha = (byte)(Math.Clamp(backgroundOpacity, 0, 1) * byte.MaxValue);
            OverlayBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x17, 0x17, 0x1A));
        }

        public void PositionOnScreen(Screen screen, string position)
        {
            var bounds = screen.Bounds;

            UpdateLayout();
            double dpiScale = GetDpiScale();

            Size overlaySize = GetOverlayLayoutSize();
            double overlayWidth = overlaySize.Width > 0 ? overlaySize.Width : 170;
            double overlayHeight = overlaySize.Height > 0 ? overlaySize.Height : 50;

            double screenLeft = bounds.Left / dpiScale;
            double screenTop = bounds.Top / dpiScale;
            double screenWidth = bounds.Width / dpiScale;
            double screenHeight = bounds.Height / dpiScale;
            double screenRight = screenLeft + screenWidth;
            double screenBottom = screenTop + screenHeight;
            const int margin = 0;

            (Left, Top) = position switch
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

        private Size GetOverlayLayoutSize()
        {
            try
            {
                UpdateLayout();

                Rect bounds = OverlayBorder.TransformToAncestor(this)
                    .TransformBounds(new Rect(OverlayBorder.RenderSize));

                if (bounds.Width > 0 && bounds.Height > 0)
                    return bounds.Size;
            }
            catch (InvalidOperationException)
            {
            }

            return new Size(ActualWidth, ActualHeight);
        }

        private double GetDpiScale()
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

        private void PlayPauseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            PlayPauseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void FinishMenuItem_Click(object sender, RoutedEventArgs e)
        {
            FinishRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragModeEnabled)
                return;

            try
            {
                DragMove();
            }
            finally
            {
                DragCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
