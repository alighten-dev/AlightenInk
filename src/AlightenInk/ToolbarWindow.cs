using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AlightenInk.Core;
using WinForms = System.Windows.Forms;

namespace AlightenInk
{
    /// <summary>
    /// The floating tool palette. Never takes focus (WS_EX_NOACTIVATE) so the app being annotated
    /// keeps keyboard focus. Horizontal or vertical layout, collapsible to a round button.
    /// </summary>
    internal sealed class ToolbarWindow : Window
    {
        private const double ShadowMargin = 18;
        private static readonly Color Pumpkin = Color.FromRgb(0xF0, 0x90, 0x30);
        private static readonly Color Navy = Color.FromRgb(0x1C, 0x2A, 0x39);
        private static readonly Color Ink = Color.FromRgb(0xF5, 0xF5, 0xF5);

        private static readonly Dictionary<string, string> SwatchNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#F09030"] = "Pumpkin",
            ["#F5F5F5"] = "White",
            ["#FF4757"] = "Red",
            ["#26DE81"] = "Green",
            ["#00C8FF"] = "Cyan",
            ["#FFD400"] = "Yellow",
            ["#1C2A39"] = "Navy",
        };

        private readonly App _app;
        private readonly Grid _root = new Grid();
        private readonly Border _panel;
        private readonly Border _bubble;
        private readonly Ellipse _bubbleRing;
        private readonly Ellipse _bubbleDot;
        private readonly ScaleTransform _panelScale = new ScaleTransform(1, 1);
        private readonly ScaleTransform _bubbleScale = new ScaleTransform(1, 1);

        private readonly Dictionary<Tool, IconButton> _tools = new Dictionary<Tool, IconButton>();
        private readonly List<IconButton> _swatches = new List<IconButton>();
        private readonly IconButton _addSwatch;
        private readonly IconButton[] _widths = new IconButton[3];
        private readonly IconButton _fade;
        private readonly TextBlock _fadeText;
        private readonly IconButton _board;
        private readonly IconButton _undo;
        private readonly IconButton _redo;
        private readonly IconButton _clear;
        private readonly IconButton _copy;
        private readonly IconButton _shot;
        private readonly IconButton _more;
        private readonly IconButton _collapse;
        private readonly TextBlock _collapseGlyph;
        private readonly Border _grip;

        private readonly Popup _toast;
        private readonly Border _toastBorder;
        private readonly TextBlock _toastText;
        private readonly TextBlock _toastIcon;
        private readonly DispatcherTimer _toastTimer;
        private readonly Popup _menu;
        private readonly ColorPickerPopup _picker;

        private IntPtr _hwnd;
        private string _swatchKey = "";
        private ToolbarLayout? _builtLayout;
        private bool _collapsed;
        private bool _positioned;

        // Drag state (grip or collapsed bubble)
        private FrameworkElement? _dragElement;
        private Native.POINT _dragCursor;
        private Native.RECT _dragRect;
        private bool _dragMoved;

