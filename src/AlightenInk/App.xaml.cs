using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AlightenInk.Core;
using WinForms = System.Windows.Forms;

namespace AlightenInk
{
    /// <summary>The application controller: owns state, overlays, toolbar, tray and hotkeys.</summary>
    public partial class App : Application
    {
        public static readonly double[] PenWidths = { 2.5, 5, 9 };
        public static readonly double[] TextSizes = { 20, 28, 40 };
        public static readonly double[] EraserSizes = { 18, 30, 48 };

        private readonly EventWaitHandle _showEvent;
        private readonly SettingsStore _store = new SettingsStore(SettingsStore.DefaultPath);
        private readonly List<OverlayWindow> _overlays = new List<OverlayWindow>();
        private readonly Dictionary<object, FadeEntry> _fading = new Dictionary<object, FadeEntry>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private DispatcherTimer? _fadeTimer;
        private DispatcherTimer? _saveTimer;
        private DispatcherTimer? _zOrderTimer;
        private ToolbarWindow? _toolbar;
        private TrayIcon? _tray;
        private HotkeyManager? _hotkeys;
        private IntPtr _previousForeground;
        private bool _busyCapturing;
        private bool _exiting;

        internal App(EventWaitHandle showEvent)
        {
            _showEvent = showEvent;
        }

        public AppSettings Settings { get; private set; } = new AppSettings();
        public UndoManager<object> Undo { get; } = new UndoManager<object>();
        public Tool CurrentTool { get; private set; } = Tool.Cursor;
        public Tool LastDrawTool { get; private set; } = Tool.Pen;
        public BoardMode Board { get; private set; }
        public bool IsDrawMode => CurrentTool != Tool.Cursor;

        public Color InkColor => (Color)ColorConverter.ConvertFromString(Settings.Color);
        public double PenWidth => PenWidths[Settings.WidthIndex];
        public double TextSize => TextSizes[Settings.WidthIndex];
        public double EraserDiameter => EraserSizes[Settings.WidthIndex];

        internal HotkeyManager? Hotkeys => _hotkeys;

        /// <summary>Raised whenever anything the toolbar/tray displays changes.</summary>
        public event EventHandler? StateChanged;

        private sealed class FadeEntry
        {
            public FadeEntry(OverlayWindow host, double start, double hold, byte baseAlpha)
            {
                Host = host;
                Start = start;
                Hold = hold;
                BaseAlpha = baseAlpha;
            }

            public OverlayWindow Host { get; }
            public double Start { get; }
            public double Hold { get; }
            public byte BaseAlpha { get; }
        }

        // ================================================================ startup / shutdown

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) =>
            {
                Log("Unhandled: " + args.Exception);
                args.Handled = true;
            };

            Settings = _store.Load();
            Undo.Changed += (_, __) => RaiseState();

            BuildOverlays();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _hotkeys = new HotkeyManager();
            _hotkeys.Pressed += OnHotkey;
            _hotkeys.RegisterDefaults();
            if (_hotkeys.Failed.Count > 0)
            {
                Log("Hotkeys unavailable: " + string.Join(", ", _hotkeys.Failed.Select(f => DefaultHotkeys.Bindings[f])));
            }

            _toolbar = new ToolbarWindow(this);
            _toolbar.Show();
            if (_hotkeys.Failed.Count > 0)
            {
                Toast(_hotkeys.Failed.Count + " hotkey(s) in use by another app — see tooltips");
            }

            _tray = new TrayIcon(this);

            _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _zOrderTimer.Tick += (_, __) => { if (IsDrawMode || Board != BoardMode.None) _toolbar?.BringToFront(); };
            _zOrderTimer.Start();

