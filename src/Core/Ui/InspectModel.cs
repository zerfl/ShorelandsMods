using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core.Ui
{
    internal enum HitKind
    {
        Screen,
        World,
        Ground,
        Cursor,
    }

    // One thing drawn under the pointer, topmost first: the texture it draws from, and the item or koi when it is
    // one's icon.
    internal sealed class InspectHit
    {
        public HitKind Kind;
        public string Texture;
        public int W,
            H;

        // Null when a file can replace it, else why not ("the game makes it in code").
        public string NotReplaceable;
        public int ItemId = -1,
            KoiId = -1;
        public string Name;
        public int IconW,
            IconH;

        // Another plugin draws this koi, so a mod's file would not show.
        public bool KoiCovered;

        // Part of a player's character: whose is said only as yours or another player's.
        public PlayerPart Player;

        // How opaque it is drawn at the pointer's pixel (its texture's alpha times its colour's); 1 when not known.
        public float Alpha = 1;
    }

    internal enum PlayerPart
    {
        None,
        Yours,
        Another,
    }

    // One line pair of the inspect panel: what it is, then the file that replaces it (or why none can).
    internal sealed class InspectEntry
    {
        public string Key,
            Title,
            Detail;
        public bool Exportable;
        public InspectHit Hit;
    }

    // What inspect mode lists for the hits under the pointer (topmost first): only what is visibly drawn at that
    // pixel, plus the ground tile under a thing of the world; one entry per thing an author can name (the same
    // texture drawn twice, as in a row of tiles, once), an item or koi icon as its id rather than its texture, the
    // cursor last, at most Max entries.
    internal static class InspectModel
    {
        public const int Max = 6;

        // Under SeeThrough nothing is drawn at the pixel; from Covers on, nothing behind it shows.
        public const float SeeThrough = 0.1f,
            Covers = 0.9f;

        public static List<InspectEntry> Entries(IEnumerable<InspectHit> hits)
        {
            var list = new List<InspectEntry>();
            var seen = new HashSet<string>();
            foreach (var h in Visible(hits.ToList()))
            {
                var e = Entry(h);
                if (seen.Add(e.Key))
                    list.Add(e);
            }
            if (list.Count > Max)
            {
                var cursor = list.LastOrDefault(e => e.Hit.Kind == HitKind.Cursor);
                list = list.Where(e => e != cursor).Take(cursor != null ? Max - 1 : Max).ToList();
                if (cursor != null)
                    list.Add(cursor);
            }
            return list;
        }

        // Front to back down to the first hit that covers the pixel; things behind it are hidden and left out. Under a
        // thing of the world the topmost ground tile is added (tiles are what authors replace most), then the cursor.
        static IEnumerable<InspectHit> Visible(List<InspectHit> hits)
        {
            var shown = new List<InspectHit>();
            foreach (var h in hits)
            {
                if (h.Kind == HitKind.Cursor || h.Alpha < SeeThrough)
                    continue;
                shown.Add(h);
                if (h.Alpha >= Covers)
                    break;
            }
            if (shown.Count > 0 && shown.All(h => h.Kind != HitKind.Screen && h.Kind != HitKind.Ground))
            {
                var tile = hits.FirstOrDefault(h => h.Kind == HitKind.Ground && h.Alpha >= SeeThrough);
                if (tile != null)
                    shown.Add(tile);
            }
            return shown.Concat(hits.Where(h => h.Kind == HitKind.Cursor));
        }

        static InspectEntry Entry(InspectHit h)
        {
            if (h.ItemId >= 0)
                return new InspectEntry
                {
                    Key = "item/" + h.ItemId,
                    Title = Named(h.Name, "item " + h.ItemId),
                    Detail = "item icon " + h.IconW + "x" + h.IconH + ": art/items/" + h.ItemId + ".png",
                    Exportable = true,
                    Hit = h,
                };
            if (h.KoiId >= 0)
                return new InspectEntry
                {
                    Key = "koi/" + h.KoiId,
                    Title = Named(h.Name, "koi " + h.KoiId),
                    Detail = h.KoiCovered
                        ? "koi icon: another plugin draws this koi, a mod's art/koi/" + h.KoiId + ".png does not show"
                        : "koi icon " + h.IconW + "x" + h.IconH + ": art/koi/" + h.KoiId + ".png",
                    Exportable = true,
                    Hit = h,
                };
            string where = Where(h);
            return new InspectEntry
            {
                Key = "texture/" + h.Texture + "/" + h.W + "x" + h.H,
                Title = h.Texture,
                Detail =
                    h.NotReplaceable != null
                        ? where + ": cannot be replaced, " + h.NotReplaceable
                        : where + " " + h.W + "x" + h.H + ": art/textures/" + h.Texture + ".png",
                Exportable = h.NotReplaceable == null,
                Hit = h,
            };
        }

        static string Named(string name, string id) => string.IsNullOrEmpty(name) ? id : name + " (" + id + ")";

        static string Where(InspectHit h)
        {
            switch (h.Kind)
            {
                case HitKind.Screen:
                    return "screen";
                case HitKind.Ground:
                    return "ground tile";
                case HitKind.Cursor:
                    return "cursor";
                default:
                    return h.Player == PlayerPart.Yours ? "your character"
                        : h.Player == PlayerPart.Another ? "another player's character"
                        : "world";
            }
        }

        // The entry marked after the list changed: while the pointer stays on the same topmost thing, the same entry
        // if it is still listed; on anything new, the topmost.
        public static int Keep(List<InspectEntry> before, int marked, List<InspectEntry> now)
        {
            if (before.Count == 0 || now.Count == 0 || before[0].Key != now[0].Key || marked >= before.Count)
                return 0;
            int i = now.FindIndex(e => e.Key == before[marked].Key);
            return i >= 0 ? i : 0;
        }
    }
}
