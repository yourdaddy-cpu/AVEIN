using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AVEIN
{
    public partial class App : Application
    {
        public static AppConfig Config { get; private set; }
        public static ModuleManager Modules { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, ev) =>
            {
                LogCrash("UI", ev.Exception);
                ev.Handled = true;
                MessageBox.Show(ev.Exception.Message + "\n\nDetails: data\\crash.log", "AVEIN error");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ev) => LogCrash("Fatal", ev.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, ev) => { LogCrash("Task", ev.Exception); ev.SetObserved(); };

            Config = AppConfig.Load();

            var palette = ThemeManager.Load();
            ApplyPalette(palette);

            Modules = new ModuleManager(Config);

            var window = new MainWindow();
            window.Show();
        }

        private static void LogCrash(string kind, Exception ex)
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {ex}\n\n");
            }
            catch { }
        }

        public static void ApplyPalette(ThemePalette palette)
        {
            var app = Current;
            SetBrush(app, "BackgroundBrush", palette.Background);
            SetBrush(app, "SurfaceBrush", palette.Surface);
            SetBrush(app, "PrimaryTextBrush", palette.PrimaryText);
            SetBrush(app, "SecondaryTextBrush", palette.SecondaryText);
            SetBrush(app, "AccentBrush", palette.Accent);
            SetBrush(app, "BorderBrush", palette.Border);
            SetBrush(app, "SidebarBackgroundBrush", palette.SidebarBackground);
            ThemeManager.Save(palette);
        }

        private static void SetBrush(Application app, string resourceKey, string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                app.Resources[resourceKey] = new SolidColorBrush(color);
            }
            catch { }
        }
    }
}
