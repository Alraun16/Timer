using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text.RegularExpressions;

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
                var taskStack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

                bool isFirstTask = true;
                foreach (var entry in group.OrderByDescending(entry => entry.FinishedAt))
                {
                    if (!isFirstTask)
                        taskStack.Children.Add(CreateTaskSeparator());

                    taskStack.Children.Add(CreateTaskRow(entry));
                    isFirstTask = false;
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
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            title.Children.Add(CreateCalendarIcon());
            title.Children.Add(new TextBlock
            {
                Text = dateText,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Foreground = GetBrush("PrimaryTextBrush"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(title);

            var totalBlock = new TextBlock
            {
                Text = totalText,
                Foreground = GetBrush("MutedTextBrush"),
                FontSize = 15,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(totalBlock, 1);
            header.Children.Add(totalBlock);

            var collapseButton = CreateCollapseButton(isExpanded, out var collapseDownImage, out var collapseUpImage);
            Grid.SetColumn(collapseButton, 2);
            header.Children.Add(collapseButton);

            taskStack.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            collapseButton.MouseLeftButtonUp += (_, _) =>
            {
                bool nextExpanded = taskStack.Visibility != Visibility.Visible;
                _expandedDays[day] = nextExpanded;
                taskStack.Visibility = nextExpanded ? Visibility.Visible : Visibility.Collapsed;
                collapseDownImage.Visibility = nextExpanded ? Visibility.Collapsed : Visibility.Visible;
                collapseUpImage.Visibility = nextExpanded ? Visibility.Visible : Visibility.Collapsed;
            };

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(taskStack);

            return new Border
            {
                Background = GetBrush("PanelBackgroundBrush"),
                BorderBrush = GetBrush("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 10, 8, 10),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Effect = (System.Windows.Media.Effects.Effect)_resourceOwner.FindResource("PanelShadowEffect"),
                Child = content
            };
        }

        private Grid CreateTaskRow(HistoryEntry entry)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var timeBlock = new Border
            {
                Background = GetBrush("LightBlockBackgroundBrush"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = entry.FinishedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Foreground = GetBrush("PrimaryTextBrush"),
                    FontSize = 12
                }
            };
            row.Children.Add(timeBlock);

            var description = new TextBlock
            {
                Text = FormatDescription(entry.Description),
                Foreground = GetBrush("PrimaryTextBrush"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(description, 1);
            row.Children.Add(description);

            var duration = new TextBlock
            {
                Text = FormatShortDuration(entry.Duration),
                Foreground = GetBrush("MutedTextBrush"),
                FontSize = 12.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(duration, 2);
            row.Children.Add(duration);

            return row;
        }

        private Border CreateTaskSeparator()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xC7, 0xC5, 0xC2), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x95, 0xC7, 0xC5, 0xC2), 0.22));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 0xC7, 0xC5, 0xC2), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x95, 0xC7, 0xC5, 0xC2), 0.78));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xC7, 0xC5, 0xC2), 1));
            brush.Freeze();

            var separator = new Border
            {
                Height = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = brush,
                Opacity = 0.55,
                Margin = new Thickness(0, 0, 0, 8)
            };
            separator.Loaded += (_, _) => UpdateSeparatorWidth(separator);
            separator.SizeChanged += (_, _) => UpdateSeparatorWidth(separator);
            return separator;
        }

        private static void UpdateSeparatorWidth(FrameworkElement separator)
        {
            if (separator.Parent is FrameworkElement parent && parent.ActualWidth > 0)
                separator.Width = parent.ActualWidth * 0.9;
        }

        private Viewbox CreateCalendarIcon()
        {
            var brush = GetSvgBrush("icon-calendar.svg");
            var canvas = new Canvas { Width = 24, Height = 24 };

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M18.013 5H5.98698C4.33732 5 3 6.29322 3 7.8885V18.1115C3 19.7068 4.33732 21 5.98698 21H18.013C19.6627 21 21 19.7068 21 18.1115V7.8885C21 6.29322 19.6627 5 18.013 5Z"),
                Stroke = brush,
                StrokeThickness = 2
            });
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M8 3L8 6"),
                Stroke = brush,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M16 3L16 6"),
                Stroke = brush,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M3 9L21 9"),
                Stroke = brush,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });

            return new Viewbox
            {
                Width = 20,
                Height = 20,
                Child = canvas,
                VerticalAlignment = VerticalAlignment.Center
            };
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
                return "Славно поработал";

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

        private Border CreateCollapseButton(bool isExpanded, out Image collapseDownImage, out Image collapseUpImage)
        {
            collapseDownImage = CreateCollapseImage(isRotated: false);
            collapseUpImage = CreateCollapseImage(isRotated: true);
            collapseDownImage.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
            collapseUpImage.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;

            var iconLayer = new Grid
            {
                Width = 14,
                Height = 14,
                ClipToBounds = false
            };
            iconLayer.Children.Add(collapseDownImage);
            iconLayer.Children.Add(collapseUpImage);

            return new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(8, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = iconLayer,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Image CreateCollapseImage(bool isRotated)
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Icons", "icon-collapse.svg");
            BitmapSource source = SvgIconRenderer.Render(path, 32);
            if (isRotated)
            {
                var rotated = new TransformedBitmap(source, new RotateTransform(180));
                rotated.Freeze();
                source = rotated;
            }

            return new Image
            {
                Source = source,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Brush GetSvgBrush(string fileName)
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Icons", fileName);
            if (!System.IO.File.Exists(path))
                return GetBrush("PrimaryTextBrush");

            string svg = System.IO.File.ReadAllText(path);
            var match = Regex.Match(svg, "(fill|stroke)\\s*=\\s*[\"'](?<color>#[0-9a-fA-F]{6,8})[\"']");
            if (!match.Success)
                return GetBrush("PrimaryTextBrush");

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(match.Groups["color"].Value));
            brush.Freeze();
            return brush;
        }
    }
}
