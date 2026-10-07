using System.Collections.Generic;

namespace AlightenInk.Core
{
    /// <summary>An axis-aligned rectangle in physical screen pixels (right/bottom exclusive).</summary>
    public readonly struct PixelRect
    {
        public PixelRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
        public bool IsEmpty => Right <= Left || Bottom <= Top;

        public bool Contains(int x, int y) => !IsEmpty && x >= Left && x < Right && y >= Top && y < Bottom;
    }

    /// <summary>
    /// Decides whether a press that reached an overlay must be ignored because it is over the
    /// toolbar (or one of its popups). The toolbar is kept above the overlays, but a press that
    /// still lands on an overlay there must never start ink or open a text box.
    /// </summary>
    public static class InputGuard
    {
        public static bool IsOverToolbar(int x, int y, IEnumerable<PixelRect> toolbarRects)
        {
            foreach (var r in toolbarRects)
            {
                if (r.Contains(x, y)) return true;
            }
            return false;
        }
    }
}
