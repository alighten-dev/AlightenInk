using System;
using System.Collections.Generic;

namespace AlightenInk.Core
{
    public readonly struct Pt : IEquatable<Pt>
    {
        public Pt(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public double DistanceTo(Pt o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
        public bool Equals(Pt o) => X == o.X && Y == o.Y;
        public override bool Equals(object? obj) => obj is Pt p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }

    public enum ShapeKind
    {
        Line,
        Arrow,
        Rectangle,
        Ellipse,
        HorizontalLine
    }

    /// <summary>Pure point generation for the shape tools (drawn as ink strokes by the app).</summary>
    public static class ShapeGeometry
    {
        public const int EllipseSegments = 72;
        public const double ArrowHeadAngleDegrees = 28;

        /// <summary>Snaps the direction start→end to the nearest multiple of 45°, keeping the length.</summary>
        public static Pt Snap45(Pt start, Pt end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return end;
            double step = Math.PI / 4;
            double angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
            double x = start.X + len * Math.Cos(angle);
            double y = start.Y + len * Math.Sin(angle);
            // Kill floating dust so horizontal/vertical results are exact.
            if (Math.Abs(x - start.X) < 1e-9) x = start.X;
            if (Math.Abs(y - start.Y) < 1e-9) y = start.Y;
            return new Pt(x, y);
        }

        /// <summary>Makes the box start→end square (side = larger of |dx|, |dy|), keeping the drag direction.</summary>
        public static Pt SnapSquare(Pt start, Pt end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            double sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
            return new Pt(start.X + sx * side, start.Y + sy * side);
        }

        /// <summary>Arrow head length for a pen width, never more than 45% of the shaft.</summary>
        public static double ArrowHeadLength(double penWidth, double shaftLength)
        {
            double head = Math.Max(14, penWidth * 4.5);
            return Math.Min(head, shaftLength * 0.45);
        }

        /// <summary>
        /// Builds the polyline for a shape. <paramref name="snap"/> is the Shift modifier:
        /// line/arrow → 45°, rectangle → square, ellipse → circle. <paramref name="surfaceWidth"/> is only
        /// used by the horizontal line, which always spans the whole surface at end.Y.
        /// </summary>
        public static IReadOnlyList<Pt> Build(ShapeKind kind, Pt start, Pt end, bool snap, double penWidth, double surfaceWidth)
        {
            switch (kind)
            {
                case ShapeKind.Line:
                    return new[] { start, snap ? Snap45(start, end) : end };
                case ShapeKind.Arrow:
                    return Arrow(start, snap ? Snap45(start, end) : end, penWidth);
                case ShapeKind.Rectangle:
                    return Rectangle(start, snap ? SnapSquare(start, end) : end);
                case ShapeKind.Ellipse:
                    return Ellipse(start, snap ? SnapSquare(start, end) : end, EllipseSegments);
                case ShapeKind.HorizontalLine:
                    return new[] { new Pt(0, end.Y), new Pt(surfaceWidth, end.Y) };
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>Shaft then both barbs as one polyline: start, tip, barbA, tip, barbB.</summary>
        public static Pt[] Arrow(Pt start, Pt tip, double penWidth)
        {
            double dx = tip.X - start.X, dy = tip.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return new[] { start, tip };
            double head = ArrowHeadLength(penWidth, len);
            double back = Math.Atan2(-dy, -dx); // direction pointing from the tip back to the start
            double a = ArrowHeadAngleDegrees * Math.PI / 180;
            var barbA = new Pt(tip.X + head * Math.Cos(back + a), tip.Y + head * Math.Sin(back + a));
            var barbB = new Pt(tip.X + head * Math.Cos(back - a), tip.Y + head * Math.Sin(back - a));
            return new[] { start, tip, barbA, tip, barbB };
        }

        /// <summary>Closed rectangle: 5 points, last == first.</summary>
        public static Pt[] Rectangle(Pt a, Pt b)
        {
            return new[]
            {
                new Pt(a.X, a.Y), new Pt(b.X, a.Y), new Pt(b.X, b.Y), new Pt(a.X, b.Y), new Pt(a.X, a.Y)
            };
        }

        /// <summary>Ellipse inscribed in the box a-b; segments+1 points, last == first (closed).</summary>
        public static Pt[] Ellipse(Pt a, Pt b, int segments)
        {
            if (segments < 8) segments = 8;
            double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;
            double rx = Math.Abs(b.X - a.X) / 2, ry = Math.Abs(b.Y - a.Y) / 2;
            var pts = new Pt[segments + 1];
            for (int i = 0; i < segments; i++)
            {
                double t = 2 * Math.PI * i / segments - Math.PI / 2; // start at the top
                pts[i] = new Pt(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t));
            }
            pts[segments] = pts[0];
            return pts;
        }

        /// <summary>True when a drag is too small to be a deliberate shape (a stray click).</summary>
        public static bool IsTooSmall(ShapeKind kind, Pt start, Pt end)
        {
            if (kind == ShapeKind.HorizontalLine) return false;
            return Math.Abs(end.X - start.X) < 3 && Math.Abs(end.Y - start.Y) < 3;
        }
    }
}
