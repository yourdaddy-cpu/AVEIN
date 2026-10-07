using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AVEIN.Views
{
    public partial class MemoryView : UserControl
    {
        public event Action<string> OpenSessionRequested;

        public MemoryView()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadSessions();
        }

        private void LoadSessions()
        {
            SessionList.Children.Clear();
            var sessions = AVEIN.HistoryStore.LoadAllSessions();

            if (sessions.Count == 0)
            {
                SessionList.Children.Add(new TextBlock
                {
                    Text = "No conversations saved yet - chat with AVEIN to build up history here.",
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            foreach (var session in sessions)
                SessionList.Children.Add(BuildSessionRow(session));
        }

        private Border BuildSessionRow(AVEIN.ChatSession session)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textPanel = new StackPanel { Cursor = System.Windows.Input.Cursors.Hand };
            textPanel.Children.Add(new TextBlock
            {
                Text = session.Title,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = session.CreatedAt + " - " + session.Messages.Count + " messages",
                FontSize = 11,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            textPanel.MouseLeftButtonUp += (s, e) => OpenSessionRequested?.Invoke(session.Id);

            var deleteButton = new Button
            {
                Content = "Delete",
                Padding = new Thickness(12, 6, 12, 6),
                Background = (Brush)FindResource("SurfaceBrush"),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            deleteButton.Click += (s, e) =>
            {
                AVEIN.HistoryStore.DeleteSession(session.Id);
                LoadSessions();
            };

            Grid.SetColumn(textPanel, 0);
            Grid.SetColumn(deleteButton, 1);
            grid.Children.Add(textPanel);
            grid.Children.Add(deleteButton);

            return new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };
        }
    }
}
