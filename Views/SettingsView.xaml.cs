using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AVEIN.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += (s, e) => BuildThemeSwatches();
        }

        private void BuildThemeSwatches()
        {
            ThemeList.Items.Clear();

            foreach (var palette in AVEIN.ThemePresets.All)
            {
                var swatch = new Border
                {
                    Width = 130,
                    Height = 70,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 12, 12),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    BorderThickness = new Thickness(2),
                    BorderBrush = (Brush)FindResource("BorderBrush")
                };

                try
                {
                    var accentColor = (Color)ColorConverter.ConvertFromString(palette.Accent);
                    var bgColor = (Color)ColorConverter.ConvertFromString(palette.Background);
                    swatch.Background = new SolidColorBrush(bgColor);

                    var stack = new StackPanel { Margin = new Thickness(10) };
                    stack.Children.Add(new Border
                    {
                        Width = 20, Height = 20,
                        CornerRadius = new CornerRadius(10),
                        Background = new SolidColorBrush(accentColor),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = palette.Name,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.PrimaryText))
                    });
                    swatch.Child = stack;
                }
                catch { }

                swatch.MouseLeftButtonUp += (s, e) => AVEIN.App.ApplyPalette(palette);

                ThemeList.Items.Add(swatch);
            }
        }
    }
}
