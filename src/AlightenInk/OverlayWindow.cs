using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AlightenInk.Core;
using WinForms = System.Windows.Forms;

namespace AlightenInk
{
    public enum Tool
    {
        Cursor,
        Pen,
        Highlighter,
        Eraser,
        Text,
        Line,
        Arrow,
        Rectangle,
        Ellipse,
        HorizontalLine
    }

    public enum BoardMode
    {
        None,
        White,
        Black
    }

    /// <summary>
    /// One transparent, topmost window per monitor. Holds strokes (pen, highlighter, shapes) in an
    /// InkCanvas and committed text as TextBlocks in the same canvas' Children.
    /// </summary>
    internal sealed class OverlayWindow : Window, IItemHost<object>
    {
        private static readonly Brush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
        private static readonly Brush WhiteBoardBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF7)));
        private static readonly Brush BlackBoardBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0E, 0x16, 0x20)));

        private readonly App _app;
        private readonly InkCanvas _ink;
        private readonly Canvas _top;       // eraser ring + text editor (above the ink)
        private IntPtr _hwnd;
        private bool _drawMode;
        private Tool _tool = Tool.Cursor;
        private BoardMode _board;

        // Shape / eraser gesture state
        private Stroke? _previewStroke;
        private Point _dragStart;
        private bool _erasing;
        private Point _lastErase;
        private IDisposable? _eraseGroup;

        // Text editing state
        private Border? _editorBorder;
        private TextBox? _editor;

        public OverlayWindow(App app, WinForms.Screen screen)
        {
            _app = app;
            Screen = screen;
            DeviceName = screen.DeviceName;

            Title = "AlightenInk Overlay";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Rough DIP placement so the HWND is born on the right monitor; SetWindowPos makes it exact.
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = Math.Max(100, screen.Bounds.Width);
            Height = Math.Max(100, screen.Bounds.Height);

            _ink = new InkCanvas
            {
                Background = Brushes.Transparent,
                EditingMode = InkCanvasEditingMode.None,
                UseCustomCursor = true,
                Cursor = Cursors.Cross,
            };
            _ink.StrokeCollected += OnStrokeCollected;

            _top = new Canvas { Background = null };

            var root = new Grid();
            root.Children.Add(_ink);
            root.Children.Add(_top);
            Content = root;

            SourceInitialized += OnSourceInitialized;
            PreviewMouseLeftButtonDown += OnDown;
            PreviewMouseMove += OnMove;
            PreviewMouseLeftButtonUp += OnUp;
            PreviewKeyDown += OnPreviewKeyDown;
            LostMouseCapture += (_, __) => FinishGesture(cancel: false);
            // Activation raises an overlay to the top of the topmost band; put the toolbar back above it.
            Activated += (_, __) => _app.KeepToolbarOnTop();
        }

        public WinForms.Screen Screen { get; private set; }
        public string DeviceName { get; }
        public IntPtr Handle => _hwnd;
        public bool IsEditingText => _editor != null;

        public IEnumerable<object> Items =>
            _ink.Strokes.Cast<object>().Concat(_ink.Children.OfType<TextBlock>()).ToList();

        private static Brush Freeze(Brush b)
        {
            b.Freeze();
            return b;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetExStyle(_hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
            Native.SetExStyle(_hwnd, ex);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            ApplyBounds();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_DPICHANGED)
            {
                // Let WPF rescale, then pin the window back to the exact monitor rectangle.
                Dispatcher.BeginInvoke(new Action(ApplyBounds), System.Windows.Threading.DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        public void UpdateScreen(WinForms.Screen screen)
        {
            Screen = screen;
            ApplyBounds();
        }

        /// <summary>Covers the monitor exactly, in device pixels (correct for mixed-DPI setups).</summary>
        public void ApplyBounds()
        {
            if (_hwnd == IntPtr.Zero) return;
            var b = Screen.Bounds;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        // ---------------------------------------------------------------- modes

        public void SetDrawMode(bool draw)
        {
            _drawMode = draw;
            if (_hwnd != IntPtr.Zero)
            {
                int ex = Native.GetExStyle(_hwnd);
                ex = draw ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
                Native.SetExStyle(_hwnd, ex);
            }
            if (!draw)
            {
                FinishGesture(cancel: false);
                CommitText();
            }
            UpdateBackground();
        }

        public void SetTool(Tool tool)
        {
            if (tool != Tool.Text) CommitText();
            FinishGesture(cancel: false);
            _tool = tool;
            _ink.EditingMode = tool == Tool.Pen || tool == Tool.Highlighter
                ? InkCanvasEditingMode.Ink
                : InkCanvasEditingMode.None;
            UpdateDrawingAttributes();
        }

        public void SetBoard(BoardMode board)
        {
            _board = board;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            Brush bg = _board switch
            {
                BoardMode.White => WhiteBoardBrush,
                BoardMode.Black => BlackBoardBrush,
                _ => _drawMode ? HitBrush : Brushes.Transparent,
            };
            _ink.Background = bg;
        }

        public void UpdateDrawingAttributes()
        {
            var color = _app.InkColor;
            double w = _app.PenWidth;
            var da = new DrawingAttributes
            {
                Color = color,
                FitToCurve = true,
                IgnorePressure = true,
                StylusTip = StylusTip.Ellipse,
                Width = w,
                Height = w,
            };
            if (_tool == Tool.Highlighter)
            {
                da.IsHighlighter = true;
                da.StylusTip = StylusTip.Rectangle;
                da.Width = w * 2.2;
                da.Height = w * 5.5;
            }
            _ink.DefaultDrawingAttributes = da;
            UpdateCursor();
            if (_editor != null)
            {
                _editor.Foreground = new SolidColorBrush(color);
                _editor.CaretBrush = new SolidColorBrush(color);
                _editor.FontSize = _app.TextSize;
            }
        }

        /// <summary>Dual-tone cursor sized to the brush, rendered for this monitor's DPI.</summary>
        private void UpdateCursor()
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _ink.Cursor = CursorFactory.For(_tool, _app.InkColor, _app.PenWidth, _app.TextSize, _app.EraserDiameter, dpi);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            UpdateCursor();
        }

        /// <summary>Puts this overlay in front and gives it keyboard focus (draw mode only).</summary>
        public void ActivateForDrawing()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.ForceForeground(_hwnd);
            Activate();
            Focus();
            Keyboard.Focus(this);
            _app.KeepToolbarOnTop();
        }

        // ---------------------------------------------------------------- items (undo host)

        public void AddItem(object item)
        {
            switch (item)
            {
                case Stroke s when !_ink.Strokes.Contains(s):
                    _ink.Strokes.Add(s);
                    break;
                case UIElement el when !_ink.Children.Contains(el):
                    _ink.Children.Add(el);
                    break;
            }
        }

        public void RemoveItem(object item)
        {
            switch (item)
            {
                case Stroke s when _ink.Strokes.Contains(s):
                    _ink.Strokes.Remove(s);
                    break;
                case UIElement el when _ink.Children.Contains(el):
                    _ink.Children.Remove(el);
                    break;
            }
        }

        public bool Contains(object item) => item switch
        {
            Stroke s => _ink.Strokes.Contains(s),
            UIElement el => _ink.Children.Contains(el),
            _ => false
        };

        private void OnStrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
        {
            _app.OnItemCreated(this, e.Stroke);
        }

        // ---------------------------------------------------------------- mouse gestures

        private static ShapeKind? ShapeFor(Tool t) => t switch
        {
            Tool.Line => ShapeKind.Line,
            Tool.Arrow => ShapeKind.Arrow,
            Tool.Rectangle => ShapeKind.Rectangle,
            Tool.Ellipse => ShapeKind.Ellipse,
            Tool.HorizontalLine => ShapeKind.HorizontalLine,
            _ => null
        };

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!_drawMode) return;
            // A press over the toolbar's area is never ink or a text box (the toolbar should have
            // received it; if z-order slipped, restore it and swallow the press).
            if (_app.IsCursorOverToolbar())
            {
                _app.KeepToolbarOnTop();
                e.Handled = true;
                return;
            }
            Point p = e.GetPosition(_ink);

            if (_tool == Tool.Text)
            {
                if (_editorBorder != null && IsInside(_editorBorder, e)) return; // let the TextBox take the click
                CommitText();
                StartText(p);
                e.Handled = true;
                return;
            }

            if (_tool == Tool.Eraser)
            {
                _erasing = true;
                _eraseGroup = _app.Undo.BeginGroup();
                _lastErase = p;
                EraseAt(p);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            var kind = ShapeFor(_tool);
            if (kind.HasValue)
            {
                _dragStart = p;
                var da = _ink.DefaultDrawingAttributes.Clone();
                da.FitToCurve = false;
                da.IsHighlighter = false;
                da.StylusTip = StylusTip.Ellipse;
                da.Width = da.Height = _app.PenWidth;
                _previewStroke = new Stroke(ToStylus(BuildShape(kind.Value, p, p)), da);
                _ink.Strokes.Add(_previewStroke);
                CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_drawMode) return;
            Point p = e.GetPosition(_ink);

            if (_tool == Tool.Eraser)
            {
                double d = _app.EraserDiameter;
                if (_erasing && e.LeftButton == MouseButtonState.Pressed)
                {
                    // Sample along the segment so fast drags don't skip thin strokes.
                    double dist = (p - _lastErase).Length;
                    int steps = Math.Max(1, (int)(dist / Math.Max(2, d / 3)));
                    for (int i = 1; i <= steps; i++)
                    {
                        EraseAt(_lastErase + (p - _lastErase) * ((double)i / steps));
                    }
                    _lastErase = p;
                }
                return;
            }

            if (_previewStroke != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var kind = ShapeFor(_tool);
                if (kind.HasValue)
                {
                    _previewStroke.StylusPoints = ToStylus(BuildShape(kind.Value, _dragStart, p));
                }
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drawMode) return;
            if (_erasing || _previewStroke != null)
            {
                Point p = e.GetPosition(_ink);
                var kind = ShapeFor(_tool);
                if (_previewStroke != null && kind.HasValue)
                {
                    _previewStroke.StylusPoints = ToStylus(BuildShape(kind.Value, _dragStart, p));
                    if (ShapeGeometry.IsTooSmall(kind.Value, ToPt(_dragStart), ToPt(p)))
                    {
                        FinishGesture(cancel: true);
                        ReleaseMouseCapture();
                        e.Handled = true;
                        return;
                    }
                }
                FinishGesture(cancel: false);
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        /// <summary>Ends any in-progress shape/eraser gesture (committing it unless <paramref name="cancel"/>).</summary>
        private void FinishGesture(bool cancel)
        {
            if (_previewStroke != null)
            {
                var s = _previewStroke;
                _previewStroke = null;
                if (cancel) _ink.Strokes.Remove(s);
                else _app.OnItemCreated(this, s);
            }
            if (_erasing)
            {
                _erasing = false;
                _eraseGroup?.Dispose();
                _eraseGroup = null;
            }
        }

        private IReadOnlyList<Pt> BuildShape(ShapeKind kind, Point a, Point b)
        {
            bool snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            double surface = _ink.ActualWidth > 0 ? _ink.ActualWidth : ActualWidth;
            return ShapeGeometry.Build(kind, ToPt(a), ToPt(b), snap, _app.PenWidth, surface);
        }

        private static Pt ToPt(Point p) => new Pt(p.X, p.Y);

        private static StylusPointCollection ToStylus(IReadOnlyList<Pt> pts)
        {
            var c = new StylusPointCollection(pts.Count);
            foreach (var p in pts) c.Add(new StylusPoint(p.X, p.Y));
            return c;
        }

        private void EraseAt(Point p)
        {
            double d = _app.EraserDiameter;
            foreach (Stroke s in _ink.Strokes.HitTest(p, d).ToList())
            {
                if (ReferenceEquals(s, _previewStroke)) continue;
                _ink.Strokes.Remove(s);
                _app.OnItemErased(this, s);
            }
            foreach (var tb in _ink.Children.OfType<TextBlock>().ToList())
            {
                var r = new Rect(InkCanvas.GetLeft(tb), InkCanvas.GetTop(tb), tb.ActualWidth, tb.ActualHeight);
                r.Inflate(d / 2, d / 2);
                if (r.Contains(p))
                {
                    _ink.Children.Remove(tb);
                    _app.OnItemErased(this, tb);
                }
            }
        }

        private static bool IsInside(FrameworkElement el, MouseEventArgs e)
        {
            var p = e.GetPosition(el);
            return p.X >= 0 && p.Y >= 0 && p.X <= el.ActualWidth && p.Y <= el.ActualHeight;
        }

        // ---------------------------------------------------------------- text

        private const double EditorPadX = 6, EditorPadY = 3;

        private void StartText(Point p)
        {
            var color = _app.InkColor;
            double size = _app.TextSize;
            _editor = new TextBox
            {
                MinWidth = 40,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(color),
                CaretBrush = new SolidColorBrush(color),
                SelectionBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x90, 0x30)),
                FontFamily = TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = size,
                AcceptsReturn = true,
                AcceptsTab = false,
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.NoWrap,
            };
            _editor.PreviewKeyDown += OnEditorKeyDown;
            _editorBorder = new Border
            {
                Child = _editor,
                Padding = new Thickness(EditorPadX, EditorPadY, EditorPadX, EditorPadY),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0x17, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xF0, 0x90, 0x30)),
                BorderThickness = new Thickness(1.5),
            };
            // Place so the caret sits roughly on the click point.
            Canvas.SetLeft(_editorBorder, p.X - EditorPadX - 1.5);
            Canvas.SetTop(_editorBorder, p.Y - size * 0.66 - EditorPadY - 1.5);
            _top.Children.Add(_editorBorder);
            ActivateForDrawing();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _editor?.Focus();
                if (_editor != null) Keyboard.Focus(_editor);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Display, Segoe UI");

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Esc cancels the box; Esc in an empty box also leaves text mode (keyboard way out).
                bool wasEmpty = _editor == null || _editor.Text.Length == 0;
                CancelText();
                e.Handled = true;
                if (wasEmpty) _app.ExitDrawMode();
            }
            else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                CommitText();
                e.Handled = true;
            }
        }

        public void CancelText()
        {
            if (_editorBorder != null) _top.Children.Remove(_editorBorder);
            _editor = null;
            _editorBorder = null;
            Keyboard.Focus(this);
        }

        /// <summary>Turns the open editor (if any) into a committed TextBlock.</summary>
        public void CommitText()
        {
            if (_editor == null || _editorBorder == null) return;
            string text = _editor.Text.TrimEnd('\r', '\n', ' ');
            double left = Canvas.GetLeft(_editorBorder) + EditorPadX + 1.5;
            double top = Canvas.GetTop(_editorBorder) + EditorPadY + 1.5;
            var brush = _editor.Foreground;
            double size = _editor.FontSize;
            CancelText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var tb = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontFamily = TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = size,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.55, Color = Colors.Black },
            };
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
            InkCanvas.SetLeft(tb, left);
            InkCanvas.SetTop(tb, top);
            _ink.Children.Add(tb);
            _app.OnItemCreated(this, tb);
        }

        // ---------------------------------------------------------------- keys

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_editor != null) return; // the text editor handles its own keys
            if (_app.HandleLocalKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true;
        }
    }
}