            _fadeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
            _fadeTimer.Tick += (_, __) => FadeTick();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, __) =>
            {
                _saveTimer.Stop();
                _store.Save(Settings);
            };

            // Second launch → show our toolbar.
            var waiter = new Thread(() =>
            {
                try
                {
                    while (_showEvent.WaitOne())
                    {
                        if (_exiting) return;
                        Dispatcher.BeginInvoke(new Action(() => ShowToolbar(true)));
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            })
            { IsBackground = true, Name = "AlightenInk show-event" };
            waiter.Start();

            // Quiet background update check (installed builds only; failures are silent).
            var updateDelay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            updateDelay.Tick += async (_, __) =>
            {
                updateDelay.Stop();
                await Updater.CheckAsync(manual: false, Toast);
            };
            updateDelay.Start();

            RaiseState();
        }

        public void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            try
            {
                foreach (var o in _overlays) o.CommitText();
                _store.Save(Settings);
                _hotkeys?.Dispose();
                _tray?.Dispose();
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                _showEvent.Set(); // release the waiter thread
            }
            catch (Exception ex)
            {
                Log("Exit: " + ex.Message);
            }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }

        // ================================================================ overlays

        private void BuildOverlays()
        {
            var screens = WinForms.Screen.AllScreens;
            // Drop overlays whose monitor disappeared.
            foreach (var o in _overlays.Where(o => screens.All(s => s.DeviceName != o.DeviceName)).ToList())
            {
                foreach (var item in o.Items) { _fading.Remove(item); Undo.Forget(item); }
                _overlays.Remove(o);
                o.Close();
            }
            foreach (var s in screens)
            {
                var existing = _overlays.FirstOrDefault(o => o.DeviceName == s.DeviceName);
                if (existing != null)
                {
                    existing.UpdateScreen(s);
                    continue;
                }
                var ov = new OverlayWindow(this, s);
                ov.Show();
                ov.SetTool(CurrentTool);
                ov.SetDrawMode(IsDrawMode);
                ov.SetBoard(Board);
                _overlays.Add(ov);
            }
            KeepToolbarOnTop();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BuildOverlays();
                _toolbar?.EnsureOnScreen();
            }));
        }

        private OverlayWindow? OverlayUnderCursor()
        {
            var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
            return _overlays.FirstOrDefault(o => o.DeviceName == screen.DeviceName) ?? _overlays.FirstOrDefault();
        }

        /// <summary>True when the mouse is over the toolbar or one of its popups (physical pixels).</summary>
        internal bool IsCursorOverToolbar()
        {
            if (_toolbar == null || !_toolbar.IsVisible) return false;
            Native.GetCursorPos(out var p);
            return InputGuard.IsOverToolbar(p.X, p.Y, _toolbar.ScreenRects());
        }

        /// <summary>Re-asserts the toolbar above the overlays (activation raises an overlay over it).</summary>
        internal void KeepToolbarOnTop()
        {
            _toolbar?.BringToFront();
            // Activation z-order changes can land after this call; repeat once the dust settles.
            Dispatcher.BeginInvoke(new Action(() => _toolbar?.BringToFront()), DispatcherPriority.Background);
        }

        /// <summary>Commits (or drops, if empty) any open text box on every monitor.</summary>
        internal void CommitAllText()
        {
            foreach (var o in _overlays) o.CommitText();
        }

        internal bool IsEditingText => _overlays.Any(o => o.IsEditingText);

        internal bool IsOwnWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        // ================================================================ tools & modes

        public void SelectTool(Tool tool)
        {
            if (tool == Tool.Cursor)
            {
                ExitDrawMode();
                return;
            }
            bool wasDrawing = IsDrawMode;
            if (!wasDrawing)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (!IsOwnWindow(fg)) _previousForeground = fg;
            }
            CurrentTool = tool;
            LastDrawTool = tool;
            foreach (var o in _overlays)
            {
                o.SetTool(tool);
                o.SetDrawMode(true);
            }
            if (!wasDrawing) OverlayUnderCursor()?.ActivateForDrawing();
            KeepToolbarOnTop();
            RaiseState();
        }

        public void ExitDrawMode()
        {
            if (!IsDrawMode) return;
            if (DebugLog) Log("ExitDrawMode " + Environment.StackTrace);
            CurrentTool = Tool.Cursor;
            foreach (var o in _overlays)
            {
                o.SetTool(Tool.Cursor);
                o.SetDrawMode(false);
            }
            if (_previousForeground != IntPtr.Zero && Native.IsWindow(_previousForeground))
            {
                Native.ForceForeground(_previousForeground);
            }
            _previousForeground = IntPtr.Zero;
            KeepToolbarOnTop();
            RaiseState();
        }

        public void ToggleDrawMode()
        {
            if (IsDrawMode) ExitDrawMode();
            else SelectTool(LastDrawTool);
        }

        public void SetColor(string color)
        {
            string? n = ColorText.Normalize(color);
            if (n == null) return;
            Settings.Color = n;
            foreach (var o in _overlays) o.UpdateDrawingAttributes();
            SaveSoon();
            RaiseState();
        }

        public void AddCustomColor(string color)
        {
            string? n = ColorText.Normalize(color);
            if (n == null) return;
            if (!Settings.AllSwatches.Contains(n, StringComparer.OrdinalIgnoreCase))
            {
                Settings.CustomSwatches.Add(n);
                if (Settings.CustomSwatches.Count > 24) Settings.CustomSwatches.RemoveAt(0);
            }
            SetColor(n);
        }

        public void RemoveCustomColor(string color)
        {
            Settings.CustomSwatches.RemoveAll(c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(Settings.Color, color, StringComparison.OrdinalIgnoreCase)) Settings.Color = AppSettings.DefaultSwatches[0];
            foreach (var o in _overlays) o.UpdateDrawingAttributes();
            SaveSoon();
            RaiseState();
        }

        public void SetWidth(int index)
        {
            if (index < 0 || index > 2) return;
            Settings.WidthIndex = index;
            foreach (var o in _overlays) o.UpdateDrawingAttributes();
            SaveSoon();
            RaiseState();
        }

        public void ToggleFade()
        {
            Settings.FadeEnabled = !Settings.FadeEnabled;
            Toast(Settings.FadeEnabled ? $"Fading ink on · {Settings.FadeSeconds:0}s" : "Fading ink off");
            SaveSoon();
            RaiseState();
        }

        public void SetFadeSeconds(double seconds)
        {
            Settings.FadeSeconds = seconds;
            SaveSoon();
            RaiseState();
        }

        public void CycleFadeSeconds()
        {
            SetFadeSeconds(FadeMath.NextHold(Settings.FadeSeconds, AppSettings.FadeChoices));
            Toast($"Fade after {Settings.FadeSeconds:0}s");
        }

        public void CycleBoard()
        {
            var next = Board switch
            {
                BoardMode.None => BoardMode.White,
                BoardMode.White => BoardMode.Black,
                _ => BoardMode.None
            };
            SetBoard(next);
        }

        public void SetBoard(BoardMode mode)
        {
            Board = mode;
            foreach (var o in _overlays) o.SetBoard(mode);
            // A board hides the desktop, so clicks must not fall through to hidden apps.
            if (mode != BoardMode.None && !IsDrawMode) SelectTool(LastDrawTool);
            Toast(mode switch { BoardMode.White => "Whiteboard", BoardMode.Black => "Blackboard", _ => "Board off" });
            RaiseState();
        }

        public void SetLayout(ToolbarLayout layout)
        {
            Settings.Layout = layout;
            SaveSoon();
            RaiseState();
        }

        public void SetCollapsed(bool collapsed)
        {
            Settings.Collapsed = collapsed;
            SaveSoon();
        }

        public void SaveToolbarPosition(double x, double y)
        {
            Settings.ToolbarX = x;
            Settings.ToolbarY = y;
            SaveSoon();
        }

        private void SaveSoon()
        {
            if (_saveTimer == null) { _store.Save(Settings); return; }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);

        // ================================================================ items, undo, fade

        internal void OnItemCreated(OverlayWindow host, object item)
        {
            Undo.RecordAdd(host, item);
            if (Settings.FadeEnabled)
            {
                byte alpha = item is Stroke s ? s.DrawingAttributes.Color.A : (byte)255;
                _fading[item] = new FadeEntry(host, _clock.Elapsed.TotalSeconds, Settings.FadeSeconds, alpha);
                if (_fadeTimer != null && !_fadeTimer.IsEnabled) _fadeTimer.Start();
            }
        }

        internal void OnItemErased(OverlayWindow host, object item)
        {
            Undo.RecordRemove(host, item);
        }

        private void FadeTick()
        {
            double now = _clock.Elapsed.TotalSeconds;
            foreach (var kv in _fading.ToList())
            {
                var item = kv.Key;
                var f = kv.Value;
                double age = now - f.Start;
                if (FadeMath.IsExpired(age, f.Hold))
                {
                    _fading.Remove(item);
                    f.Host.RemoveItem(item);
                    Undo.Forget(item); // gone for good: undo/redo must never resurrect it
                    continue;
                }
                double opacity = FadeMath.Opacity(age, f.Hold);
                switch (item)
                {
                    case Stroke s:
                        var c = s.DrawingAttributes.Color;
                        byte a = (byte)Math.Round(f.BaseAlpha * opacity);
                        if (c.A != a) s.DrawingAttributes.Color = Color.FromArgb(a, c.R, c.G, c.B);
                        break;
                    case UIElement el:
                        el.Opacity = opacity;
                        break;
                }
            }
            if (_fading.Count == 0) _fadeTimer?.Stop();
        }

        public void DoUndo()
        {
            foreach (var o in _overlays) o.CommitText();
            Undo.Undo();
        }

        public void DoRedo() => Undo.Redo();

        public void ClearAll()
        {
            using (Undo.BeginGroup())
            {
                foreach (var o in _overlays)
                {
                    o.CancelText();
                    foreach (var item in o.Items)
                    {
                        o.RemoveItem(item);
                        Undo.RecordRemove(o, item);
                    }
                }
            }
        }

        // ================================================================ keys

        /// <summary>Single-key shortcuts while an overlay has focus (draw mode).</summary>
        internal bool HandleLocalKey(Key key, ModifierKeys mods)
        {
            if (DebugLog) Log($"LocalKey {key} {mods}");
            if (mods == ModifierKeys.Control)
            {
                if (key == Key.Z) { DoUndo(); return true; }
                if (key == Key.Y) { DoRedo(); return true; }
                return false;
            }
            if (mods != ModifierKeys.None) return false;
            switch (key)
            {
                case Key.P: SelectTool(Tool.Pen); return true;
                case Key.H: SelectTool(Tool.Highlighter); return true;
                case Key.E: SelectTool(Tool.Eraser); return true;
                case Key.L: SelectTool(Tool.Line); return true;
                case Key.A: SelectTool(Tool.Arrow); return true;
                case Key.R: SelectTool(Tool.Rectangle); return true;
                case Key.O: SelectTool(Tool.Ellipse); return true;
                case Key.I: SelectTool(Tool.HorizontalLine); return true;
                case Key.T: SelectTool(Tool.Text); return true;
                case Key.D1: case Key.NumPad1: SetWidth(0); return true;
                case Key.D2: case Key.NumPad2: SetWidth(1); return true;
                case Key.D3: case Key.NumPad3: SetWidth(2); return true;
                case Key.Escape: ClearAll(); return true;
            }
            return false;
        }

        private void OnHotkey(object? sender, HotkeyCommand cmd)
        {
            if (DebugLog) Log("Hotkey " + cmd);
            switch (cmd)
            {
                case HotkeyCommand.ToggleDraw: ToggleDrawMode(); break;
                case HotkeyCommand.ToggleFade: ToggleFade(); break;
                case HotkeyCommand.CopyRegion: _ = CopyRegionAsync(); break;
                case HotkeyCommand.Screenshot: _ = ScreenshotAsync(); break;
                case HotkeyCommand.Undo: DoUndo(); break;
                case HotkeyCommand.Redo: DoRedo(); break;
                case HotkeyCommand.Clear: ClearAll(); break;
                case HotkeyCommand.Board: CycleBoard(); break;
            }
        }

        // ================================================================ capture

        public async Task CopyRegionAsync()
        {
            if (_busyCapturing) return;
            _busyCapturing = true;
            try
            {
                foreach (var o in _overlays) o.CommitText();
                var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
                System.Drawing.Bitmap frozen;
                await HideToolbarForCaptureAsync();
                try
                {
                    frozen = Capture.CaptureRect(screen.Bounds);
                }
                finally
                {
                    _toolbar?.Hide();
                }

                var picker = new RegionSelectWindow(frozen, screen.Bounds);
                var rect = await picker.PickAsync();
                _toolbar?.ShowAfterCapture();
                using (frozen)
                {
                    if (rect == null)
                    {
                        Toast("Copy cancelled");
                        return;
                    }
                    using var crop = Capture.Crop(frozen, rect.Value);
                    bool ok = Capture.ToClipboard(crop);
                    Toast(ok ? "Copied ✓" : "Clipboard busy — try again", ok);
                }
            }
            catch (Exception ex)
            {
                Log("Copy region: " + ex);
                _toolbar?.ShowAfterCapture();
                Toast("Copy failed");
            }
            finally
            {
                _busyCapturing = false;
            }
        }

        public async Task ScreenshotAsync()
        {
            if (_busyCapturing) return;
            _busyCapturing = true;
            try
            {
                foreach (var o in _overlays) o.CommitText();
                var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
                await HideToolbarForCaptureAsync();
                System.Drawing.Bitmap bmp;
                try
                {
                    bmp = Capture.CaptureRect(screen.Bounds);
                }
                finally
                {
                    _toolbar?.ShowAfterCapture();
                }
                using (bmp)
                {
                    string path = Capture.SavePng(bmp, Capture.ScreenshotFolder);
                    bool ok = Capture.ToClipboard(bmp);
                    Toast(ok ? "Saved ✓  " + System.IO.Path.GetFileName(path) : "Saved ✓ (clipboard busy)", true);
                }
            }
            catch (Exception ex)
            {
                Log("Screenshot: " + ex);
                _toolbar?.ShowAfterCapture();
                Toast("Screenshot failed");
            }
            finally
            {
                _busyCapturing = false;
            }
        }

        private async Task HideToolbarForCaptureAsync()
        {
            _toolbar?.HideForCapture();
            // Give DWM a couple of frames to drop the toolbar from the composed desktop.
            await Task.Delay(140);
        }

        public void OpenScreenshotsFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Capture.ScreenshotFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Capture.ScreenshotFolder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("Open folder: " + ex.Message);
            }
        }

        // ================================================================ misc UI

        public void Toast(string message) => Toast(message, false);

        public void Toast(string message, bool success) => _toolbar?.ShowToast(message, success);

        public bool IsToolbarVisible => _toolbar?.IsVisible == true;

        public void ShowToolbar(bool show)
        {
            if (_toolbar == null) return;
            if (show)
            {
                _toolbar.Show();
                _toolbar.EnsureOnScreen();
                _toolbar.BringToFront();
            }
            else
            {
                _toolbar.Hide();
            }
            RaiseState();
        }

        public void ShowAbout()
        {
            var about = new AboutWindow(this);
            about.Show();
            about.Activate();
        }

        public Task CheckForUpdatesAsync() => Updater.CheckAsync(manual: true, Toast);

        internal static readonly bool DebugLog = Environment.GetEnvironmentVariable("ALIGHTENINK_DEBUG") == "1";

        internal static void Log(string text)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(SettingsStore.DefaultPath)!;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + Environment.NewLine);
            }
            catch
            {
                // logging must never take the app down
            }
        }
    }
}
