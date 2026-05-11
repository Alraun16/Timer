using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Timer
{
    internal sealed class HistoryPanel
    {
        private readonly HistoryService _historyService;
        private readonly StackPanel _historyStack;
        private readonly TextBlock _totalTextBlock;
        private readonly FrameworkElement _resourceOwner;
        private readonly Dictionary<DateTime, bool> _expandedDays = new();

        private bool _isOpenedOnce;

        public HistoryPanel(
            HistoryService historyService,
            StackPanel historyStack,
            TextBlock totalTextBlock,
            FrameworkElement resourceOwner)
        {
            _historyService = historyService;
            _historyStack = historyStack;
            _totalTextBlock = totalTextBlock;
            _resourceOwner = resourceOwner;
        }

        public void Refresh()
        {
            _historyStack.Children.Clear();

            var entries = _historyService.ReadEntries().ToList();
            TimeSpan fullTotal = TimeSpan.FromTicks(entries.Sum(entry => entry.Duration.Ticks));
            _totalTextBlock.Text = $"Всего: {FormatShortDuration(fullTotal)}";

            if (entries.Count == 0)
            {
                _historyStack.Children.Add(new TextBlock
                {
                    Text = "История пуста",
                    Foreground = GetBrush("MutedTextBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            DateTime today = DateTime.Today;
            foreach (var group in entries.GroupBy(entry => entry.FinishedAt.Date).OrderByDescending(group => group.Key))
            {
                TimeSpan total = TimeSpan.FromTicks(group.Sum(entry => entry.Duration.Ticks));
                var taskStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

                foreach (var entry in group.OrderByDescending(entry => entry.FinishedAt))
                {
                    taskStack.Children.Add(CreateTaskRow(entry));
                }

                _historyStack.Children.Add(CreateDayBlock(
                    group.Key,
                    FormatDate(group.Key, today),
                    FormatShortDuration(total),
                    taskStack,
                    GetDayExpanded(group.Key, today)));
            }
        }

        public void MarkOpened()
        {
            _isOpenedOnce = true;
        }

        private bool GetDayExpanded(DateTime day, DateTime today)
        {
            if (_expandedDays.TryGetValue(day, out bool isExpanded))
                return isExpanded;

            return !_isOpenedOnce && day == today;
        }

        private Border CreateDayBlock(
            DateTime day,
            string dateText,
            string totalText,
            StackPanel taskStack,
            bool isExpanded)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            header.Children.Add(new TextBlock
            {
                Text = dateText,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("PrimaryTextBrush")
            });

            var totalBlock = new TextBlock
            {
                Text = totalText,
                Foreground = GetBrush("MutedTextBrush"),
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(totalBlock, 1);
            header.Children.Add(totalBlock);

            var expander = new Expander
            {
                Header = header,
                Content = taskStack,
                IsExpanded = isExpanded,
                Foreground = GetBrush("PrimaryTextBrush"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            expander.Expanded += (_, _) => _expandedDays[day] = true;
            expander.Collapsed += (_, _) => _expandedDays[day] = false;

            return new Border
            {
                Background = GetBrush("AppBackgroundBrush"),
                BorderBrush = GetBrush("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = expander
            };
        }

        private Grid CreateTaskRow(HistoryEntry entry)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = entry.FinishedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                Foreground = GetBrush("MutedTextBrush"),
                Margin = new Thickness(0, 0, 8, 0)
            });

            var description = new TextBlock
            {
                Text = FormatDescription(entry.Description),
                Foreground = GetBrush("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(description, 1);
            row.Children.Add(description);

            var duration = new TextBlock
            {
                Text = FormatShortDuration(entry.Duration),
                Foreground = GetBrush("MutedTextBrush"),
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(duration, 2);
            row.Children.Add(duration);

            return row;
        }

        private Brush GetBrush(string resourceName)
        {
            return (Brush)_resourceOwner.FindResource(resourceName);
        }

        private static string FormatDate(DateTime date, DateTime today)
        {
            string dateText = $"{date.Day} {GetMonthName(date.Month)}";
            if (date.Date == today)
                return $"Сегодня, {dateText}";

            if (date.Date == today.AddDays(-1))
                return $"Вчера, {dateText}";

            return dateText;
        }

        private static string FormatShortDuration(TimeSpan duration)
        {
            int totalMinutes = (int)Math.Round(duration.TotalMinutes);
            if (duration > TimeSpan.Zero && totalMinutes == 0) totalMinutes = 1;

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            if (hours == 0)
                return $"{minutes} мин";

            if (minutes == 0)
                return $"{hours} ч";

            return $"{hours} ч {minutes} мин";
        }

        private static string FormatDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "Без описания";

            return description
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }

        private static string GetMonthName(int month)
        {
            return month switch
            {
                1 => "января",
                2 => "февраля",
                3 => "марта",
                4 => "апреля",
                5 => "мая",
                6 => "июня",
                7 => "июля",
                8 => "августа",
                9 => "сентября",
                10 => "октября",
                11 => "ноября",
                12 => "декабря",
                _ => string.Empty
            };
        }
    }
}
