using System;

namespace ShorelandsMods.Core.Ui
{
    // The Mods page inside the game's settings window: the window grows to the page's size while it shows. Top line:
    // summary (left) and the mods folder (right); below, the mod list (left) and the selected mod's details (right),
    // each on the dark inset the game's Controls page uses; bottom row: Inspect (the author tool), Reload mods and the
    // game's Back button, as the Controls page has Defaults and Back.
    internal sealed class ModsPageLayout
    {
        public const int Margin = 12,
            TitleH = 18,
            Gap = 6,
            RowH = 16,
            TrackW = 3,
            BottomH = 32,
            MaxW = 620,
            MaxH = 420;

        public int W,
            H;
        public URect Summary,
            Folder,
            List,
            ListTrack,
            Detail,
            DetailTrack,
            Inspect,
            Reload,
            Back;
        public int ListRows;

        public static ModsPageLayout Compute(int canvasW, int canvasH, int lineH, int inspectW, int reloadW, int backW)
        {
            var L = new ModsPageLayout { W = Math.Min(MaxW, canvasW - 2 * 16), H = Math.Min(MaxH, canvasH - 2 * 10) };
            int inner = L.W - 2 * Margin;
            int y = TitleH;
            int listW = Math.Max(150, inner * 9 / 25);
            L.Summary = new URect(Margin, y, listW, lineH);
            L.Folder = new URect(Margin + listW + Gap, y, inner - listW - Gap, lineH);
            y += lineH + 4;
            int bottomY = L.H - 6 - BottomH;
            int paneH = bottomY - 6 - y;
            L.ListRows = Math.Max(1, (paneH - 4) / RowH);
            L.List = new URect(Margin, y, listW - TrackW - 1, paneH);
            L.ListTrack = new URect(Margin + listW - TrackW, y, TrackW, paneH);
            int dx = Margin + listW + Gap;
            int dw = inner - listW - Gap;
            L.Detail = new URect(dx, y, dw - TrackW - 1, paneH);
            L.DetailTrack = new URect(dx + dw - TrackW, y, TrackW, paneH);
            int row = inspectW + 8 + reloadW + 8 + backW;
            int bx = (L.W - row) / 2;
            L.Inspect = new URect(bx, bottomY, inspectW, BottomH);
            bx += inspectW + 8;
            L.Reload = new URect(bx, bottomY, reloadW, BottomH);
            L.Back = new URect(bx + reloadW + 8, bottomY, backW, BottomH);
            return L;
        }
    }
}
