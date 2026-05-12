using System.Collections.Generic;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Timer
{
    internal static class TimerNotificationService
    {
        private const string ActionKey = "action";
        private const string RepeatAction = "repeat";

        public static void ShowCompleted()
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("source", "timer")
                .AddText("Timer complete")
                .AddText("The countdown has finished.")
                .AddButton(new AppNotificationButton("Repeat")
                    .AddArgument(ActionKey, RepeatAction))
                .MuteAudio()
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }

        public static bool IsRepeatAction(IDictionary<string, string> arguments)
        {
            return arguments.TryGetValue(ActionKey, out string? action)
                && action == RepeatAction;
        }
    }
}
