using System;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace AlightenInk
{
    /// <summary>Notification-area icon with a dark menu matching the toolbar.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly App _app;
        private readonly WinForms.NotifyIcon _icon;
        private readonly WinForms.ToolStripMenuItem _toolbarItem;
        private readonly WinForms.ToolStripMenuItem _drawItem;
        private bool _disposed;

        public TrayIcon(App app)
        {
            _app = app;
            var menu = new WinForms.ContextMenuStrip
            {
                Renderer = new DarkRenderer(),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Font = new Font("Segoe UI", 9.5f),
                Padding = new WinForms.Padding(4),
            };
            _toolbarItem = new WinForms.ToolStripMenuItem("Show toolbar", null, (_, __) => _app.ShowToolbar(!_app.IsToolbarVisible));
            _drawItem = new WinForms.ToolStripMenuItem("Draw mode", null, (_, __) => _app.ToggleDrawMode()) { ShortcutKeyDisplayString = "Ctrl+Shift+D" };
            menu.Items.Add(_toolbarItem);
            menu.Items.Add(_drawItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(new WinForms.ToolStripMenuItem("Check for updates", null, async (_, __) => await _app.CheckForUpdatesAsync()));
            menu.Items.Add(new WinForms.ToolStripMenuItem("Open screenshots folder", null, (_, __) => _app.OpenScreenshotsFolder()));
            menu.Items.Add(new WinForms.ToolStripMenuItem("About AlightenInk", null, (_, __) => _app.ShowAbout()));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(new WinForms.ToolStripMenuItem("Exit", null, (_, __) => _app.ExitApp()));
            foreach (WinForms.ToolStripItem item in menu.Items)
            {
                item.ForeColor = Color.FromArgb(0xF5, 0xF5, 0xF5);
                item.Padding = new WinForms.Padding(2, 4, 2, 4);
            }
            menu.Opening += (_, __) => Refresh();

            _icon = new WinForms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "AlightenInk",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) _app.ShowToolbar(true);
            };
            _app.StateChanged += (_, __) => Refresh();
            Refresh();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AlightenInk.ico"));
                if (res != null)
                {
                    using var s = res.Stream;
                    return new Icon(s, WinForms.SystemInformation.SmallIconSize);
                }
            }
            catch
            {
                // fall through to the exe icon
            }
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }

        private void Refresh()
        {
            if (_disposed) return;
            _toolbarItem.Text = _app.IsToolbarVisible ? "Hide toolbar" : "Show toolbar";
            _drawItem.Checked = _app.IsDrawMode;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _icon.Visible = false;
            _icon.Dispose();
        }

        private sealed class DarkRenderer : WinForms.ToolStripProfessionalRenderer
        {
            private static readonly Color Bg = Color.FromArgb(0x16, 0x1E, 0x28);
            private static readonly Color Hover = Color.FromArgb(0x2A, 0x33, 0x3E);
            private static readonly Color Line = Color.FromArgb(0x33, 0x3C, 0x48);
            private static readonly Color Pumpkin = Color.FromArgb(0xF0, 0x90, 0x30);

            public DarkRenderer() : base(new DarkColors()) => RoundedEdges = true;

            protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
            {
                var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
                using var b = new SolidBrush(e.Item.Selected ? Hover : Bg);
                e.Graphics.FillRectangle(b, r);
            }

            protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Color.FromArgb(0xF5, 0xF5, 0xF5) : Color.Gray;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = e.ImageRectangle;
                using var pen = new Pen(Pumpkin, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.76f),
                    new PointF(r.Left + r.Width * 0.84f, r.Top + r.Height * 0.28f),
                });
            }

            protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using var p = new Pen(Line);
                e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
            }

            private sealed class DarkColors : WinForms.ProfessionalColorTable
            {
                public override Color ToolStripDropDownBackground => Bg;
                public override Color ImageMarginGradientBegin => Bg;
                public override Color ImageMarginGradientMiddle => Bg;
                public override Color ImageMarginGradientEnd => Bg;
                public override Color MenuBorder => Line;
                public override Color MenuItemBorder => Hover;
                public override Color MenuItemSelected => Hover;
                public override Color CheckBackground => Bg;
                public override Color CheckSelectedBackground => Hover;
                public override Color CheckPressedBackground => Hover;
            }
        }
    }
}
