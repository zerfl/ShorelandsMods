using System;

namespace ShorelandsMods.Core.Ui
{
    // The visible part of a long list drawn with a fixed pool of rows: which item each row slot shows, scrolling by
    // wheel or track, and the scroll mark. Immutable; every change returns a new value.
    internal readonly struct ListScroll
    {
        public readonly int Rows,
            Count,
            Offset;

        public ListScroll(int rows, int count, int offset)
        {
            Rows = Math.Max(0, rows);
            Count = Math.Max(0, count);
            Offset = Math.Max(0, Math.Min(offset, Math.Max(0, Count - Rows)));
        }

        public int MaxOffset => Math.Max(0, Count - Rows);

        public bool CanScroll => Count > Rows;

        // The item shown in a row slot, or -1 for an empty slot below the end.
        public int IndexAt(int slot)
        {
            int i = Offset + slot;
            return slot >= 0 && slot < Rows && i < Count ? i : -1;
        }

        public ListScroll ScrollBy(int rows) => new ListScroll(Rows, Count, Offset + rows);

        public ListScroll WithCount(int count) => new ListScroll(Rows, count, Offset);

        public ListScroll WithRows(int rows) => new ListScroll(rows, Count, Offset);

        // Makes an item visible with as little movement as possible.
        public ListScroll Reveal(int index) =>
            index < Offset ? new ListScroll(Rows, Count, index)
            : index >= Offset + Rows ? new ListScroll(Rows, Count, index - Rows + 1)
            : this;

        // The mark on a track of trackH units: (top, height), at least 4 units high; the whole track when nothing
        // scrolls.
        public (int y, int h) Mark(int trackH)
        {
            if (!CanScroll || trackH <= 0)
                return (0, trackH);
            int h = Math.Max(4, (int)Math.Round((double)trackH * Rows / Count));
            h = Math.Min(h, trackH);
            int y = (int)Math.Round((double)(trackH - h) * Offset / MaxOffset);
            return (y, h);
        }

        // A click or drag at y on the track centres the mark there.
        public ListScroll AtTrack(int y, int trackH)
        {
            if (!CanScroll || trackH <= 0)
                return this;
            var (_, h) = Mark(trackH);
            double free = trackH - h;
            double t = free <= 0 ? 0 : (y - h / 2.0) / free;
            t = Math.Max(0, Math.Min(1, t));
            return new ListScroll(Rows, Count, (int)Math.Round(t * MaxOffset));
        }
    }
}
