using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using WinForms = System.Windows.Forms;

namespace AlightenInk
{
    /// <summary>Screen capture (including layered overlay windows), clipboard and PNG output.</summary>
    internal static class Capture
    {
        public static string ScreenshotFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AlightenInk");

        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, int rop);

        /// <summary>
        /// Captures a rectangle in physical screen pixels with SRCCOPY|CAPTUREBLT so layered windows
        /// (our overlays) are included. Graphics.CopyFromScreen rejects the combined flag, hence BitBlt.
        /// </summary>
        public static Bitmap CaptureRect(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dest = g.GetHdc();
                IntPtr src = GetDC(IntPtr.Zero);
                try
                {
                    if (!BitBlt(dest, 0, 0, r.Width, r.Height, src, r.Left, r.Top, SRCCOPY | CAPTUREBLT))
                    {
                        throw new InvalidOperationException("BitBlt failed");
                    }
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, src);
                    g.ReleaseHdc(dest);
                }
            }
            // BitBlt leaves alpha at 0 in a 32bpp ARGB target on some drivers; force opaque.
            return MakeOpaque(bmp);
        }

        private static Bitmap MakeOpaque(Bitmap src)
        {
            var opaque = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(opaque)) g.DrawImageUnscaled(src, 0, 0);
            src.Dispose();
            var result = new Bitmap(opaque.Width, opaque.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result)) g.DrawImageUnscaled(opaque, 0, 0);
            opaque.Dispose();
            return result;
        }

        public static Bitmap Crop(Bitmap source, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, source.Width, source.Height));
            if (r.Width <= 0 || r.Height <= 0) r = new Rectangle(0, 0, source.Width, source.Height);
            return source.Clone(r, PixelFormat.Format32bppArgb);
        }

        public static string SavePng(Bitmap bmp, string folder)
        {
            Directory.CreateDirectory(folder);
            string name = "AlightenInk_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(folder, name + ".png");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name}_{i}.png");
            bmp.Save(path, ImageFormat.Png);
            return path;
        }

        /// <summary>Puts the image on the clipboard as both a bitmap and PNG (for chat apps). Retries if busy.</summary>
        public static bool ToClipboard(Bitmap bmp)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    var data = new WinForms.DataObject();
                    data.SetData(WinForms.DataFormats.Bitmap, true, bmp);
                    var png = new MemoryStream();
                    bmp.Save(png, ImageFormat.Png);
                    png.Position = 0;
                    data.SetData("PNG", false, png);
                    WinForms.Clipboard.SetDataObject(data, true, 3, 60);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    Thread.Sleep(80);
                }
            }
            return false;
        }
    }
}
