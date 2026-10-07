using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AVEIN.Views
{
    public partial class PluginsView : UserControl
    {
        private static string TokenPath =>
            Path.Combine(AppContext.BaseDirectory, "config", "github.token.json");

        public PluginsView()
        {
            InitializeComponent();

            WebSearchModule.ActivityLogged += AppendActivity;
            FileToolModule.ActivityLogged += AppendActivity;
            GitHubModule.ActivityLogged += AppendActivity;
            AvenDexModule.ActivityLogged += AppendActivity;

            WebSearchToggle.IsChecked = App.Config.IsModuleEnabled("web-search");

            Loaded += (s, e) =>
            {
                AppendActivity("Plugins view ready.");
                LoadStoredToken();
            };
        }

        private void WebSearchToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Config == null) return;

            var enabled = WebSearchToggle.IsChecked == true;
            App.Config.SetModuleEnabled("web-search", enabled);
            AppendActivity("Web search " + (enabled ? "enabled" : "disabled"));
        }

        private void SaveGitHubToken_Click(object sender, RoutedEventArgs e)
        {
            var token = GitHubTokenBox.Password?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                GitHubStatusText.Text = "No token entered.";
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(TokenPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = new JObject { ["token"] = token }.ToString(Formatting.Indented);
                File.WriteAllText(TokenPath, json);

                GitHubStatusText.Text = "Token saved. Testing connection...";
                AppendActivity("Saved GitHub token.");

                // Test it
                var testModule = new GitHubModule(token);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await testModule.TestConnectionAsync();
                    Dispatcher.Invoke(() =>
                    {
                        GitHubStatusText.Text = ok
                            ? "✓ Connected successfully."
                            : "✗ Connection failed. Check the token and its 'repo' scope.";
                    });
                });
            }
            catch (Exception ex)
            {
                GitHubStatusText.Text = "Error: " + ex.Message;
                AppendActivity("Token save failed: " + ex.Message);
            }
        }

        private void LoadStoredToken()
        {
            try
            {
                if (!File.Exists(TokenPath))
                {
                    GitHubStatusText.Text = "Not connected.";
                    return;
                }

                var json = File.ReadAllText(TokenPath);
                var obj = JObject.Parse(json);
                var token = obj["token"]?.ToString();

                if (string.IsNullOrEmpty(token))
                {
                    GitHubStatusText.Text = "Not connected.";
                    return;
                }

                GitHubTokenBox.Password = token;
                GitHubStatusText.Text = "Token loaded. Press Save to test.";

                var testModule = new GitHubModule(token);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await testModule.TestConnectionAsync();
                    Dispatcher.Invoke(() =>
                    {
                        GitHubStatusText.Text = ok
                            ? "✓ Connected successfully."
                            : "✗ Stored token appears invalid.";
                    });
                });
            }
            catch
            {
                GitHubStatusText.Text = "Not connected.";
            }
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

            while (ActivityLog.Children.Count > 200)
                ActivityLog.Children.RemoveAt(0);

            if (ActivityLog.Parent is ScrollViewer sv)
                sv.ScrollToEnd();
        }
    }
}
