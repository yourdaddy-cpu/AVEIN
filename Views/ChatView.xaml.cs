using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace AVEIN.Views
{
    public partial class ChatView : UserControl
    {
        private readonly AVEIN.LocalAiModule _ai = new AVEIN.LocalAiModule();
        private string _currentSessionId;

        public ChatView()
        {
            InitializeComponent();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e) => SendMessage();

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SendMessage();
        }

        private async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            MicButton.Content = "Listening...";
            MicButton.IsEnabled = false;
            var heard = await AVEIN.VoiceModule.ListenOnceAsync();
            MicButton.Content = "Mic";
            MicButton.IsEnabled = true;
            if (!string.IsNullOrWhiteSpace(heard)) InputBox.Text = heard;
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSessionId = null;
            MessageList.Children.Clear();
        }

        public void OpenSession(string sessionId)
        {
            var session = AVEIN.HistoryStore.GetSession(sessionId);
            if (session == null) return;
            _currentSessionId = sessionId;
            MessageList.Children.Clear();
            foreach (var msg in session.Messages)
                AddMessage(msg.Sender, msg.Text);
        }

        private string CurrentModelName => "Phi4-mini";

        private async void SendMessage()
        {
            var text = InputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (_currentSessionId == null)
            {
                var session = AVEIN.HistoryStore.CreateSession();
                _currentSessionId = session.Id;
            }

            AddMessage("You", text);
            AVEIN.HistoryStore.AppendMessage(_currentSessionId, "You", text);
            InputBox.Clear();

            var modelName = CurrentModelName;
            var deepThink = DeepThinkToggle.IsChecked == true;
            var placeholder = AddPlaceholder(modelName, deepThink ? "thinking carefully..." : "thinking...");

            string reply;
            try
            {
                reply = await _ai.AskAsync(text, modelName, deepThink);
            }
            catch (Exception ex)
            {
                reply = BuildFullError(ex);
            }

            MessageList.Children.Remove(placeholder);
            AddMessage(modelName, reply);
            AVEIN.HistoryStore.AppendMessage(_currentSessionId, modelName, reply);

            if (SpeakToggle.IsChecked == true)
            {
                var spoken = StripAllTags(reply);
                spoken = Regex.Replace(spoken, @"\[used live web search\]|\[no web search result used.*?\]", "");
                AVEIN.VoiceModule.Speak(spoken.Trim());
            }
        }

        private string BuildFullError(Exception ex)
        {
            var msg = "(error: " + (ex.Message ?? "(no message)") + ")";
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 3)
            {
                msg += "\n  → " + (inner.GetType().Name) + ": " + (inner.Message ?? "(no message)");
                inner = inner.InnerException;
                depth++;
            }
            return msg;
        }

        private TextBlock AddPlaceholder(string sender, string text)
        {
            var tb = new TextBlock
            {
                Text = $"{sender}: {text}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            MessageList.Children.Add(tb);
            return tb;
        }

        private void AddMessage(string sender, string text)
        {
            string reasoning = null;
            string answer = null;

            var thinkMatch = Regex.Match(text, @"<thinking>(.*?)(?:</thinking>|$)", RegexOptions.Singleline);
            var ansMatch = Regex.Match(text, @"<answer>(.*?)(?:</answer>|$)", RegexOptions.Singleline);

            if (thinkMatch.Success) reasoning = thinkMatch.Groups[1].Value.Trim();
            if (ansMatch.Success) answer = ansMatch.Groups[1].Value.Trim();

            if (answer == null && reasoning == null)
            {
                AddPlainMessage(sender, StripAllTags(text));
                return;
            }

            if (!string.IsNullOrWhiteSpace(reasoning))
            {
                MessageList.Children.Add(new TextBlock
                {
                    Text = $"{sender} (reasoning): {reasoning}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                    FontStyle = FontStyles.Italic,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                });
            }

            var final = !string.IsNullOrWhiteSpace(answer) ? answer : StripAllTags(text);
            AddPlainMessage(sender, final);
        }

        private void AddPlainMessage(string sender, string text)
        {
            MessageList.Children.Add(new TextBlock
            {
                Text = $"{sender}: {text}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            });
        }

        private static string StripAllTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Regex.Replace(text, @"</?thinking>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</?answer>", "", RegexOptions.IgnoreCase);
            return text.Trim();
        }
    }
}
