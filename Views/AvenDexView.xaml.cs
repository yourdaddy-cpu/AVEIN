using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AVEIN.Views
{
    public partial class AvenDexView : UserControl
    {
        private readonly LocalAiModule _ai = new LocalAiModule();
        private AvenDexModule _dex;
        private string _projectRoot;
        private string _sessionId;

        public AvenDexView()
        {
            InitializeComponent();

            AvenDexModule.ActivityLogged += msg => { /* optional: log to a panel later */ };
        }

        private void BrowseProject_Click(object sender, RoutedEventArgs e)
        {
            // A folder picker on .NET 8 WPF uses OpenFolderDialog
            var dialog = new OpenFolderDialog
            {
                Title = "Choose the project folder for Aven Dex"
            };

            bool? result = dialog.ShowDialog();
            if (result != true) return;

            _projectRoot = dialog.FolderName;
            ProjectPathBox.Text = _projectRoot;
            _dex = new AvenDexModule(_ai, _projectRoot);

            AddSystemMessage($"Project set to: {_projectRoot}");
        }

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
            _sessionId = null;
            MessageList.Children.Clear();
            if (!string.IsNullOrEmpty(_projectRoot))
                AddSystemMessage($"Project: {_projectRoot}");
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SendMessage();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e) => SendMessage();

        private async void SendMessage()
        {
            var text = InputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (_dex == null || string.IsNullOrEmpty(_projectRoot))
            {
                AddSystemMessage("Pick a project folder first (click Browse above).");
                return;
            }

            if (_sessionId == null)
                _sessionId = Guid.NewGuid().ToString("N");

            AddUserMessage(text);
            InputBox.Clear();

            var placeholder = AddSystemMessage("Aven Dex is thinking...");

            string reply;
            try
            {
                reply = await _dex.ExecuteCodeTaskAsync(text);
            }
            catch (Exception ex)
            {
                reply = "(error: " + ex.Message + ")";
            }

            MessageList.Children.Remove(placeholder);
            AddAssistantMessage(reply);
        }

        private TextBlock AddSystemMessage(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            MessageList.Children.Add(tb);
            return tb;
        }

        private void AddUserMessage(string text)
        {
            MessageList.Children.Add(new TextBlock
            {
                Text = "You: " + text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            });
        }

        private void AddAssistantMessage(string text)
        {
            MessageList.Children.Add(new TextBlock
            {
                Text = "Aven Dex: " + text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            });
        }
    }
}
