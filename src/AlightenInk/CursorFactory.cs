using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlightenInk
{
    /// <summary>
    /// Builds drawing cursors at runtime with a dual-tone outline (dark outer, light inner) so they
    /// read on a whiteboard, a blackboard and any chart. Pen/highlighter/eraser cursors are sized to
    /// the brush (a live brush preview). Rendered at the monitor's physical pixel size for crisp
    /// high-DPI output, and packed as a PNG-compressed .cur in memory.
    /// </summary>
    internal static class CursorFactory
    {
        private static readonly Dictionary<string, Cursor> Cache = new Dictionary<string, Cursor>();
        private static readonly Color Dark = Color.FromArgb(0xE6, 0x0B, 0x11, 0x1A);
        private static readonly Color Light = Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5);

        public static Cursor For(Tool tool, Color color, double penWidth, double textSize, double eraserDiameter, double dpiScale)
        {
            string key = $"{tool}|{color}|{penWidth:0.##}|{textSize:0.##}|{eraserDiameter:0.##}|{dpiScale:0.###}";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            Cursor cursor;
            try
            {
                cursor = Build(tool, color, penWidth, textSize, eraserDiameter, dpiScale);
            }
            catch (Exception ex)
            {
                App.Log("Cursor build failed: " + ex.Message);
                cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            }
            if (Cache.Count > 64) Cache.Clear();
            Cache[key] = cursor;
            return cursor;
        }

        private static Cursor Build(Tool tool, Color color, double penWidth, double textSize, double eraserD, double s)
        {
            // Canvas size in DIPs, big enough for the brush plus the outline.
            double brushW, brushH;
            switch (tool)
            {
                case Tool.Pen:
                    brushW = brushH = Math.Max(penWidth, 5);
                    break;
                case Tool.Highlighter:
                    brushW = Math.Max(penWidth * 2.2, 5);
                    brushH = Math.Max(penWidth * 5.5, 10);
                    break;
                case Tool.Eraser:
                    brushW = brushH = eraserD;
                    break;
                case Tool.Text:
                    brushW = textSize * 0.5;
                    brushH = textSize * 1.05;
                    break;
                default:
                    brushW = brushH = 23;
                    break;
            }
            double sizeDip = Math.Ceiling(Math.Max(brushW, brushH) + 10);
            int px = (int)Math.Ceiling(sizeDip * s);
            if (px % 2 == 1) px++;
            px = Math.Min(px, 256);
            double c = px / 2.0 / s; // centre in DIPs

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var darkPen = new Pen(new SolidColorBrush(Dark), 4.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var lightPen = new Pen(new SolidColorBrush(Light), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var centre = new Point(c, c);
                switch (tool)
                {
                    case Tool.Pen:
                    {
                        double r = brushW / 2;
                        dc.DrawEllipse(new SolidColorBrush(color), null, centre, r, r);
                        dc.DrawEllipse(null, darkPen, centre, r + 2.1, r + 2.1);
                        dc.DrawEllipse(null, lightPen, centre, r + 2.1, r + 2.1);
                        break;
                    }
                    case Tool.Highlighter:
                    {
                        var rect = new Rect(c - brushW / 2, c - brushH / 2, brushW, brushH);
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B)), null, rect, 1.5, 1.5);
                        rect.Inflate(2.1, 2.1);
                        dc.DrawRoundedRectangle(null, darkPen, rect, 2.5, 2.5);
                        dc.DrawRoundedRectangle(null, lightPen, rect, 2.5, 2.5);
                        break;
                    }
                    case Tool.Eraser:
                    {
                        double r = brushW / 2;
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0xF5, 0xF5, 0xF5)), null, centre, r, r);
                        dc.DrawEllipse(null, darkPen, centre, r, r);
                        dc.DrawEllipse(null, lightPen, centre, r, r);
                        DrawCross(dc, c, 2.5, darkPen, lightPen);
                        break;
                    }
                    case Tool.Text:
                    {
                        double h = brushH, serif = Math.Max(3, textSize * 0.16);
                        var geo = new PathGeometry();
                        var top = new Point(c, c - h / 2);
                        var bottom = new Point(c, c + h / 2);
                        AddLine(geo, top, bottom);
                        AddLine(geo, new Point(c - serif, top.Y), new Point(c + serif, top.Y));
                        AddLine(geo, new Point(c - serif, bottom.Y), new Point(c + serif, bottom.Y));
                        dc.DrawGeometry(null, darkPen, geo);
                        dc.DrawGeometry(null, lightPen, geo);
                        break;
                    }
                    default:
                    {
                        // Crosshair with an open centre and a dot in the ink colour.
                        double gap = 3.5, arm = 10.5;
                        var geo = new PathGeometry();
                        AddLine(geo, new Point(c - arm, c), new Point(c - gap, c));
                        AddLine(geo, new Point(c + gap, c), new Point(c + arm, c));
                        AddLine(geo, new Point(c, c - arm), new Point(c, c - gap));
                        AddLine(geo, new Point(c, c + gap), new Point(c, c + arm));
                        dc.DrawGeometry(null, darkPen, geo);
                        dc.DrawGeometry(null, lightPen, geo);
                        dc.DrawEllipse(new SolidColorBrush(Dark), null, centre, 2.1, 2.1);
                        dc.DrawEllipse(new SolidColorBrush(color), null, centre, 1.3, 1.3);
                        break;
                    }
                }
            }

            var rtb = new RenderTargetBitmap(px, px, 96 * s, 96 * s, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var enc = new PngBitmapEncoder();
            // PNG wants straight alpha; RenderTargetBitmap is premultiplied (translucent fills go dark otherwise).
            enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0)));
            byte[] png;
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                png = ms.ToArray();
            }
            if (App.DebugLog)
            {
                try { File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"alighten_cursor_{tool}.png"), png); } catch { }
            }
            int hot = px / 2;
            return new Cursor(new MemoryStream(BuildCur(png, px, hot, hot)), true);
        }

        private static void DrawCross(DrawingContext dc, double c, double arm, Pen dark, Pen light)
        {
            var geo = new PathGeometry();
            AddLine(geo, new Point(c - arm, c), new Point(c + arm, c));
            AddLine(geo, new Point(c, c - arm), new Point(c, c + arm));
            dc.DrawGeometry(null, dark, geo);
            dc.DrawGeometry(null, light, geo);
        }

        private static void AddLine(PathGeometry geo, Point a, Point b)
        {
            var fig = new PathFigure { StartPoint = a, IsClosed = false };
            fig.Segments.Add(new LineSegment(b, true));
            geo.Figures.Add(fig);
        }

        /// <summary>Wraps a PNG in a single-image .cur container (supported since Windows Vista).</summary>
        internal static byte[] BuildCur(byte[] png, int size, int hotX, int hotY)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((ushort)0);      // reserved
            w.Write((ushort)2);      // type: cursor
            w.Write((ushort)1);      // image count
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);        // palette
            w.Write((byte)0);        // reserved
            w.Write((ushort)hotX);
            w.Write((ushort)hotY);
            w.Write((uint)png.Length);
            w.Write((uint)22);       // data offset
            w.Write(png);
            w.Flush();
            return ms.ToArray();
        }
    }
}
