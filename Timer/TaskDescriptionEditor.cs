using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Timer
{
    internal sealed class TaskDescriptionEditor
    {
        private const int SaveLimit = 120;

        private readonly Border _panel;
        private readonly TextBox _textBox;
        private readonly TextBlock _placeholder;
        private readonly TextBlock _counter;
        private readonly Button _saveButton;

        private bool _isFocused;
        private string _savedText = string.Empty;

        public string SavedText => _savedText;

        public TaskDescriptionEditor(
            Border panel,
            TextBox textBox,
            TextBlock placeholder,
            TextBlock counter,
            Button saveButton)
        {
            _panel = panel;
            _textBox = textBox;
            _placeholder = placeholder;
            _counter = counter;
            _saveButton = saveButton;

            _textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
            _textBox.GotFocus += TextBox_GotFocus;
            _textBox.LostFocus += TextBox_LostFocus;
            _textBox.TextChanged += TextBox_TextChanged;
            DataObject.AddPastingHandler(_textBox, TextBox_Pasting);

            UpdateState();
        }

        public void Toggle()
        {
            if (_panel.Visibility == Visibility.Visible)
            {
                Collapse();
                return;
            }

            _textBox.Text = _savedText;
            _isFocused = false;
            _panel.Visibility = Visibility.Visible;
            UpdateState();
        }

        public void Cancel()
        {
            _textBox.Text = _savedText;
            _isFocused = false;
            Keyboard.ClearFocus();
            Collapse();
        }

        public void Save()
        {
            if (_textBox.Text.Length > SaveLimit)
                return;

            _savedText = _textBox.Text;
            _isFocused = false;
            Keyboard.ClearFocus();
            Collapse();
        }

        public void Collapse()
        {
            _isFocused = false;
            _panel.Visibility = Visibility.Collapsed;
            UpdateState();
        }

        public void MarkFocusCleared()
        {
            if (ReferenceEquals(Keyboard.FocusedElement, _textBox))
            {
                _isFocused = false;
            }
        }

        public void UpdateStateLater()
        {
            _textBox.Dispatcher.BeginInvoke(UpdateState, DispatcherPriority.Background);
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isFocused = true;
            UpdateState();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _isFocused = true;
            UpdateState();
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _isFocused = false;
            UpdateStateLater();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateState();
        }

        private void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            string pasteFormat = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
                ? DataFormats.UnicodeText
                : DataFormats.Text;

            if (!e.DataObject.GetDataPresent(pasteFormat))
                return;

            if (e.DataObject.GetData(pasteFormat) is not string pastedText)
                return;

            int currentLengthWithoutSelection = _textBox.Text.Length - _textBox.SelectionLength;
            int allowedLength = _textBox.MaxLength - currentLengthWithoutSelection;
            if (allowedLength <= 0)
            {
                e.CancelCommand();
                return;
            }

            if (pastedText.Length <= allowedLength)
                return;

            e.DataObject = new DataObject(DataFormats.UnicodeText, pastedText[..allowedLength]);
            e.FormatToApply = DataFormats.UnicodeText;
        }

        private void UpdateState()
        {
            int length = _textBox.Text.Length;
            bool isOverLimit = length > SaveLimit;

            _counter.Text = $"{length} / {SaveLimit}";
            _counter.Foreground = (Brush)_counter.FindResource(isOverLimit ? "ErrorTextBrush" : "MutedTextBrush");
            _saveButton.IsEnabled = !isOverLimit;

            _placeholder.Visibility =
                length == 0 && !_isFocused
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
