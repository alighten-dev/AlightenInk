using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AlightenInk.Core;

namespace AlightenInk
{
    /// <summary>A compact HSV colour popover (saturation/value square + hue bar) in the toolbar's style.</summary>
    internal sealed class ColorPickerPopup : Popup
    {
        private const double SvWidth = 212, SvHeight = 132, HueHeight = 14;

        private readonly Border _sv;
        private readonly Ellipse _svThumb;
        private readonly Border _hueBar;
        private readonly Border _hueThumb;
        private readonly Ellipse _preview;
        private readonly TextBlock _hex;
        private readonly Canvas _svCanvas;
        private readonly Canvas _hueCanvas;
        private readonly WrapPanel _quick;

        private double _h, _s = 1, _v = 1;

        public ColorPickerPopup()
        {
            AllowsTransparency = true;
            StaysOpen = true;
            PopupAnimation = PopupAnimation.Fade;
            Placement = PlacementMode.Bottom;
            VerticalOffset = -10;
            HorizontalOffset = -10;

            // Saturation/value square: hue base + white→transparent + transparent→black.
            _sv = new Border { Width = SvWidth, Height = SvHeight, CornerRadius = new CornerRadius(8), ClipToBounds = true };
            var whiteLayer = new Rectangle
            {
                Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0),
                RadiusX = 8,
                RadiusY = 8,
            };
            var blackLayer = new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90),
                RadiusX = 8,
                RadiusY = 8,
            };
            _svThumb = new Ellipse
            {
                Width = 16,
                Height = 16,
                Stroke = Brushes.White,
                StrokeThickness = 2.5,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.8 },
            };
            _svCanvas = new Canvas { Width = SvWidth, Height = SvHeight, Background = Brushes.Transparent, Cursor = Cursors.Cross };
            var svGrid = new Grid { Width = SvWidth, Height = SvHeight };
            svGrid.Children.Add(_sv);
            svGrid.Children.Add(whiteLayer);
            svGrid.Children.Add(blackLayer);
            svGrid.Children.Add(_svCanvas);
            _svCanvas.Children.Add(_svThumb);
            HookDrag(_svCanvas, p =>
            {
                _s = Math.Clamp(p.X / SvWidth, 0, 1);
                _v = 1 - Math.Clamp(p.Y / SvHeight, 0, 1);
                UpdateVisuals();
            });

            // Hue bar
            var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i <= 6; i++)
            {
                var (r, g, b) = ColorMath.HsvToRgb(i * 60, 1, 1);
                hueBrush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), i / 6.0));
            }
            _hueBar = new Border { Width = SvWidth, Height = HueHeight, CornerRadius = new CornerRadius(HueHeight / 2), Background = hueBrush };
            _hueThumb = new Border
            {
                Width = 8,
                Height = HueHeight + 6,
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.8 },
            };
            _hueCanvas = new Canvas { Width = SvWidth, Height = HueHeight + 6, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var hueGrid = new Grid { Width = SvWidth, Height = HueHeight + 6, Margin = new Thickness(0, 10, 0, 0) };
            _hueBar.VerticalAlignment = VerticalAlignment.Center;
            hueGrid.Children.Add(_hueBar);
            hueGrid.Children.Add(_hueCanvas);
            _hueCanvas.Children.Add(_hueThumb);
            HookDrag(_hueCanvas, p =>
            {
                _h = Math.Clamp(p.X / SvWidth, 0, 0.9999) * 360;
                UpdateVisuals();
            });

            // Quick tints row (common trading colours not in the defaults).
            _quick = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (string q in new[] { "#FF8C00", "#FF6B81", "#A55EEA", "#4B7BEC", "#2BCBBA", "#B8E994", "#C8D6E5", "#000000" })
            {
                string hex = q;
                var dot = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 6.5, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = hex,
                };
                dot.MouseLeftButtonUp += (_, __) => SetFromHex(hex);
                _quick.Children.Add(dot);
            }

            // Footer: preview + hex + buttons.
            _preview = new Ellipse { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 };
            _hex = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("PillButtonStyle"), Margin = new Thickness(0, 0, 6, 0) };
            cancel.Click += (_, __) => IsOpen = false;
            var add = new Button
            {
                Content = "Add",
                Style = (Style)Application.Current.FindResource("PillButtonStyle"),
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0x90, 0x30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x39)),
            };
            add.Click += (_, __) =>
            {
                IsOpen = false;
                ColorChosen?.Invoke(this, CurrentHex);
            };
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(_preview);
            left.Children.Add(_hex);
            DockPanel.SetDock(left, Dock.Left);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(cancel);
            right.Children.Add(add);
            DockPanel.SetDock(right, Dock.Right);
            footer.Children.Add(left);
            footer.Children.Add(right);

            var title = new TextBlock
            {
                Text = "CUSTOM COLOUR",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xF5, 0xF5, 0xF5)),
                Margin = new Thickness(2, 0, 0, 8),
            };

            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(svGrid);
            stack.Children.Add(hueGrid);
            stack.Children.Add(_quick);
            stack.Children.Add(footer);

            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x16, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 12, 14, 14),
                Margin = new Thickness(14),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5 },
                Child = stack,
            };
            TextElement.SetFontFamily(Child, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        }

        public event EventHandler<string>? ColorChosen;

        public string CurrentHex
        {
            get
            {
                var (r, g, b) = ColorMath.HsvToRgb(_h, _s, _v);
                return ColorText.Format(r, g, b);
            }
        }

        public void Open(string startHex)
        {
            SetFromHex(startHex);
            IsOpen = true;
        }

        private void SetFromHex(string hex)
        {
            var (r, g, b) = ColorText.Parse(hex);
            var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
            // Keep the previous hue for greys so the square doesn't jump to red.
            if (s > 0.001) _h = h;
            _s = s;
            _v = v;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            var (hr, hg, hb) = ColorMath.HsvToRgb(_h, 1, 1);
            _sv.Background = new SolidColorBrush(Color.FromRgb(hr, hg, hb));
            Canvas.SetLeft(_svThumb, _s * SvWidth - 8);
            Canvas.SetTop(_svThumb, (1 - _v) * SvHeight - 8);
            Canvas.SetLeft(_hueThumb, _h / 360 * SvWidth - 4);
            Canvas.SetTop(_hueThumb, 0);
            var (r, g, b) = ColorMath.HsvToRgb(_h, _s, _v);
            _preview.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
            _hex.Text = ColorText.Format(r, g, b);
        }

        private static void HookDrag(FrameworkElement el, Action<Point> update)
        {
            el.MouseLeftButtonDown += (_, e) =>
            {
                el.CaptureMouse();
                update(e.GetPosition(el));
                e.Handled = true;
            };
            el.MouseMove += (_, e) =>
            {
                if (el.IsMouseCaptured) update(e.GetPosition(el));
            };
            el.MouseLeftButtonUp += (_, e) =>
            {
                if (el.IsMouseCaptured) el.ReleaseMouseCapture();
            };
        }
    }
}
