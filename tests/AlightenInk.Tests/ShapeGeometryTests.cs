using System;
using System.Linq;
using AlightenInk.Core;
using Xunit;

namespace AlightenInk.Tests
{
    public class ShapeGeometryTests
    {
        private const double Eps = 1e-6;

        private static void Near(double expected, double actual, double eps = Eps) =>
            Assert.True(Math.Abs(expected - actual) < eps, $"expected {expected}, got {actual}");

        [Theory]
        [InlineData(100, 10, 0)]     // nearly horizontal → horizontal
        [InlineData(10, 100, 90)]    // nearly vertical → vertical
        [InlineData(100, 90, 45)]    // near diagonal → 45°
        [InlineData(-100, 95, 135)]
        [InlineData(-100, -5, 180)]
        [InlineData(5, -100, -90)]
        public void Snap45_SnapsAngleAndKeepsLength(double dx, double dy, double expectedDeg)
        {
            var s = new Pt(50, 50);
            var e = new Pt(50 + dx, 50 + dy);
            var r = ShapeGeometry.Snap45(s, e);
            double angle = Math.Atan2(r.Y - s.Y, r.X - s.X) * 180 / Math.PI;
            Near(expectedDeg, angle, 1e-6);
            Near(s.DistanceTo(e), s.DistanceTo(r), 1e-6);
        }

        [Fact]
        public void Snap45_HorizontalIsExact()
        {
            var r = ShapeGeometry.Snap45(new Pt(10, 20), new Pt(110, 23));
            Assert.Equal(20, r.Y);
        }

        [Fact]
        public void Snap45_ZeroLength_ReturnsEnd()
        {
            var p = new Pt(5, 5);
            Assert.Equal(p, ShapeGeometry.Snap45(p, p));
        }

        [Theory]
        [InlineData(100, 40, 100, 100)]
        [InlineData(-30, 80, -80, 80)]
        [InlineData(-50, -20, -50, -50)]
        public void SnapSquare_UsesLargerSideAndKeepsDirection(double dx, double dy, double ex, double ey)
        {
            var r = ShapeGeometry.SnapSquare(new Pt(0, 0), new Pt(dx, dy));
            Assert.Equal(ex, r.X);
            Assert.Equal(ey, r.Y);
        }

        [Fact]
        public void Rectangle_IsClosedFivePoints()
        {
            var pts = ShapeGeometry.Build(ShapeKind.Rectangle, new Pt(10, 10), new Pt(60, 30), false, 5, 1000);
            Assert.Equal(5, pts.Count);
            Assert.Equal(pts[0], pts[4]);
            Assert.Contains(new Pt(60, 30), pts);
        }

        [Fact]
        public void Rectangle_ShiftMakesSquare()
        {
            var pts = ShapeGeometry.Build(ShapeKind.Rectangle, new Pt(0, 0), new Pt(80, 30), true, 5, 1000);
            double w = pts.Max(p => p.X) - pts.Min(p => p.X);
            double h = pts.Max(p => p.Y) - pts.Min(p => p.Y);
            Assert.Equal(w, h);
            Assert.Equal(80, w);
        }

        [Fact]
        public void Ellipse_IsClosedAndInscribed()
        {
            var pts = ShapeGeometry.Build(ShapeKind.Ellipse, new Pt(0, 0), new Pt(200, 100), false, 5, 1000);
            Assert.Equal(ShapeGeometry.EllipseSegments + 1, pts.Count);
            Assert.Equal(pts[0], pts[pts.Count - 1]);
            foreach (var p in pts)
            {
                double nx = (p.X - 100) / 100, ny = (p.Y - 50) / 50;
                Near(1, nx * nx + ny * ny, 1e-9);
            }
            Near(0, pts.Min(p => p.X), 1e-9);
            Near(200, pts.Max(p => p.X), 1e-9);
            Near(0, pts.Min(p => p.Y), 1e-9);
            Near(100, pts.Max(p => p.Y), 1e-9);
        }

        [Fact]
        public void Ellipse_ShiftMakesCircle()
        {
            var pts = ShapeGeometry.Build(ShapeKind.Ellipse, new Pt(0, 0), new Pt(120, 40), true, 5, 1000);
            var c = new Pt(60, 60);
            foreach (var p in pts) Near(60, c.DistanceTo(p), 1e-9);
        }

        [Fact]
        public void Ellipse_DraggedBackwards_StillClosed()
        {
            var pts = ShapeGeometry.Ellipse(new Pt(100, 100), new Pt(0, 0), 16);
            Assert.Equal(17, pts.Length);
            Assert.Equal(pts[0], pts[16]);
        }

        [Fact]
        public void Arrow_HeadIsSymmetricAtTheTip()
        {
            var start = new Pt(0, 0);
            var tip = new Pt(200, 0);
            var pts = ShapeGeometry.Arrow(start, tip, 5);
            Assert.Equal(5, pts.Length);
            Assert.Equal(start, pts[0]);
            Assert.Equal(tip, pts[1]);
            Assert.Equal(tip, pts[3]);
            var a = pts[2];
            var b = pts[4];
            // Barbs sit behind the tip, mirrored across the shaft.
            Assert.True(a.X < tip.X && b.X < tip.X);
            Near(a.X, b.X);
            Near(-a.Y, b.Y);
            double head = ShapeGeometry.ArrowHeadLength(5, 200);
            Near(head, tip.DistanceTo(a));
            Near(head, tip.DistanceTo(b));
            // Each barb makes the configured angle with the shaft.
            double angle = Math.Atan2(Math.Abs(a.Y), tip.X - a.X) * 180 / Math.PI;
            Near(ShapeGeometry.ArrowHeadAngleDegrees, angle, 1e-9);
        }

        [Fact]
        public void ArrowHead_ScalesWithWidthButNeverExceedsShaftShare()
        {
            Assert.Equal(14, ShapeGeometry.ArrowHeadLength(2, 1000));
            Assert.Equal(9 * 4.5, ShapeGeometry.ArrowHeadLength(9, 1000));
            Assert.Equal(20 * 0.45, ShapeGeometry.ArrowHeadLength(9, 20));
        }

        [Fact]
        public void Arrow_Shift_SnapsShaft()
        {
            var pts = ShapeGeometry.Build(ShapeKind.Arrow, new Pt(0, 0), new Pt(100, 7), true, 5, 1000);
            Assert.Equal(0, pts[1].Y);
        }

        [Fact]
        public void HorizontalLine_SpansSurfaceAtCursorY()
        {
            var pts = ShapeGeometry.Build(ShapeKind.HorizontalLine, new Pt(300, 100), new Pt(320, 250), false, 5, 1920);
            Assert.Equal(new[] { new Pt(0, 250), new Pt(1920, 250) }, pts);
        }

        [Fact]
        public void Line_NoSnap_IsVerbatim()
        {
            var pts = ShapeGeometry.Build(ShapeKind.Line, new Pt(1, 2), new Pt(30, 47), false, 5, 1000);
            Assert.Equal(new[] { new Pt(1, 2), new Pt(30, 47) }, pts);
        }

        [Fact]
        public void IsTooSmall_ClickVsDrag()
        {
            Assert.True(ShapeGeometry.IsTooSmall(ShapeKind.Rectangle, new Pt(0, 0), new Pt(1, 2)));
            Assert.False(ShapeGeometry.IsTooSmall(ShapeKind.Rectangle, new Pt(0, 0), new Pt(10, 2)));
            Assert.False(ShapeGeometry.IsTooSmall(ShapeKind.HorizontalLine, new Pt(0, 0), new Pt(0, 0)));
        }
    }
}
