using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AVEIN.Views
{
    public partial class PluginsView : UserControl
    {
        public PluginsView()
        {
            InitializeComponent();

            // Wire up the activity log sources
            WebSearchModule.ActivityLogged += AppendActivity;
            FileToolModule.ActivityLogged += AppendActivity;

            // Reflect current enabled state
            WebSearchToggle.IsChecked = App.Config.IsModuleEnabled("web-search");

            Loaded += (s, e) =>
            {
                AppendActivity("Plugins view ready.");
            };
        }

        private void WebSearchToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Config == null) return;

            var enabled = WebSearchToggle.IsChecked == true;
            App.Config.SetModuleEnabled("web-search", enabled);
            AppendActivity("Web search " + (enabled ? "enabled" : "disabled"));
        }

        private void AppendActivity(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendActivity(message));
                return;
            }

            ActivityLog.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            });

            // Keep the log from growing forever
            while (ActivityLog.Children.Count > 200)
                ActivityLog.Children.RemoveAt(0);

            if (ActivityLog.Parent is ScrollViewer sv)
                sv.ScrollToEnd();
        }
    }
}
