using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Timer
{
    internal sealed class AppIconController : IDisposable
    {
        private readonly Window _window;
        private readonly string _iconsPath;
        private readonly Dictionary<AppIconState, Drawing.Icon> _notifyIcons = new();
        private readonly Action _showRequested;
        private readonly Action _playPauseRequested;
        private readonly Action _resetRequested;
        private readonly Action _exitRequested;

        private WinForms.NotifyIcon? _notifyIcon;

        public AppIconController(
            Window window,
            string iconsPath,
            Action showRequested,
            Action playPauseRequested,
            Action resetRequested,
            Action exitRequested)
        {
            _window = window;
            _iconsPath = iconsPath;
            _showRequested = showRequested;
            _playPauseRequested = playPauseRequested;
            _resetRequested = resetRequested;
            _exitRequested = exitRequested;

            LoadNotifyIcon(AppIconState.Idle, "icon_idle.png");
            LoadNotifyIcon(AppIconState.Paused, "icon_paused.png");
            LoadNotifyIcon(AppIconState.Running, "icon_running.png");

            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "Timer",
                Visible = true,
                Icon = GetNotifyIcon(AppIconState.Idle),
                ContextMenuStrip = new WinForms.ContextMenuStrip()
            };
            _notifyIcon.ContextMenuStrip.Items.Add("Show", null, (_, _) => _showRequested());
            _notifyIcon.ContextMenuStrip.Items.Add("Play / Pause", null, (_, _) => _playPauseRequested());
            _notifyIcon.ContextMenuStrip.Items.Add("Reset", null, (_, _) => _resetRequested());
            _notifyIcon.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => _exitRequested());
            _notifyIcon.DoubleClick += (_, _) => _showRequested();

            SetState(AppIconState.Idle);
        }

        public void SetState(AppIconState state)
        {
            string fileName = state switch
            {
                AppIconState.Running => "icon_running.png",
                AppIconState.Paused => "icon_paused.png",
                _ => "icon_idle.png"
            };

            _window.Icon = new BitmapImage(new Uri($"pack://application:,,,/Icons/{fileName}", UriKind.Absolute));

            if (_notifyIcon != null)
            {
                _notifyIcon.Icon = GetNotifyIcon(state);
            }
        }

        public void ShowTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
            }
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();

            foreach (var icon in _notifyIcons.Values)
            {
                icon.Dispose();
            }
        }

        private void LoadNotifyIcon(AppIconState state, string fileName)
        {
            string path = Path.Combine(_iconsPath, fileName);
            if (!File.Exists(path)) return;

            using var bitmap = new Drawing.Bitmap(path);
            IntPtr handle = bitmap.GetHicon();
            _notifyIcons[state] = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
        }

        private Drawing.Icon GetNotifyIcon(AppIconState state)
        {
            if (_notifyIcons.TryGetValue(state, out var icon)) return icon;
            if (_notifyIcons.TryGetValue(AppIconState.Idle, out var idleIcon)) return idleIcon;
            return Drawing.SystemIcons.Application;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal enum AppIconState
    {
        Idle,
        Paused,
        Running
    }
}
