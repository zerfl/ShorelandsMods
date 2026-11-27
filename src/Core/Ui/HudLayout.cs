using System;
using System.Collections.Generic;

namespace ShorelandsMods.Core.Ui
{
    // One HUD widget for the layout: its anchor, size, and either a position chosen by the author or the player
    // (inward distances from the anchor's edges) or none, in which case the host stacks it.
    internal sealed class HudSlot
    {
        public Anchor Anchor;
        public int W,
            H;
        public int? X,
            Y;
        public URect Placed;
    }

    // Where HUD widgets go. Widgets with a position are placed there (kept on the canvas). The others stack from
    // their anchor in list order (load order, then creation order): top and middle rows downwards, the bottom row
    // upwards, each pushed past the game's HUD and past every widget placed before it, so none overlap by default.
    // A widget that no longer fits in its column starts a new one further in; only when no column has room does it
    // overlap.
    internal static class HudLayout
    {
        public const int Margin = 4,
            Gap = 2;

        public static void Place(IList<HudSlot> slots, int canvasW, int canvasH, IList<URect> obstacles)
        {
            var placed = new List<URect>(obstacles);
            foreach (var s in slots)
                if (s.X.HasValue || s.Y.HasValue)
                {
                    s.Placed = Clamp(
                        At(s.Anchor, s.W, s.H, s.X ?? Margin, s.Y ?? Margin, canvasW, canvasH),
                        canvasW,
                        canvasH
                    );
                    placed.Add(s.Placed);
                }
            var cursor = new Dictionary<Anchor, int>();
            foreach (var s in slots)
            {
                if (s.X.HasValue || s.Y.HasValue)
                    continue;
                int row = Anchors.Row(s.Anchor);
                if (!cursor.TryGetValue(s.Anchor, out int y))
                    y =
                        row < 0 ? Margin
                        : row > 0 ? canvasH - Margin
                        : (canvasH - StackHeight(slots, s.Anchor)) / 2;
                int x = At(s.Anchor, s.W, s.H, Margin, 0, canvasW, canvasH).X;
                var first = new URect(x, row > 0 ? y - s.H : y, s.W, s.H);
                var r = Fit(first, row, placed, canvasH, out bool fits);
                // No room left in the anchor's column: a column beside everything in it, from the anchor's edge again.
                int col = Anchors.Column(s.Anchor);
                var side = first.At(
                    first.X,
                    row < 0 ? Margin
                        : row > 0 ? canvasH - Margin - s.H
                        : (canvasH - s.H) / 2
                );
                while (!fits && col != 0)
                {
                    side = side.At(
                        col < 0 ? Beside(side, placed, true) + Gap : Beside(side, placed, false) - Gap - s.W,
                        side.Y
                    );
                    if (side.X < 0 || side.Right > canvasW)
                        break;
                    var there = Fit(side, row, placed, canvasH, out fits);
                    if (fits)
                        r = there;
                }
                s.Placed = Clamp(r, canvasW, canvasH);
                placed.Add(s.Placed);
                cursor[s.Anchor] = row > 0 ? s.Placed.Y - Gap : s.Placed.Bottom + Gap;
            }
        }

        // The first place from r along the stack's direction that overlaps nothing; r moved as far as it went if none.
        static URect Fit(URect r, int row, List<URect> placed, int canvasH, out bool fits)
        {
            for (int guard = 0; guard < 64; guard++)
            {
                int hit = FirstOverlap(r, placed);
                if (hit < 0)
                {
                    fits = true;
                    return r;
                }
                var b = placed[hit];
                var next = row > 0 ? r.At(r.X, b.Y - Gap - r.H) : r.At(r.X, b.Bottom + Gap);
                if (next.Y < 0 || next.Bottom > canvasH)
                    break;
                r = next;
            }
            fits = false;
            return r;
        }

        // The right edge (or left edge) of everything in the canvas-high strip that r's column covers.
        static int Beside(URect r, List<URect> placed, bool right)
        {
            int edge = right ? r.X : r.Right;
            foreach (var p in placed)
                if (p.X < r.Right && r.X < p.Right)
                    edge = right ? Math.Max(edge, p.Right) : Math.Min(edge, p.X);
            return edge;
        }

        static int StackHeight(IList<HudSlot> slots, Anchor a)
        {
            int h = 0,
                n = 0;
            foreach (var s in slots)
                if (s.Anchor == a && !s.X.HasValue && !s.Y.HasValue)
                {
                    h += s.H;
                    n++;
                }
            return h + Math.Max(0, n - 1) * Gap;
        }

        static int FirstOverlap(URect r, List<URect> others)
        {
            for (int i = 0; i < others.Count; i++)
                if (r.Overlaps(others[i]))
                    return i;
            return -1;
        }

        // The rect of a widget of size w x h at inward distances dx, dy from the anchor's edges (from the middle for
        // the centre row or column, positive to the right and down).
        public static URect At(Anchor a, int w, int h, int dx, int dy, int canvasW, int canvasH)
        {
            int col = Anchors.Column(a),
                row = Anchors.Row(a);
            int x =
                col < 0 ? dx
                : col > 0 ? canvasW - w - dx
                : (canvasW - w) / 2 + dx;
            int y =
                row < 0 ? dy
                : row > 0 ? canvasH - h - dy
                : (canvasH - h) / 2 + dy;
            return new URect(x, y, w, h);
        }

        // The inverse of At: the inward distances that put a widget at r (a dragged position).
        public static (int dx, int dy) Inward(Anchor a, URect r, int canvasW, int canvasH)
        {
            int col = Anchors.Column(a),
                row = Anchors.Row(a);
            int dx =
                col < 0 ? r.X
                : col > 0 ? canvasW - r.W - r.X
                : r.X - (canvasW - r.W) / 2;
            int dy =
                row < 0 ? r.Y
                : row > 0 ? canvasH - r.H - r.Y
                : r.Y - (canvasH - r.H) / 2;
            return (dx, dy);
        }

        public static URect Clamp(URect r, int canvasW, int canvasH) =>
            r.At(Math.Max(0, Math.Min(r.X, canvasW - r.W)), Math.Max(0, Math.Min(r.Y, canvasH - r.H)));
    }
}
