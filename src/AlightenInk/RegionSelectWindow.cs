using System;
using MemoryStream = System.IO.MemoryStream;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace AlightenInk
{
    /// <summary>
    /// Shows a frozen capture of one monitor, dimmed, and lets the user drag a region.
    /// A click without dragging selects the whole monitor; Esc or right-click cancels.
    /// </summary>
    internal sealed class RegionSelectWindow : Window
    {
        private static readonly Color Pumpkin = Color.FromRgb(0xF0, 0x90, 0x30);

        private readonly Drawing.Rectangle _bounds;
        private readonly Canvas _canvas = new Canvas();
        private readonly Path _dim;
        private readonly Rectangle _outline;
        private readonly Border _readout;
        private readonly TextBlock _readoutText;
        private readonly Border _hint;
        private readonly TaskCompletionSource<Drawing.Rectangle?> _result = new TaskCompletionSource<Drawing.Rectangle?>();
        private IntPtr _hwnd;
        private Point? _start;
        private Rect _sel;

        public RegionSelectWindow(Drawing.Bitmap frozen, Drawing.Rectangle bounds)
        {
            _bounds = bounds;
            Title = "AlightenInk Copy Region";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            var image = new Image { Source = ToBitmapSource(frozen), Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0x05, 0x0A, 0x12)), IsHitTestVisible = false };
            _outline = new Rectangle
            {
                Stroke = new SolidColorBrush(Pumpkin),
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.6 },
            };
            _readoutText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x39)),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
            };
            _readout = new Border
            {
                Background = new SolidColorBrush(Pumpkin),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = _readoutText,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x16, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 9, 16, 9),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5 },
                Child = new TextBlock
                {
                    Text = "Drag to copy a region  ·  Click for the whole screen  ·  Esc to cancel",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                    FontSize = 13,
                },
            };

            var grid = new Grid();
            grid.Children.Add(image);
            grid.Children.Add(_canvas);
            _canvas.Children.Add(_dim);
            _canvas.Children.Add(_outline);
            _canvas.Children.Add(_readout);
            _canvas.Children.Add(_hint);
            _canvas.Background = Brushes.Transparent;
            Content = grid;

            SourceInitialized += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.SetExStyle(_hwnd, Native.GetExStyle(_hwnd) | Native.WS_EX_TOOLWINDOW);
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, 0);
            };
            Loaded += (_, __) =>
            {
                UpdateDim();
                _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(_hint, (ActualWidth - _hint.DesiredSize.Width) / 2);
                Canvas.SetTop(_hint, 28);
                Native.ForceForeground(_hwnd);
                Activate();
                Focus();
            };
            SizeChanged += (_, __) => UpdateDim();
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += (_, __) => Finish(null);
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Finish(null);
            };
            Deactivated += (_, __) =>
            {
                // Losing focus mid-pick (e.g. Alt+Tab) cancels rather than leaving a frozen screen up.
                if (!_result.Task.IsCompleted && _start == null) Finish(null);
            };
        }

        public Task<Drawing.Rectangle?> PickAsync()
        {
            Show();
            return _result.Task;
        }

        private static BitmapSource ToBitmapSource(Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(_canvas);
            _sel = new Rect(_start.Value, _start.Value);
            _hint.Visibility = Visibility.Collapsed;
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_start == null) return;
            _sel = new Rect(_start.Value, e.GetPosition(_canvas));
            _sel.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
            if (_sel.IsEmpty) return;
            _outline.Visibility = Visibility.Visible;
            Canvas.SetLeft(_outline, _sel.X);
            Canvas.SetTop(_outline, _sel.Y);
            _outline.Width = _sel.Width;
            _outline.Height = _sel.Height;

            var px = ToPixels(_sel);
            _readoutText.Text = $"{px.Width} × {px.Height}";
            _readout.Visibility = Visibility.Visible;
            _readout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double ry = _sel.Bottom + 8;
            if (ry + _readout.DesiredSize.Height > ActualHeight - 4) ry = _sel.Bottom - _readout.DesiredSize.Height - 8;
            Canvas.SetLeft(_readout, Math.Min(_sel.X, ActualWidth - _readout.DesiredSize.Width - 4));
            Canvas.SetTop(_readout, ry);
            UpdateDim();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_start == null) return;
            ReleaseMouseCapture();
            var end = e.GetPosition(_canvas);
            bool click = Math.Abs(end.X - _start.Value.X) < 4 && Math.Abs(end.Y - _start.Value.Y) < 4;
            if (click)
            {
                Finish(new Drawing.Rectangle(0, 0, _bounds.Width, _bounds.Height));
                return;
            }
            var px = ToPixels(_sel);
            Finish(px.Width > 0 && px.Height > 0 ? px : (Drawing.Rectangle?)null);
        }

        /// <summary>DIP rectangle on this window → pixel rectangle in the frozen bitmap.</summary>
        private Drawing.Rectangle ToPixels(Rect r)
        {
            double sx = ActualWidth > 0 ? _bounds.Width / ActualWidth : 1;
            double sy = ActualHeight > 0 ? _bounds.Height / ActualHeight : 1;
            int x = (int)Math.Round(r.X * sx), y = (int)Math.Round(r.Y * sy);
            int w = (int)Math.Round(r.Width * sx), h = (int)Math.Round(r.Height * sy);
            return new Drawing.Rectangle(x, y, w, h);
        }

        private void UpdateDim()
        {
            var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
            if (_start == null || _sel.IsEmpty || _sel.Width < 1)
            {
                _dim.Data = full;
                return;
            }
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(_sel));
        }

        private void Finish(Drawing.Rectangle? result)
        {
            if (_result.Task.IsCompleted) return;
            _result.TrySetResult(result);
            Close();
        }
    }
}
