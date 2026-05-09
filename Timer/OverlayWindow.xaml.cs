using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Timer
{
    public partial class OverlayWindow : Window
    {
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
            ex |= (WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        }

        public event EventHandler? PlayPauseRequested;
        public event EventHandler? FinishRequested;
        public event EventHandler? ResetRequested;
        public event EventHandler? ExitRequested;

        public OverlayWindow()
        {
            InitializeComponent();
            ApplySettings(0);
        }

        public void UpdateTime(string timeText)
        {
            TimeText.Text = timeText;
        }

        public void ApplySettings(double backgroundOpacity)
        {
            byte alpha = (byte)(Math.Clamp(backgroundOpacity, 0, 1) * byte.MaxValue);
            OverlayBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
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
    }
}
