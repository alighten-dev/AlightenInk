using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AlightenInk
{
    /// <summary>A toolbar button with an "active" (selected) state rendered as a pumpkin pill.</summary>
    public class IconButton : Button
    {
        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(IconButton), new PropertyMetadata(false));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        /// <summary>Raised on right-click (no context menu is used in the no-activate toolbar).</summary>
        public event EventHandler? RightClick;

        protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (RightClick != null)
            {
                RightClick(this, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }

    /// <summary>Icon factory: Fluent glyphs and a few hand-drawn stroked vector icons.</summary>
    internal static class Icons
    {
        public const string Cursor = "";
        public const string Pen = "";
        public const string Highlighter = "";
        public const string Eraser = "";
        public const string Text = "";
        public const string Undo = "";
        public const string Redo = "";
        public const string Clear = "";
        public const string CopyRegion = "";
        public const string Camera = "";
        public const string More = "";
        public const string Timer = "";
        public const string Add = "";
        public const string Folder = "";
        public const string Sync = "";
        public const string Info = "";
        public const string Close = "";
        public const string Check = "";
        public const string ChevronUp = "";
        public const string ChevronLeft = "";

        public static TextBlock Glyph(string glyph, double size = 17)
        {
            var tb = new TextBlock
            {
                Text = glyph,
                FontSize = size,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            TextOptions.SetTextRenderingMode(tb, TextRenderingMode.Grayscale);
            return tb;
        }

        // Stroked 20x20 vector icons for the shape tools so they match each other exactly.
        public const string LineData = "M4.5,15.5 L15.5,4.5";
        public const string ArrowData = "M4.5,15.5 L15.5,4.5 M8.8,4.5 L15.5,4.5 L15.5,11.2";
        public const string RectData = "M3.5,5.5 L16.5,5.5 L16.5,14.5 L3.5,14.5 Z";
        public const string EllipseData = "M10,4.5 A7,5.5 0 1 1 9.99,4.5 Z";
        public const string HLineData = "M2,10 L11.5,10 M12,7 L17.5,7 L17.5,13 L12,13 Z";
        public const string BoardData = "M3,4.5 L17,4.5 L17,13.5 L3,13.5 Z M10,13.5 L10,16.5 M7,16.5 L13,16.5";
        public const string LayoutHData = "M2.5,7.5 L17.5,7.5 L17.5,12.5 L2.5,12.5 Z";
        public const string LayoutVData = "M7.5,2.5 L12.5,2.5 L12.5,17.5 L7.5,17.5 Z";

        /// <summary>A Path whose stroke follows the hosting button's Foreground (so active = navy).</summary>
        public static FrameworkElement Vector(string data, double thickness = 1.6, double size = 20, bool fillFromForeground = false)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = size,
                Height = size,
                Stretch = Stretch.None,
                SnapsToDevicePixels = false,
            };
            var fg = new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
            };
            path.SetBinding(Shape.StrokeProperty, fg);
            if (fillFromForeground) path.SetBinding(Shape.FillProperty, fg);
            return path;
        }

        /// <summary>Six-dot drag grip.</summary>
        public static FrameworkElement Grip(bool vertical)
        {
            var canvas = new Canvas { Width = vertical ? 18 : 8, Height = vertical ? 8 : 18, IsHitTestVisible = false };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    var dot = new Ellipse { Width = 3, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(0x80, 0xF5, 0xF5, 0xF5)) };
                    double a = i * 6 + 0.5, b = j * 5;
                    Canvas.SetLeft(dot, vertical ? a : b);
                    Canvas.SetTop(dot, vertical ? b : a);
                    canvas.Children.Add(dot);
                }
            }
            return canvas;
        }

        /// <summary>Tooltip content: bold title + muted hotkey/explanation line.</summary>
        public static object Tip(string title, string? hint = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(hint))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = hint,
                    Margin = new Thickness(0, 2, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xF5, 0xF5, 0xF5)),
                    FontSize = 11.5,
                });
            }
            return sp;
        }

        public static void Animate(UIElement el, DependencyProperty dp, double to, double ms = 120)
        {
            el.BeginAnimation(dp, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            });
        }
    }
}
