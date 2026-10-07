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

            Config = AppConfig.Load();
            var palette = ThemeManager.Load();
            ApplyPalette(palette);

            Modules = new ModuleManager(Config);

            var window = new MainWindow();
            window.Show();
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
