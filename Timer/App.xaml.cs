using System.Windows;

namespace Timer
{
    public partial class App : Application
    {
        private ControllerWindow? _controllerWindow;
        private SingleInstanceService? _singleInstanceService;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            _singleInstanceService = new SingleInstanceService(HandleActivation);
            if (!_singleInstanceService.TryStart(e.Args))
            {
                Shutdown();
                return;
            }

            if (ToastNotificationService.IsRepeatActivation(e.Args))
            {
                Shutdown();
                return;
            }

            ToastNotificationService.Register();

            _controllerWindow = new ControllerWindow();
            MainWindow = _controllerWindow;

            _controllerWindow.Show();
        }

        private void HandleActivation(string[] args)
        {
            Dispatcher.Invoke(() =>
            {
                if (_controllerWindow == null)
                    return;

                if (ToastNotificationService.IsRepeatActivation(args))
                {
                    _controllerWindow.RepeatTimerFromNotification();
                    return;
                }

                _controllerWindow.RestoreFromExternalActivation();
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceService?.Dispose();
            base.OnExit(e);
        }
    }
}
