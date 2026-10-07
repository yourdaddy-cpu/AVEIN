using System.Windows;
using System.Windows.Controls;
using AVEIN.Views;

namespace AVEIN
{
    public partial class MainWindow : Window
    {
        private readonly ChatView _chatView = new ChatView();
        private readonly AvenDexView _dexView = new AvenDexView();
        private readonly MemoryView _memoryView = new MemoryView();
        private readonly SettingsView _settingsView = new SettingsView();
        private readonly PluginsView _pluginsView = new PluginsView();
        private readonly AboutView _aboutView = new AboutView();

        public MainWindow()
        {
            InitializeComponent();

            var window = App.Config.Window;
            Width = window.Width;
            Height = window.Height;
            if (window.Maximized) WindowState = WindowState.Maximized;

            _memoryView.OpenSessionRequested += sessionId =>
            {
                _chatView.OpenSession(sessionId);
                ShowView(_chatView, NavChatButton);
            };

            ShowView(_chatView, NavChatButton);

            Closing += MainWindow_Closing;
        }

        private void NavChatButton_Click(object sender, RoutedEventArgs e) => ShowView(_chatView, NavChatButton);
        private void NavDexButton_Click(object sender, RoutedEventArgs e) => ShowView(_dexView, NavDexButton);
        private void NavMemoryButton_Click(object sender, RoutedEventArgs e) => ShowView(_memoryView, NavMemoryButton);
        private void NavPluginsButton_Click(object sender, RoutedEventArgs e) => ShowView(_pluginsView, NavPluginsButton);
        private void NavSettingsButton_Click(object sender, RoutedEventArgs e) => ShowView(_settingsView, NavSettingsButton);
        private void NavAboutButton_Click(object sender, RoutedEventArgs e) => ShowView(_aboutView, NavAboutButton);

        private void ShowView(UserControl view, Button activeButton)
        {
            MainContent.Content = view;

            // Reset all styles
            NavChatButton.Style = (Style)FindResource("TopNavButtonStyle");
            NavDexButton.Style = (Style)FindResource("TopNavButtonStyle");
            NavMemoryButton.Style = (Style)FindResource("NavButtonStyle");
            NavPluginsButton.Style = (Style)FindResource("NavButtonStyle");
            NavSettingsButton.Style = (Style)FindResource("NavButtonStyle");
            NavAboutButton.Style = (Style)FindResource("NavButtonStyle");

            // Apply active style
            if (activeButton == NavChatButton || activeButton == NavDexButton)
                activeButton.Style = (Style)FindResource("TopNavButtonActiveStyle");
            else
                activeButton.Style = (Style)FindResource("NavButtonActiveStyle");
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            App.Config.Window.Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                App.Config.Window.Width = Width;
                App.Config.Window.Height = Height;
            }
            App.Config.Save();
            App.Modules.ShutdownAll();
        }
    }
}
