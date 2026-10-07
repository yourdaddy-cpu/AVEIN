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

        public AvenDexView()
        {
            InitializeComponent();
        }

        private void BrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Choose the project folder for Aven Dex" };
            bool? result = dialog.ShowDialog();
            if (result != true) return;

            _projectRoot = dialog.FolderName;
            ProjectPathBox.Text = _projectRoot;

            if (_dex != null)
                _dex.StepReport -= OnStepReport;

            _dex = new AvenDexModule(_ai, _projectRoot);
            _dex.StepReport += OnStepReport;
            ApplyPermissionMode();

            AddSystemMessage($"Project set to: {_projectRoot}");
        }

        private void PermissionMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyPermissionMode();
        }

        private void ApplyPermissionMode()
        {
            if (_dex == null) return;

            switch (PermissionModeBox.SelectedIndex)
            {
                case 0: _dex.PermissionMode = DexPermissionMode.Default; break;
                case 1: _dex.PermissionMode = DexPermissionMode.AcceptEdits; break;
                case 2: _dex.PermissionMode = DexPermissionMode.Plan; break;
                case 3: _dex.PermissionMode = DexPermissionMode.Auto; break;
            }

            AddSystemMessage($"Permission mode: {_dex.PermissionMode}");
        }

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
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

            AddUserMessage(text);
            InputBox.Clear();

            var typing = AddSystemMessage("agent starting...");

            string final;
            try
            {
                final = await _dex.ExecuteCodeTaskAsync(text);
            }
            catch (Exception ex)
            {
                final = "(error: " + ex.Message + ")";
            }

            MessageList.Children.Remove(typing);
            AddAssistantMessage(final);
            ScrollToBottom();
        }

        private void OnStepReport(string msg)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnStepReport(msg));
                return;
            }

            AddStepLine(msg);
            ScrollToBottom();
        }

        private void AddStepLine(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            MessageList.Children.Add(tb);
        }

        private TextBlock AddSystemMessage(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 6),
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
                Margin = new Thickness(0, 8, 0, 6),
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
                Margin = new Thickness(0, 8, 0, 12),
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            });
        }

        private void ScrollToBottom()
        {
            if (MessageList.Parent is ScrollViewer sv)
                sv.ScrollToEnd();
        }
    }
}
