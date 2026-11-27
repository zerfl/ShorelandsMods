using System;
using System.Collections.Generic;

namespace ShorelandsMods.Core.Ui
{
    // Where the parts of a mod window go, top to bottom: title with the close button, search field, buttons (wrapping
    // to more rows), text, list (with its scroll track). Everything inside the plate's padding; nothing overlaps.
    internal sealed class WindowLayout
    {
        public const int Pad = 5,
            Gap = 3,
            FieldH = 14,
            ButtonH = 14,
            ButtonPad = 12,
            ButtonGap = 3,
            TrackW = 3,
            MinListRows = 3,
            MinW = 120,
            MinH = 60,
            CanvasMargin = 4,
            TopBand = 26;

        public int W,
            H;
        public URect Title,
            Close,
            Search,
            Text,
            List,
            Track;
        public readonly List<URect> Buttons = new List<URect>();
        public int TextLines,
            ListRows,
            RowH;

        // Changes when the window's objects must be built again; texts and rows alone do not change it.
        public string Signature =>
            W + "x" + H + " " + Search + " " + string.Join(";", Buttons) + " " + Text + " " + List + " " + RowH;

        // closeW/H: the close button's size; lineH: one label line; rowH: one list row; textLinesAt: how many lines the
        // text wraps to at a width (the layout shows at most what fits).
        public static WindowLayout Compute(
            int w,
            int h,
            int canvasW,
            int canvasH,
            int lineH,
            int closeW,
            int closeH,
            bool search,
            IList<int> buttonLabelWidths,
            Func<int, int> textLinesAt,
            bool list,
            int rowH
        )
        {
            var L = new WindowLayout
            {
                W = Math.Max(MinW, Math.Min(w, canvasW - 2 * CanvasMargin)),
                H = Math.Max(MinH, Math.Min(h, canvasH - TopBand - CanvasMargin)),
                RowH = rowH,
            };
            int inner = L.W - 2 * Pad;
            int x = Pad,
                y = Pad;
            int titleH = Math.Max(lineH, closeH);
            L.Close = new URect(L.W - Pad - closeW, y, closeW, closeH);
            L.Title = new URect(x, y + (titleH - lineH) / 2, inner - closeW - Gap, lineH);
            y += titleH + Gap;
            if (search)
            {
                L.Search = new URect(x, y, inner, FieldH);
                y += FieldH + Gap;
            }
            if (buttonLabelWidths.Count > 0)
            {
                int bx = 0;
                foreach (int lw in buttonLabelWidths)
                {
                    int bw = Math.Min(inner, lw + ButtonPad);
                    if (bx > 0 && bx + bw > inner)
                    {
                        bx = 0;
                        y += ButtonH + ButtonGap;
                    }
                    L.Buttons.Add(new URect(x + bx, y, bw, ButtonH));
                    bx += bw + ButtonGap;
                }
                y += ButtonH + Gap;
            }
            int bottom = L.H - Pad;
            int textW = inner;
            int wanted = textLinesAt(textW);
            if (wanted > 0)
            {
                int room = bottom - y - (list ? Gap + MinListRows * rowH : 0);
                L.TextLines = Math.Max(0, Math.Min(wanted, room / lineH));
                if (L.TextLines > 0)
                {
                    L.Text = new URect(x, y, textW, L.TextLines * lineH);
                    y += L.TextLines * lineH + (list ? Gap : 0);
                }
            }
            if (list)
            {
                L.ListRows = Math.Max(0, (bottom - y) / rowH);
                L.List = new URect(x, y, inner - TrackW - 1, L.ListRows * rowH);
                L.Track = new URect(x + inner - TrackW, y, TrackW, L.ListRows * rowH);
            }
            return L;
        }

        // A window's place on the canvas: inside it and below TopBand, the strip that holds the game's menu and remote
        // buttons (top right, 4 to 22 units down at every resolution), so they and that strip stay clickable.
        public static URect Keep(URect r, int canvasW, int canvasH) =>
            r.At(Math.Max(0, Math.Min(r.X, canvasW - r.W)), Math.Max(TopBand, Math.Min(r.Y, canvasH - r.H)));
    }
}
