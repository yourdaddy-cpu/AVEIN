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

            if (!string.IsNullOrWhiteSpace(heard))
                InputBox.Text = heard;
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

        private string CurrentModelName
        {
            get
            {
                var selected = ModelSelector?.SelectedItem as ComboBoxItem;
                return selected?.Content?.ToString() ?? "Phi4-mini";
            }
        }

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
            AddMessage(modelName, deepThink ? "thinking carefully..." : "thinking...");

            string reply;
            try
            {
                reply = await _ai.AskAsync(text, modelName, deepThink);
            }
            catch (System.Exception ex)
            {
                reply = "(error: " + ex.Message + ")";
            }

            MessageList.Children.RemoveAt(MessageList.Children.Count - 1);
            AddMessage(modelName, reply);
            AVEIN.HistoryStore.AppendMessage(_currentSessionId, modelName, reply);

            if (SpeakToggle.IsChecked == true)
            {
                var spoken = Regex.Replace(reply, @"</?(thinking|answer)>", "");
                spoken = Regex.Replace(spoken, @"\[used live web search\]|\[no web search result used.*?\]", "");
                AVEIN.VoiceModule.Speak(spoken.Trim());
            }
        }

        private void AddMessage(string sender, string text)
        {
            var thinkingMatch = Regex.Match(text, @"<thinking>(.*?)</?thinking>\s*<answer>(.*?)</?answer>", RegexOptions.Singleline);

            if (thinkingMatch.Success)
            {
                var reasoning = thinkingMatch.Groups[1].Value.Trim();
                var answer = thinkingMatch.Groups[2].Value.Trim();

                MessageList.Children.Add(new TextBlock
                {
                    Text = $"{sender} (reasoning): {reasoning}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                    FontStyle = FontStyles.Italic,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                });

                MessageList.Children.Add(new TextBlock
                {
                    Text = $"{sender}: {answer}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                });
                return;
            }

            var fallbackText = Regex.Replace(text, @"</?(thinking|answer)>", "").Trim();
            MessageList.Children.Add(new TextBlock
            {
                Text = $"{sender}: {fallbackText}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            });
        }
    }
}
