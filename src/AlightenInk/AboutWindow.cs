using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AlightenInk
{
    /// <summary>Small branded about card with the shortcut cheat-sheet.</summary>
    internal sealed class AboutWindow : Window
    {
        public AboutWindow(App app)
        {
            Title = "About AlightenInk";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            var white = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
            var muted = new SolidColorBrush(Color.FromArgb(0x99, 0xF5, 0xF5, 0xF5));
            var pumpkin = new SolidColorBrush(Color.FromRgb(0xF0, 0x90, 0x30));

            var logo = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/AlightenInk.png")),
                Width = 56,
                Height = 56,
            };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            var titleStack = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = "AlightenInk", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = white });
            titleStack.Children.Add(new TextBlock { Text = "Version " + Updater.CurrentVersion + "  ·  free for traders", FontSize = 12.5, Foreground = muted });
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(logo);
            head.Children.Add(titleStack);

            var grid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[,] rows =
            {
                { "Ctrl+Shift+D", "Draw / pass-through" },
                { "Ctrl+Shift+F", "Fading ink on/off" },
                { "Ctrl+Shift+C", "Copy a region to the clipboard" },
                { "Ctrl+Shift+S", "Screenshot (PNG + clipboard)" },
                { "Ctrl+Shift+Z / Y", "Undo / redo" },
                { "Ctrl+Shift+X", "Clear everything" },
                { "Ctrl+Shift+W", "Whiteboard → blackboard → off" },
                { "P H E T", "Pen, highlighter, eraser, text" },
                { "L A R O I", "Line, arrow, rect, ellipse, h-line" },
                { "1 2 3 · Esc", "Width · clear (while drawing)" },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 2, 7, 3),
                    Margin = new Thickness(0, 3, 12, 3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = rows[i, 0], Foreground = pumpkin, FontSize = 12, FontWeight = FontWeights.SemiBold },
                };
                var d = new TextBlock { Text = rows[i, 1], Foreground = white, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(k, i);
                Grid.SetRow(d, i);
                Grid.SetColumn(d, 1);
                grid.Children.Add(k);
                grid.Children.Add(d);
            }

            var close = new Button { Content = "Close", Style = (Style)Application.Current.FindResource("PillButtonStyle"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Background = pumpkin, Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x39)) };
            close.Click += (_, __) => Close();

            var stack = new StackPanel { Width = 360 };
            stack.Children.Add(head);
            stack.Children.Add(grid);
            stack.Children.Add(new TextBlock
            {
                Text = "Screenshots: Pictures\\AlightenInk   ·   Settings: %AppData%\\AlightenInk",
                Foreground = muted,
                FontSize = 11,
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            stack.Children.Add(close);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF7, 0x16, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22, 20, 22, 18),
                Margin = new Thickness(24),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.55 },
                Child = stack,
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = card;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };
            Deactivated += (_, __) => Close();
        }
    }
}
