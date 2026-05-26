using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private const int DescriptionSaveLimit = 120;

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
                FontSize = 13,
                Foreground = GetBrush("PrimaryTextBrush"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(title);

            var totalBlock = new TextBlock
            {
                Text = totalText,
                Foreground = GetBrush("MutedTextBrush"),
                FontSize = 13,
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
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8),
                Background = Brushes.Transparent
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.MouseRightButtonUp += (_, e) =>
            {
                ShowTaskActionMenu(row, entry, e.GetPosition(row));
                e.Handled = true;
            };

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

            var description = CreateDescriptionContent(entry);
            Grid.SetColumn(description, 1);
            row.Children.Add(description);

            var duration = new TextBlock
            {
                Text = FormatShortDuration(entry.Duration),
                Foreground = GetBrush("MutedTextBrush"),
                FontSize = 11.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(duration, 2);
            row.Children.Add(duration);

            return row;
        }

        private void ShowTaskActionMenu(FrameworkElement row, HistoryEntry entry, Point position)
        {
            var popup = new Popup
            {
                PlacementTarget = row,
                Placement = PlacementMode.Relative,
                HorizontalOffset = position.X,
                VerticalOffset = position.Y,
                AllowsTransparency = true,
                StaysOpen = false
            };

            var editItem = CreateMenuAction("Редактировать запись");
            editItem.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                BeginEdit(row, entry);
            };

            var deleteItem = CreateMenuAction("Удалить запись");
            deleteItem.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                ShowDeleteConfirmation(row, entry);
            };

            popup.Child = new Border
            {
                Background = GetPopupBackgroundBrush(),
                BorderBrush = GetBrush("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0, 4, 0, 4),
                Child = new StackPanel
                {
                    Children =
                    {
                        editItem,
                        CreateMenuSeparator(),
                        deleteItem
                    }
                }
            };

            popup.IsOpen = true;
        }

        private Border CreateMenuAction(string text)
        {
            var background = GetPopupBackgroundBrush();
            var hoverBackground = GetPopupHoverBackgroundBrush();
            var item = new Border
            {
                Background = background,
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = GetBrush("PrimaryTextBrush"),
                    FontSize = 12
                }
            };
            item.MouseEnter += (_, _) => item.Background = hoverBackground;
            item.MouseLeave += (_, _) => item.Background = background;
            return item;
        }

        private Border CreateMenuSeparator()
        {
            var separator = CreateTaskSeparator();
            separator.Margin = new Thickness(0, 1, 0, 1);
            return new Border
            {
                Background = GetOpaquePopupBackgroundBrush(),
                Child = separator
            };
        }

        private void BeginEdit(FrameworkElement row, HistoryEntry entry)
        {
            if (row.Parent is not Panel parent)
                return;

            int rowIndex = parent.Children.IndexOf(row);
            if (rowIndex < 0)
                return;

            row.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (row.Parent is not Panel currentParent)
                    return;

                int currentRowIndex = currentParent.Children.IndexOf(row);
                if (currentRowIndex < 0)
                    return;

                currentParent.Children.RemoveAt(currentRowIndex);
                currentParent.Children.Insert(currentRowIndex, CreateEditTaskRow(entry));
            }));
        }

        private Border CreateEditTaskRow(HistoryEntry entry)
        {
            var finishedAtBox = CreateHistoryInput(
                entry.FinishedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                48,
                12);
            finishedAtBox.Padding = new Thickness(3, 1, 1, 1);
            AttachTimeMask(finishedAtBox);
            
            var durationBox = CreateHistoryInput(FormatFullDuration(entry.Duration), 80, 12);
            durationBox.Padding = new Thickness(3, 1, 1, 1);
            AttachDurationMask(durationBox);

            var descriptionBox = CreateHistoryInput(entry.Description, double.NaN, 12);
            descriptionBox.Height = double.NaN;
            descriptionBox.MinHeight = GetDescriptionInputHeight(entry.Description);
            descriptionBox.Padding = new Thickness(4, 4, 4, 4);
            descriptionBox.Margin = new Thickness(-5, 0, -5, 0);
            descriptionBox.TextWrapping = TextWrapping.Wrap;
            descriptionBox.AcceptsReturn = true;
            descriptionBox.MaxLength = 1000;
            descriptionBox.VerticalContentAlignment = VerticalAlignment.Top;
            descriptionBox.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            var cancelButton = CreatePopupButton("Отменить");
            cancelButton.Click += (_, _) => Refresh();

            var saveButton = CreatePopupButton("Сохранить");
            saveButton.MinWidth += 2;

            var descriptionCounter = new TextBlock
            {
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            void UpdateDescriptionState()
            {
                descriptionBox.MinHeight = GetDescriptionInputHeight(descriptionBox.Text);

                int length = descriptionBox.Text.Length;
                bool isOverLimit = length > DescriptionSaveLimit;
                descriptionCounter.Text = $"{length} / {DescriptionSaveLimit}";
                descriptionCounter.Foreground = GetBrush(isOverLimit ? "ErrorTextBrush" : "MutedTextBrush");
                saveButton.IsEnabled = !isOverLimit;
            }

            descriptionBox.TextChanged += (_, _) => UpdateDescriptionState();
            UpdateDescriptionState();

            saveButton.Click += (_, _) =>
            {
                if (!TryParseFinishedAt(entry.FinishedAt.Date, finishedAtBox.Text, out DateTime finishedAt))
                    return;

                if (!TryParseDuration(durationBox.Text, out TimeSpan duration))
                    return;

                _historyService.UpdateEntry(entry.Index, finishedAt, duration, descriptionBox.Text);
                Refresh();
            };

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var descriptionLabel = new TextBlock
            {
                Text = "Краткое описание",
                Foreground = GetBrush("MutedTextBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(descriptionLabel);

            var timeFields = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children =
                {
                    finishedAtBox,
                    durationBox
                }
            };
            finishedAtBox.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(timeFields, 1);
            header.Children.Add(timeFields);

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(descriptionBox);

            var footer = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0),
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var actionButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    cancelButton,
                    saveButton
                }
            };
            Grid.SetColumn(actionButtons, 1);
            footer.Children.Add(actionButtons);

            descriptionCounter.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(descriptionCounter, 2);
            footer.Children.Add(descriptionCounter);
            content.Children.Add(footer);

            return new Border
            {
                Background = GetBrush("PanelBackgroundBrush"),
                BorderBrush = GetBrush("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = content
            };
        }

        private TextBox CreateHistoryInput(string text, double width, double fontSize)
        {
            var textBox = new TextBox
            {
                Text = text,
                Style = (Style)_resourceOwner.FindResource("HistoryInlineTextBox"),
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Top
            };

            if (!double.IsNaN(width))
                textBox.Width = width;

            return textBox;
        }

        private static void AttachTimeMask(TextBox textBox)
        {
            AttachDigitMask(
                textBox,
                new[] { 0, 1, 3, 4 },
                NormalizeTimeMask);
        }

        private static void AttachDurationMask(TextBox textBox)
        {
            AttachDigitMask(
                textBox,
                new[] { 0, 1, 5, 6 },
                NormalizeDurationMask);
        }

        private static void AttachDigitMask(
            TextBox textBox,
            int[] digitIndexes,
            Func<string, string> normalize)
        {
            textBox.Text = normalize(textBox.Text);
            textBox.PreviewTextInput += (_, e) =>
            {
                e.Handled = true;
                if (e.Text.Length != 1 || !char.IsDigit(e.Text[0]))
                    return;

                ReplaceMaskDigit(textBox, e.Text[0], digitIndexes, normalize);
            };

            textBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Delete)
                {
                    e.Handled = true;
                    ClearMaskDigit(textBox, e.Key == System.Windows.Input.Key.Back ? -1 : 0, digitIndexes, normalize);
                    return;
                }

                if (e.Key == System.Windows.Input.Key.Space)
                    e.Handled = true;
            };

            DataObject.AddPastingHandler(textBox, (_, e) => e.CancelCommand());
            textBox.LostKeyboardFocus += (_, _) => textBox.Text = normalize(textBox.Text);
        }

        private static string NormalizeTimeMask(string text)
        {
            (int hours, int minutes) = GetLimitedHoursAndMinutes(text);
            return $"{hours:D2}:{minutes:D2}";
        }

        private static string NormalizeDurationMask(string text)
        {
            (int hours, int minutes) = GetLimitedHoursAndMinutes(text);
            return $"{hours:D2} ч {minutes:D2} мин";
        }

        private static (int Hours, int Minutes) GetLimitedHoursAndMinutes(string text)
        {
            string digits = new string(text.Where(char.IsDigit).Take(4).ToArray()).PadRight(4, '0');
            int hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
            int minutes = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
            return (Math.Min(hours, 24), Math.Min(minutes, 60));
        }

        private static void ReplaceMaskDigit(
            TextBox textBox,
            char digit,
            int[] digitIndexes,
            Func<string, string> normalize)
        {
            int index = GetMaskDigitIndex(textBox.SelectionStart, digitIndexes);
            if (index < 0)
                return;

            char[] chars = normalize(textBox.Text).ToCharArray();
            chars[index] = digit;

            string nextText = new string(chars);
            int nextPosition = GetNextMaskPosition(index, digitIndexes, nextText.Length);
            textBox.Text = ShouldNormalizeMaskSegment(index, digitIndexes)
                ? normalize(nextText)
                : nextText;
            textBox.SelectionStart = Math.Min(nextPosition, textBox.Text.Length);
        }

        private static void ClearMaskDigit(
            TextBox textBox,
            int offset,
            int[] digitIndexes,
            Func<string, string> normalize)
        {
            int index = GetMaskDigitIndex(textBox.SelectionStart + offset, digitIndexes);
            if (index < 0)
                return;

            char[] chars = normalize(textBox.Text).ToCharArray();
            chars[index] = '0';
            textBox.Text = new string(chars);
            textBox.SelectionStart = index;
        }

        private static int GetMaskDigitIndex(int selectionStart, int[] digitIndexes)
        {
            foreach (int digitIndex in digitIndexes)
            {
                if (selectionStart <= digitIndex)
                    return digitIndex;
            }

            return digitIndexes[^1];
        }

        private static int GetNextMaskPosition(int index, int[] digitIndexes, int textLength)
        {
            for (int i = 0; i < digitIndexes.Length; i++)
            {
                if (digitIndexes[i] == index)
                    return i + 1 < digitIndexes.Length ? digitIndexes[i + 1] : textLength;
            }

            return textLength;
        }

        private static bool ShouldNormalizeMaskSegment(int index, int[] digitIndexes)
        {
            return digitIndexes.Length >= 4
                && (index == digitIndexes[1] || index == digitIndexes[3]);
        }

        private FrameworkElement CreateDescriptionContent(HistoryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Description))
            {
                string path = System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "Icons",
                    "Emojis",
                    GetEmptyDescriptionEmojiFileName(entry));

                return new Image
                {
                    Source = SvgIconRenderer.Render(path, 48),
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };
            }

            return new TextBlock
            {
                Text = FormatDescription(entry.Description),
                Foreground = GetBrush("PrimaryTextBrush"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        private static string GetEmptyDescriptionEmojiFileName(HistoryEntry entry)
        {
            long seed = entry.FinishedAt.Ticks ^ entry.Duration.Ticks ^ (entry.Index * 397L);
            int emojiIndex = (int)(((seed % 6) + 6) % 6) + 1;
            return $"emoji_{emojiIndex}.svg";
        }

        private static double GetDescriptionInputHeight(string text)
        {
            int lineCount = Math.Max(1, text.Split('\n').Length);
            return Math.Max(48, 24 + lineCount * 18);
        }

        private void ShowDeleteConfirmation(FrameworkElement row, HistoryEntry entry)
        {
            var popup = new Popup
            {
                PlacementTarget = row,
                Placement = PlacementMode.Center,
                AllowsTransparency = true,
                StaysOpen = false
            };

            var cancelButton = CreatePopupButton("Отменить");
            cancelButton.Click += (_, _) => popup.IsOpen = false;

            var deleteButton = CreatePopupButton("Удалить");
            deleteButton.Click += (_, _) =>
            {
                popup.IsOpen = false;
                _historyService.DeleteEntry(entry.Index);
                Refresh();
            };

            popup.Child = new Border
            {
                Background = GetOpaquePopupBackgroundBrush(),
                BorderBrush = GetBrush("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Effect = (System.Windows.Media.Effects.Effect)_resourceOwner.FindResource("PanelShadowEffect"),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Удалить задачу?",
                            Foreground = GetBrush("PrimaryTextBrush"),
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                cancelButton,
                                deleteButton
                            }
                        }
                    }
                }
            };

            popup.IsOpen = true;
        }

        private Button CreatePopupButton(string text)
        {
            return new Button
            {
                Content = text,
                Style = (Style)_resourceOwner.FindResource("TextButton"),
                MinWidth = 58,
                Margin = new Thickness(4, 0, 4, 0)
            };
        }

        private static bool TryParseFinishedAt(DateTime date, string text, out DateTime finishedAt)
        {
            finishedAt = default;
            string digits = new string(text.Where(char.IsDigit).Take(4).ToArray());
            if (digits.Length != 4)
                return false;

            int hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
            int minutes = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
            if (hours > 24 || minutes > 60)
                return false;

            finishedAt = date.Date + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
            return true;
        }

        private static bool TryParseDuration(string text, out TimeSpan duration)
        {
            duration = default;
            string value = text.Trim();
            if (value.Length == 0)
                return false;

            if (value.Contains(':'))
            {
                string[] parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int hours)
                    && int.TryParse(parts[1], out int minutes))
                {
                    if (hours > 24 || minutes > 60)
                        return false;

                    duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
                    return duration >= TimeSpan.Zero;
                }

                if (parts.Length == 3
                    && int.TryParse(parts[0], out hours)
                    && int.TryParse(parts[1], out minutes)
                    && int.TryParse(parts[2], out int seconds))
                {
                    if (hours > 24 || minutes > 60)
                        return false;

                    duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                    return duration >= TimeSpan.Zero;
                }
            }

            var numbers = Regex.Matches(value, @"\d+")
                .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
                .ToList();
            if (numbers.Count == 0)
                return false;

            bool hasHours = value.Contains('ч', StringComparison.OrdinalIgnoreCase);
            if (hasHours)
            {
                int hours = numbers[0];
                int minutes = numbers.Count > 1 ? numbers[1] : 0;
                if (hours > 24 || minutes > 60)
                    return false;

                duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
                return true;
            }

            if (numbers.Count == 1)
            {
                if (numbers[0] > 60)
                    return false;

                duration = TimeSpan.FromMinutes(numbers[0]);
                return true;
            }

            if (numbers[0] > 24 || numbers[1] > 60)
                return false;

            duration = TimeSpan.FromHours(numbers[0]) + TimeSpan.FromMinutes(numbers[1]);
            return true;
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

        private static Brush GetPopupBackgroundBrush()
        {
            return new SolidColorBrush(Color.FromArgb(0xCC, 0x18, 0x19, 0x1B));
        }

        private static Brush GetOpaquePopupBackgroundBrush()
        {
            return new SolidColorBrush(Color.FromArgb(0xFF, 0x18, 0x19, 0x1B));
        }

        private static Brush GetPopupHoverBackgroundBrush()
        {
            return new SolidColorBrush(Color.FromArgb(0xE0, 0x0C, 0x0D, 0x0F));
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

        private static string FormatFullDuration(TimeSpan duration)
        {
            int totalMinutes = (int)Math.Round(duration.TotalMinutes);
            if (duration > TimeSpan.Zero && totalMinutes == 0) totalMinutes = 1;

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:D2} ч {minutes:D2} мин";
        }

        private static string FormatDescription(string description)
        {
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
