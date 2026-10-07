using System;

namespace AlightenInk.Core
{
    /// <summary>HSV ↔ RGB for the custom colour picker. H in degrees [0,360), S and V in [0,1].</summary>
    public static class ColorMath
    {
        public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Clamp(s, 0, 1);
            v = Math.Clamp(v, 0, 1);
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) (r, g, b) = (c, x, 0);
            else if (h < 120) (r, g, b) = (x, c, 0);
            else if (h < 180) (r, g, b) = (0, c, x);
            else if (h < 240) (r, g, b) = (0, x, c);
            else if (h < 300) (r, g, b) = (x, 0, c);
            else (r, g, b) = (c, 0, x);
            return (ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        public static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf));
            double min = Math.Min(rf, Math.Min(gf, bf));
            double d = max - min;
            double h = 0;
            if (d > 1e-12)
            {
                if (max == rf) h = 60 * (((gf - bf) / d) % 6);
                else if (max == gf) h = 60 * ((bf - rf) / d + 2);
                else h = 60 * ((rf - gf) / d + 4);
            }
            if (h < 0) h += 360;
            double s = max <= 0 ? 0 : d / max;
            return (h, s, max);
        }

        private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
    }
}
