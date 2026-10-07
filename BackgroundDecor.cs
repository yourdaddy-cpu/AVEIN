using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AVEIN
{
    public static class BackgroundDecor
    {
        public static void Apply(Grid target, string themeName)
        {
            target.Children.Clear();

            switch (themeName)
            {
                case "Forest Green": BuildJungle(target); break;
                case "Clean Light":  BuildRain(target);   break;
                case "Midnight Purple": BuildBlackHole(target); break;
                case "Ember Orange": BuildSun(target);    break;
                case "Rose":         BuildRose(target);   break;
                default:             BuildJungle(target); break;
            }
        }

        private static void BuildJungle(Grid target)
        {
            for (int i = 0; i < 5; i++)
            {
                target.Children.Add(new Ellipse
                {
                    Width = 60, Height = 22,
                    Fill = new SolidColorBrush(Color.FromArgb(28, 34, 197, 94)),
                    RenderTransform = new RotateTransform(30 + i * 15),
                    HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(10 + i * 18, 10 + i * 30, 10 + i * 18, 0)
                });
            }
        }

        private static void BuildRain(Grid target)
        {
            for (int i = 0; i < 14; i++)
            {
                target.Children.Add(new Line
                {
                    X1 = 0, Y1 = 0, X2 = -8, Y2 = 40,
                    Stroke = new SolidColorBrush(Color.FromArgb(22, 100, 130, 200)),
                    StrokeThickness = 2,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(20 + i * 40, -10, 0, 0)
                });
            }
        }

        private static void BuildBlackHole(Grid target)
        {
            var glow = new Ellipse
            {
                Width = 140, Height = 140,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 20, 20)
            };
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(60, 0, 0, 0), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(35, 168, 85, 247), 0.7));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 168, 85, 247), 1));
            glow.Fill = brush;
            target.Children.Add(glow);
        }

        private static void BuildSun(Grid target)
        {
            var sun = new Ellipse
            {
                Width = 100, Height = 100,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 20, 20, 0)
            };
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(50, 249, 115, 22), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 249, 115, 22), 1));
            sun.Fill = brush;
            target.Children.Add(sun);
        }

        private static void BuildRose(Grid target)
        {
            var glow = new Ellipse
            {
                Width = 160, Height = 160,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 10)
            };
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(45, 236, 72, 153), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 236, 72, 153), 1));
            glow.Fill = brush;
            target.Children.Add(glow);

            target.Children.Add(new TextBlock
            {
                Text = "there is no one left in this world to stop me",
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                Opacity = 0.18,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 16, 16)
            });
        }
    }
}
