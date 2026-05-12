using System.Windows;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Timer
{
    public partial class App : Application
    {
        private ControllerWindow? _controllerWindow;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();

            _controllerWindow = new ControllerWindow();
            MainWindow = _controllerWindow;

            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs.Kind == ExtendedActivationKind.AppNotification
                && activatedArgs.Data is AppNotificationActivatedEventArgs notificationArgs
                && TimerNotificationService.IsRepeatAction(notificationArgs.Arguments))
            {
                _controllerWindow.RepeatTimerFromNotification();
                return;
            }

            _controllerWindow.Show();
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            if (!TimerNotificationService.IsRepeatAction(args.Arguments))
                return;

            Current.Dispatcher.Invoke(() => _controllerWindow?.RepeatTimerFromNotification());
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppNotificationManager.Default.Unregister();
            base.OnExit(e);
        }
    }
}