        public ToolbarWindow(App app)
        {
            _app = app;
            Title = "AlightenInk Toolbar";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000;
            Top = -10000;
            FontFamily = (FontFamily)FindResource("UiFont");
            UseLayoutRounding = true;

            // ---- panel
            _panel = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Margin = new Thickness(ShadowMargin),
                Effect = (Effect)FindResource("PanelShadow"),
                RenderTransform = _panelScale,
                RenderTransformOrigin = new Point(0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            // ---- collapsed bubble
            _bubbleRing = new Ellipse { Stroke = new SolidColorBrush(Pumpkin), StrokeThickness = 2, Opacity = 0 };
            _bubbleDot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = new SolidColorBrush(Color.FromRgb(0x16, 0x1E, 0x28)),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 5, 5),
            };
            var bubbleGlyph = Icons.Glyph(Icons.Pen, 21);
            bubbleGlyph.Foreground = new SolidColorBrush(Pumpkin);
            var bubbleGrid = new Grid();
            bubbleGrid.Children.Add(_bubbleRing);
            bubbleGrid.Children.Add(bubbleGlyph);
            bubbleGrid.Children.Add(_bubbleDot);
            _bubble = new Border
            {
                Width = 50,
                Height = 50,
                CornerRadius = new CornerRadius(25),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x1E, 0x28)),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(ShadowMargin),
                Effect = (Effect)FindResource("PanelShadow"),
                Child = bubbleGrid,
                Cursor = Cursors.Hand,
                RenderTransform = _bubbleScale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                ToolTip = Icons.Tip("AlightenInk", "Click to expand · drag to move"),
            };
            AttachDrag(_bubble, () => SetCollapsed(false));

            _root.Children.Add(_panel);
            _root.Children.Add(_bubble);
            Content = _root;

            // ---- buttons
            _grip = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                ToolTip = Icons.Tip("Move", "Drag to move the toolbar"),
            };
            AttachDrag(_grip, null);

            AddTool(Tool.Cursor, Icons.Glyph(Icons.Cursor, 17), "Pass-through", "Clicks go to your apps · Ctrl+Shift+D toggles");
            AddTool(Tool.Pen, Icons.Glyph(Icons.Pen), "Pen", "P");
            AddTool(Tool.Highlighter, Icons.Glyph(Icons.Highlighter), "Highlighter", "H");
            AddTool(Tool.Eraser, Icons.Glyph(Icons.Eraser), "Eraser", "E · erases ink, shapes and text");
            AddTool(Tool.Text, Icons.Glyph(Icons.Text, 16), "Text", "T · click, type, Enter · Shift+Enter new line · Esc cancels");
            AddTool(Tool.Line, Icons.Vector(Icons.LineData), "Line", "L · hold Shift to snap to 45°");
            AddTool(Tool.Arrow, Icons.Vector(Icons.ArrowData), "Arrow", "A · hold Shift to snap to 45°");
            AddTool(Tool.Rectangle, Icons.Vector(Icons.RectData), "Rectangle", "R · hold Shift for a square");
            AddTool(Tool.Ellipse, Icons.Vector("M3,10 A7,5.5 0 1 1 17,10 A7,5.5 0 1 1 3,10 Z"), "Ellipse", "O · hold Shift for a circle");
            AddTool(Tool.HorizontalLine, Icons.Vector(Icons.HLineData), "Horizontal line", "I · full-width price line");

            _addSwatch = new IconButton
            {
                Content = Icons.Glyph(Icons.Add, 12),
                Width = 28,
                ToolTip = Icons.Tip("Add colour", "Pick a custom colour · right-click a custom swatch to remove it"),
            };
            _addSwatch.Click += (_, __) => TogglePicker();

            string[] widthNames = { "Thin", "Medium", "Thick" };
            double[] barHeights = { 2, 4, 7 };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var bar = new Rectangle { Width = 15, Height = barHeights[i], RadiusX = barHeights[i] / 2, RadiusY = barHeights[i] / 2 };
                bar.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1)
                });
                _widths[i] = new IconButton { Content = bar, Width = 30, ToolTip = Icons.Tip(widthNames[i] + " width", (i + 1).ToString()) };
                _widths[i].Click += (_, __) => _app.SetWidth(idx);
            }

            _fadeText = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("UiFont"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
            var fadeStack = new StackPanel { Orientation = Orientation.Vertical };
            var fadeGlyph = Icons.Glyph(Icons.Timer, 14);
            fadeStack.Children.Add(fadeGlyph);
            fadeStack.Children.Add(_fadeText);
            _fade = new IconButton { Content = fadeStack };
            _fade.Click += (_, __) => _app.ToggleFade();
            _fade.RightClick += (_, __) => _app.CycleFadeSeconds();

            _board = new IconButton { Content = Icons.Vector(Icons.BoardData) };
            _board.Click += (_, __) => _app.CycleBoard();

            _undo = new IconButton { Content = Icons.Glyph(Icons.Undo, 16) };
            _undo.Click += (_, __) => _app.DoUndo();
            _redo = new IconButton { Content = Icons.Glyph(Icons.Redo, 16) };
            _redo.Click += (_, __) => _app.DoRedo();
            _clear = new IconButton { Content = Icons.Glyph(Icons.Clear, 16) };
            _clear.Click += (_, __) => _app.ClearAll();
            _copy = new IconButton { Content = Icons.Glyph(Icons.CopyRegion, 17) };
            _copy.Click += async (_, __) => await _app.CopyRegionAsync();
            _shot = new IconButton { Content = Icons.Glyph(Icons.Camera, 17) };
            _shot.Click += async (_, __) => await _app.ScreenshotAsync();
            _more = new IconButton { Content = Icons.Glyph(Icons.More, 16), ToolTip = Icons.Tip("More", "Layout, fade time, updates, about, exit") };
            _more.Click += (_, __) => ToggleMenu();
            _collapseGlyph = Icons.Glyph(Icons.ChevronLeft, 12);
            _collapse = new IconButton { Content = _collapseGlyph, ToolTip = Icons.Tip("Collapse", "Shrink to a small button") };
            _collapse.Click += (_, __) => SetCollapsed(true);

            // ---- toast
            _toastText = new TextBlock { Foreground = new SolidColorBrush(Ink), FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _toastIcon = Icons.Glyph(Icons.Check, 12);
            _toastIcon.Foreground = new SolidColorBrush(Pumpkin);
            _toastIcon.Margin = new Thickness(0, 1, 7, 0);
            var toastRow = new StackPanel { Orientation = Orientation.Horizontal };
            toastRow.Children.Add(_toastIcon);
            toastRow.Children.Add(_toastText);
            _toastBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x1E, 0x28)),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 14, 7),
                Margin = new Thickness(12),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.45 },
                Child = toastRow,
            };
            TextOptions.SetTextFormattingMode(_toastBorder, TextFormattingMode.Display);
            _toast = new Popup
            {
                AllowsTransparency = true,
                Child = _toastBorder,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = PlaceToast,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.None,
                IsHitTestVisible = false,
            };
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
            _toastTimer.Tick += (_, __) => HideToast();

            _menu = BuildMenu();
            _picker = new ColorPickerPopup();
            _picker.ColorChosen += (_, hex) => _app.AddCustomColor(hex);

            SourceInitialized += OnSourceInitialized;
            ContentRendered += (_, __) => Dispatcher.BeginInvoke(new Action(PlaceInitially), DispatcherPriority.Background);
            _app.StateChanged += (_, __) => Refresh();
            _collapsed = _app.Settings.Collapsed;
            Refresh();
            ApplyCollapsedVisuals(animate: false);
        }

        // ================================================================ window plumbing

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, Native.GetExStyle(_hwnd) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        private void PlaceInitially()
        {
            if (_positioned) return;
            _positioned = true;
            var s = _app.Settings;
            if (s.ToolbarX.HasValue && s.ToolbarY.HasValue)
            {
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, (int)s.ToolbarX.Value, (int)s.ToolbarY.Value, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                EnsureOnScreen();
            }
            else
            {
                Native.GetWindowRect(_hwnd, out var r);
                var wa = WinForms.Screen.PrimaryScreen!.WorkingArea;
                int x = wa.Left + (wa.Width - r.Width) / 2;
                int y = wa.Top + 4;
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        /// <summary>Keeps the visible panel inside the working area of the monitor it is mostly on.</summary>
        public void EnsureOnScreen()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_hwnd == IntPtr.Zero || !IsVisible) return;
                Native.GetWindowRect(_hwnd, out var r);
                double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
                int m = (int)Math.Round((ShadowMargin - 4) * scale);
                var rect = new System.Drawing.Rectangle(r.Left, r.Top, r.Width, r.Height);
                var screen = WinForms.Screen.AllScreens
                    .OrderByDescending(sc => Area(System.Drawing.Rectangle.Intersect(sc.WorkingArea, rect)))
                    .First();
                var wa = screen.WorkingArea;
                int x = r.Left, y = r.Top;
                if (x + r.Width - m > wa.Right) x = wa.Right - r.Width + m;
                if (y + r.Height - m > wa.Bottom) y = wa.Bottom - r.Height + m;
                if (x + m < wa.Left) x = wa.Left - m;
                if (y + m < wa.Top) y = wa.Top - m;
                if (x != r.Left || y != r.Top)
                {
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
            }), DispatcherPriority.Loaded);
        }

        private static long Area(System.Drawing.Rectangle r) => r.Width <= 0 || r.Height <= 0 ? 0 : (long)r.Width * r.Height;

        public void BringToFront()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void HideForCapture()
        {
            _menu.IsOpen = false;
            _picker.IsOpen = false;
            HideToast(immediate: true);
            Hide();
        }

        public void ShowAfterCapture()
        {
            Show();
            BringToFront();
        }

        // ================================================================ dragging

        private void AttachDrag(FrameworkElement el, Action? onClick)
        {
            el.MouseLeftButtonDown += (_, e) =>
            {
                _dragElement = el;
                _dragMoved = false;
                Native.GetCursorPos(out _dragCursor);
                Native.GetWindowRect(_hwnd, out _dragRect);
                el.CaptureMouse();
                e.Handled = true;
            };
            el.MouseMove += (_, e) =>
            {
                if (_dragElement != el || !el.IsMouseCaptured) return;
                Native.GetCursorPos(out var p);
                int dx = p.X - _dragCursor.X, dy = p.Y - _dragCursor.Y;
                if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
                _dragMoved = true;
                Native.SetWindowPos(_hwnd, IntPtr.Zero, _dragRect.Left + dx, _dragRect.Top + dy, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            };
            el.MouseLeftButtonUp += (_, e) =>
            {
                if (_dragElement != el) return;
                _dragElement = null;
                el.ReleaseMouseCapture();
                e.Handled = true;
                if (_dragMoved)
                {
                    EnsureOnScreen();
                    Dispatcher.BeginInvoke(new Action(SavePosition), DispatcherPriority.ContextIdle);
                }
                else
                {
                    onClick?.Invoke();
                }
            };
        }

        private void SavePosition()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.GetWindowRect(_hwnd, out var r);
            _app.SaveToolbarPosition(r.Left, r.Top);
        }

        // ================================================================ collapse

        private void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;
            _collapsed = collapsed;
            _menu.IsOpen = false;
            _picker.IsOpen = false;
            _app.SetCollapsed(collapsed);
            ApplyCollapsedVisuals(animate: true);
            if (!collapsed) EnsureOnScreen();
        }

        private void ApplyCollapsedVisuals(bool animate)
        {
            var show = _collapsed ? (FrameworkElement)_bubble : _panel;
            var hide = _collapsed ? (FrameworkElement)_panel : _bubble;
            var scale = _collapsed ? _bubbleScale : _panelScale;
            hide.Visibility = Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            if (!animate) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(160);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, dur) { EasingFunction = ease });
            show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        }

        // ================================================================ layout

        private void AddTool(Tool tool, FrameworkElement icon, string title, string hint)
        {
            var b = new IconButton { Content = icon, ToolTip = Icons.Tip(title, hint) };
            b.Click += (_, __) =>
            {
                _picker.IsOpen = false;
                _app.SelectTool(tool);
            };
            _tools[tool] = b;
        }

        private static void Detach(UIElement el)
        {
            if (el is FrameworkElement fe && fe.Parent is Panel p) p.Children.Remove(el);
            else if (el is FrameworkElement fe2 && fe2.Parent is Decorator d) d.Child = null;
        }

        private FrameworkElement Separator(bool vertical)
        {
            return new Rectangle
            {
                Fill = (Brush)FindResource("SeparatorBrush"),
                Width = vertical ? 56 : 1,
                Height = vertical ? 1 : 22,
                Margin = vertical ? new Thickness(0, 6, 0, 6) : new Thickness(6, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
            };
        }

        private Panel Row(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items)
            {
                Detach(i);
                sp.Children.Add(i);
            }
            return sp;
        }

        private void BuildLayout(ToolbarLayout layout)
        {
            bool v = layout == ToolbarLayout.Vertical;
            var all = new List<UIElement>();
            all.Add(_grip);
            all.AddRange(_tools.Values);
            all.AddRange(_swatches);
            all.Add(_addSwatch);
            all.AddRange(_widths);
            all.AddRange(new UIElement[] { _fade, _board, _undo, _redo, _clear, _copy, _shot, _more, _collapse });
            foreach (var el in all) Detach(el);

            _grip.Child = Icons.Grip(v);
            _grip.Width = v ? 72 : 18;
            _grip.Height = v ? 18 : 36;
            _grip.Child.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            _grip.Child.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            _collapseGlyph.Text = v ? Icons.ChevronUp : Icons.ChevronLeft;

            var placement = v ? PlacementMode.Right : PlacementMode.Bottom;
            foreach (var el in all) ToolTipService.SetPlacement(el, placement);

            var swatchPanel = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (v) swatchPanel.Width = 76;
            foreach (var s in _swatches)
            {
                s.Width = v ? 38 : 28;
                s.Height = v ? 32 : 36;
                swatchPanel.Children.Add(s);
            }
            _addSwatch.Width = v ? 38 : 28;
            _addSwatch.Height = v ? 32 : 36;
            swatchPanel.Children.Add(_addSwatch);

            var stack = new StackPanel { Orientation = v ? Orientation.Vertical : Orientation.Horizontal };
            if (v)
            {
                stack.Children.Add(_grip);
                stack.Children.Add(Separator(true));
                stack.Children.Add(Row(_tools[Tool.Cursor], _tools[Tool.Eraser]));
                stack.Children.Add(Row(_tools[Tool.Pen], _tools[Tool.Highlighter]));
                stack.Children.Add(Row(_tools[Tool.Text], _tools[Tool.HorizontalLine]));
                stack.Children.Add(Separator(true));
                stack.Children.Add(Row(_tools[Tool.Line], _tools[Tool.Arrow]));
                stack.Children.Add(Row(_tools[Tool.Rectangle], _tools[Tool.Ellipse]));
                stack.Children.Add(Separator(true));
                stack.Children.Add(swatchPanel);
                stack.Children.Add(Separator(true));
                stack.Children.Add(Row(_widths));
                stack.Children.Add(Separator(true));
                stack.Children.Add(Row(_fade, _board));
                stack.Children.Add(Row(_undo, _redo));
                stack.Children.Add(Row(_clear, _copy));
                stack.Children.Add(Row(_shot, _more));
                stack.Children.Add(Separator(true));
                _collapse.Width = 74;
                _collapse.Height = 26;
                stack.Children.Add(Row(_collapse));
            }
            else
            {
                _collapse.Width = 28;
                _collapse.Height = 36;
                stack.Children.Add(_grip);
                stack.Children.Add(Row(_tools[Tool.Cursor]));
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_tools[Tool.Pen], _tools[Tool.Highlighter], _tools[Tool.Eraser], _tools[Tool.Text]));
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_tools[Tool.Line], _tools[Tool.Arrow], _tools[Tool.Rectangle], _tools[Tool.Ellipse], _tools[Tool.HorizontalLine]));
                stack.Children.Add(Separator(false));
                stack.Children.Add(swatchPanel);
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_widths));
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_fade, _board));
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_undo, _redo, _clear));
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_copy, _shot));
                stack.Children.Add(Separator(false));
                stack.Children.Add(Row(_more, _collapse));
            }
            _panel.Child = stack;
            _builtLayout = layout;
            _toast.Placement = PlacementMode.Custom;
            _menu.Placement = v ? PlacementMode.Right : PlacementMode.Bottom;
            _picker.Placement = v ? PlacementMode.Right : PlacementMode.Bottom;
        }

        private void RebuildSwatches()
        {
            foreach (var s in _swatches) Detach(s);
            _swatches.Clear();
            var custom = new HashSet<string>(_app.Settings.CustomSwatches, StringComparer.OrdinalIgnoreCase);
            foreach (string hex in _app.Settings.AllSwatches)
            {
                string h = hex;
                bool isCustom = custom.Contains(h);
                var b = new IconButton
                {
                    Style = (Style)FindResource("SwatchStyle"),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)),
                    Tag = h,
                    ToolTip = Icons.Tip(SwatchNames.TryGetValue(h, out var name) ? name : h, isCustom ? "Right-click to remove" : null),
                };
                b.Click += (_, __) => _app.SetColor(h);
                if (isCustom) b.RightClick += (_, __) => _app.RemoveCustomColor(h);
                _swatches.Add(b);
            }
            _builtLayout = null; // force re-layout so the new buttons are placed
        }

        // ================================================================ state → visuals

        private void Refresh()
        {
            var s = _app.Settings;
            string key = string.Join(",", s.AllSwatches);
            if (key != _swatchKey)
            {
                _swatchKey = key;
                RebuildSwatches();
            }
            if (_builtLayout != s.Layout) BuildLayout(s.Layout);

            foreach (var kv in _tools) kv.Value.IsActive = kv.Key == _app.CurrentTool;
            foreach (var sw in _swatches) sw.IsActive = string.Equals((string)sw.Tag, s.Color, StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < 3; i++) _widths[i].IsActive = i == s.WidthIndex;

            _fade.IsActive = s.FadeEnabled;
            _fadeText.Text = $"{s.FadeSeconds:0}s";
            _board.IsActive = _app.Board != BoardMode.None;
            _undo.IsEnabled = _app.Undo.CanUndo;
            _redo.IsEnabled = _app.Undo.CanRedo;

            var hk = _app.Hotkeys;
            string K(HotkeyCommand c) => hk?.Describe(c) ?? DefaultHotkeys.Bindings[c];
            _tools[Tool.Cursor].ToolTip = Icons.Tip("Pass-through", "Clicks go to your apps · " + K(HotkeyCommand.ToggleDraw) + " toggles drawing");
            _fade.ToolTip = Icons.Tip(s.FadeEnabled ? $"Fading ink: on ({s.FadeSeconds:0}s)" : "Fading ink: off", K(HotkeyCommand.ToggleFade) + " · right-click: 2 / 3 / 5 / 8 s");
            _board.ToolTip = Icons.Tip(_app.Board switch { BoardMode.White => "Whiteboard", BoardMode.Black => "Blackboard", _ => "Whiteboard / blackboard" },
                K(HotkeyCommand.Board) + " · cycles white → black → off");
            _undo.ToolTip = Icons.Tip("Undo", "Ctrl+Z · " + K(HotkeyCommand.Undo) + " anywhere");
            _redo.ToolTip = Icons.Tip("Redo", "Ctrl+Y · " + K(HotkeyCommand.Redo) + " anywhere");
            _clear.ToolTip = Icons.Tip("Clear all", "Esc while drawing · " + K(HotkeyCommand.Clear) + " anywhere");
            _copy.ToolTip = Icons.Tip("Copy region", K(HotkeyCommand.CopyRegion) + " · drag a region, click for the whole screen");
            _shot.ToolTip = Icons.Tip("Screenshot", K(HotkeyCommand.Screenshot) + " · saves to Pictures\\AlightenInk and copies");

            // Collapsed bubble shows the colour and whether drawing is on.
            _bubbleDot.Fill = new SolidColorBrush(_app.InkColor);
            _bubbleRing.Opacity = _app.IsDrawMode ? 1 : 0;

            RefreshMenu();
            if (_picker.IsOpen && _app.IsDrawMode == false) { /* keep picker open; independent of mode */ }
        }

        // ================================================================ toast

        public void ShowToast(string message, bool success)
        {
            if (!IsVisible) return;
            _toastText.Text = message.Replace(" ✓", "").Replace("✓", "").Trim();
            _toastIcon.Visibility = success || message.Contains('✓') ? Visibility.Visible : Visibility.Collapsed;
            _toast.PlacementTarget = _collapsed ? _bubble : _panel;
            _toastBorder.BeginAnimation(OpacityProperty, null);
            _toastBorder.Opacity = 1;
            if (_toast.IsOpen)
            {
                // Re-place for the new size.
                _toast.HorizontalOffset += 0.01;
                _toast.HorizontalOffset -= 0.01;
            }
            else
            {
                _toastBorder.Opacity = 0;
                _toast.IsOpen = true;
                _toastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast(bool immediate = false)
        {
            _toastTimer.Stop();
            if (!_toast.IsOpen) return;
            if (immediate)
            {
                _toast.IsOpen = false;
                return;
            }
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            anim.Completed += (_, __) =>
            {
                if (!_toastTimer.IsEnabled) _toast.IsOpen = false;
            };
            _toastBorder.BeginAnimation(OpacityProperty, anim);
        }

        private CustomPopupPlacement[] PlaceToast(Size popupSize, Size targetSize, Point offset)
        {
            // Below and centred for a horizontal bar; to the right for vertical/collapsed.
            bool side = _collapsed || _app.Settings.Layout == ToolbarLayout.Vertical;
            if (side)
            {
                return new[]
                {
                    new CustomPopupPlacement(new Point(targetSize.Width - 8, 0), PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(new Point(-popupSize.Width + 8, 0), PopupPrimaryAxis.Horizontal),
                };
            }
            return new[]
            {
                new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height - 6), PopupPrimaryAxis.Vertical),
                new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height + 6), PopupPrimaryAxis.Vertical),
            };
        }

        // ================================================================ "more" menu

        private Button? _layoutH, _layoutV;
        private readonly List<(Button Button, double Seconds)> _fadeChips = new List<(Button, double)>();

        private Popup BuildMenu()
        {
            var stack = new StackPanel { Width = 236 };

            stack.Children.Add(Header("Toolbar layout"));
            _layoutH = Chip("Horizontal", Icons.LayoutHData);
            _layoutV = Chip("Vertical", Icons.LayoutVData);
            _layoutH.Click += (_, __) => { _app.SetLayout(ToolbarLayout.Horizontal); _menu!.IsOpen = false; EnsureOnScreen(); };
            _layoutV.Click += (_, __) => { _app.SetLayout(ToolbarLayout.Vertical); _menu!.IsOpen = false; EnsureOnScreen(); };
            var seg = new UniformGrid { Columns = 2, Margin = new Thickness(4, 0, 4, 4) };
            seg.Children.Add(_layoutH);
            seg.Children.Add(_layoutV);
            stack.Children.Add(seg);

            stack.Children.Add(Header("Fading ink holds for"));
            var chips = new UniformGrid { Columns = AppSettings.FadeChoices.Length, Margin = new Thickness(4, 0, 4, 4) };
            foreach (double sec in AppSettings.FadeChoices)
            {
                double s = sec;
                var c = Chip($"{s:0} s", null);
                c.Click += (_, __) => _app.SetFadeSeconds(s);
                _fadeChips.Add((c, s));
                chips.Children.Add(c);
            }
            stack.Children.Add(chips);
            stack.Children.Add(MenuSeparator());

            stack.Children.Add(MenuRow(Icons.Folder, "Open screenshots folder", () => _app.OpenScreenshotsFolder()));
            stack.Children.Add(MenuRow(Icons.Sync, "Check for updates", async () => await _app.CheckForUpdatesAsync()));
            stack.Children.Add(MenuRow(Icons.Info, "About AlightenInk", () => _app.ShowAbout()));
            stack.Children.Add(MenuRow("", "Hide toolbar (tray icon restores)", () => _app.ShowToolbar(false)));
            stack.Children.Add(MenuSeparator());
            stack.Children.Add(MenuRow(Icons.Close, "Exit AlightenInk", () => _app.ExitApp()));

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x16, 0x1E, 0x28)),
                BorderBrush = (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6),
                Margin = new Thickness(14),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5 },
                Child = stack,
            };
            TextElement.SetFontFamily(border, (FontFamily)FindResource("UiFont"));
            var popup = new Popup
            {
                AllowsTransparency = true,
                Child = border,
                PlacementTarget = null,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = -10,
                HorizontalOffset = -10,
            };
            return popup;
        }

        private void ToggleMenu()
        {
            _picker.IsOpen = false;
            if (_menu.IsOpen)
            {
                _menu.IsOpen = false;
                return;
            }
            _menu.PlacementTarget = _more;
            RefreshMenu();
            _menu.IsOpen = true;
        }

        private void TogglePicker()
        {
            _menu.IsOpen = false;
            if (_picker.IsOpen)
            {
                _picker.IsOpen = false;
                return;
            }
            _picker.PlacementTarget = _addSwatch;
            _picker.Open(_app.Settings.Color);
        }

        private void RefreshMenu()
        {
            if (_layoutH == null || _layoutV == null) return;
            SetChipActive(_layoutH, _app.Settings.Layout == ToolbarLayout.Horizontal);
            SetChipActive(_layoutV, _app.Settings.Layout == ToolbarLayout.Vertical);
            foreach (var (b, sec) in _fadeChips) SetChipActive(b, Math.Abs(sec - _app.Settings.FadeSeconds) < 0.01);
        }

        private static void SetChipActive(Button b, bool active)
        {
            b.Background = active ? new SolidColorBrush(Pumpkin) : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            b.Foreground = active ? new SolidColorBrush(Navy) : new SolidColorBrush(Ink);
        }

        private Button Chip(string text, string? iconData)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (iconData != null)
            {
                var icon = Icons.Vector(iconData, 1.4, 20);
                icon.Width = icon.Height = 16;
                ((Path)icon).Stretch = Stretch.Uniform;
                icon.Margin = new Thickness(0, 0, 6, 0);
                content.Children.Add(icon);
            }
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return new Button
            {
                Style = (Style)FindResource("PillButtonStyle"),
                Content = content,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 6, 8, 6),
            };
        }

        private static TextBlock Header(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xF5, 0xF5, 0xF5)),
            Margin = new Thickness(10, 6, 0, 4),
        };

        private FrameworkElement MenuSeparator() => new Rectangle
        {
            Height = 1,
            Fill = (Brush)FindResource("SeparatorBrush"),
            Margin = new Thickness(6, 6, 6, 6),
        };

        private Button MenuRow(string glyph, string text, Action action)
        {
            var g = Icons.Glyph(glyph, 14);
            g.Width = 18;
            g.Margin = new Thickness(0, 0, 10, 0);
            g.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xF5, 0xF5, 0xF5));
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(g);
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = (Style)FindResource("MenuRowStyle"), Content = sp };
            b.Click += (_, __) =>
            {
                _menu.IsOpen = false;
                action();
            };
            return b;
        }
    }
}
