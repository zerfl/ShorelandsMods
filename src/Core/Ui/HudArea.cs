using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core.Ui
{
    internal enum HudKind
    {
        Text,
        Icon,
        Bar,
    }

    // One HUD widget of a mod as the stage draws it.
    internal sealed class HudWidget
    {
        public string ModId,
            Id;
        public int Seq;
        public HudKind Kind;

        // Text: escaped lines, already broken to fit (TextFit); Lines is what the stage draws.
        public string Text = "";
        public List<string> Lines = new List<string>();
        public IconRef Icon;
        public double Fraction;
        public Anchor Anchor;
        public int? X,
            Y,
            SavedX,
            SavedY;
        public Rgba Color = new Rgba(1, 1, 1, 1);
        public int Scale = 1,
            BarW = 60,
            BarH = 4;
        public bool Plate,
            Draggable,
            Visible = true;

        // Set by the stage when it drew the widget; set by the layout.
        public int W,
            H;
        public URect Rect;
        public bool Dirty = true;

        public string Key => ModId + "/" + Id;
    }

    // An item icon (item id, meta: a koi's id for item 9999) or a PNG file of the mod, with its size and write time
    // when the host checked it (a new version is a new icon).
    internal sealed class IconRef
    {
        public int ItemId,
            Meta;
        public string File;
        public long Stamp;

        public override bool Equals(object o) =>
            o is IconRef r && r.ItemId == ItemId && r.Meta == Meta && r.File == File && r.Stamp == Stamp;

        public override int GetHashCode() => ItemId * 31 + Meta + (File?.GetHashCode() ?? 0);

        // Icon files a mod named, by the path it gave: checked once per mod start (any change of its files reloads it).
        sealed class Checked
        {
            public readonly Dictionary<string, (IconRef Icon, string Why)> Files =
                new Dictionary<string, (IconRef, string)>();
        }

        static readonly string[] PngExtension = { ".png" };

        // A PNG of the mod whose header and file size passed for an icon of at most Limits.IconSide pixels a side;
        // null with a sentence otherwise. No decoder has seen it yet.
        public static IconRef OfFile(Mod mod, string path, out string why)
        {
            var known = mod.Get<Checked>().Files;
            // A long path is refused before it is hashed for the lookup.
            if (path.Length > ModPaths.MaxPathChars)
            {
                ModPaths.ResolveFile(mod.Dir, mod.Id, path, PngExtension, out _, out why);
                return null;
            }
            if (known.TryGetValue(path, out var k))
            {
                why = k.Why;
                return k.Icon;
            }
            if (known.Count >= Limits.IconFilesPerMod)
            {
                why = "the mod already uses " + Limits.IconFilesPerMod + " icon files";
                return null;
            }
            IconRef icon = null;
            string full = ModPaths.ResolveFile(mod.Dir, mod.Id, path, PngExtension, out var rel, out why);
            if (full != null)
            {
                if (!System.IO.File.Exists(full))
                    why = "there is no file " + rel + " in the mod folder";
                else if (!FileHeaders.Png(full, out _, out _, out string problem, Limits.IconSide))
                    why = rel + " cannot be an icon: " + problem;
                else
                {
                    var info = new System.IO.FileInfo(full);
                    icon = new IconRef { File = full, Stamp = info.Length ^ info.LastWriteTimeUtc.Ticks };
                }
            }
            known[path] = (icon, why);
            return icon;
        }
    }

    // The game side of the HUD.
    internal interface IHudStage
    {
        // The game's HUD exists (the player is in the world).
        bool Ready { get; }
        int CanvasW { get; }
        int CanvasH { get; }

        // Rects of the game's own HUD (menu button, hotbar, chat, wallet, ...).
        List<URect> GameHud();

        // Width of a label line in the font in use, at text scale 1.
        int MeasureText(string line);

        // Creates or updates the widget's visuals and sets its W and H.
        void Show(HudWidget w);
        void Place(HudWidget w);
        void Remove(HudWidget w);
    }

    // HudText, HudIcon, HudBar, HudRemove, HudShow: retained widgets by id per mod, laid out by HudLayout.
    internal sealed class HudArea : Area
    {
        public const int MaxWidgets = 64,
            MaxTextChars = 500,
            MaxLines = 12,
            MaxTextWidth = 240;

        static readonly string[] TextOpts = { "anchor", "x", "y", "color", "size", "plate", "draggable" };
        static readonly string[] IconOpts = { "anchor", "x", "y", "color", "size", "plate", "draggable", "meta" };
        static readonly string[] BarOpts = { "anchor", "x", "y", "color", "width", "height", "plate", "draggable" };

        readonly IHudStage stage;
        readonly List<HudWidget> widgets = new List<HudWidget>();
        int seq,
            laidW,
            laidH;
        bool layoutDirty;
        List<URect> gameHud = new List<URect>();
        double nextHudCheck;

        public HudArea(IHudStage stage)
        {
            this.stage = stage;
        }

        public override string Name => "HUD";

        public IReadOnlyList<HudWidget> Widgets => widgets;

        sealed class State
        {
            public UiPositions Positions;
        }

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "HudText( id:string, text:string, [opts:table] ) -> ok:boolean",
                "shows a text on the HUD, or updates the widget with this id (an id keeps its kind); \"\\n\" starts a "
                    + "line, lines wrap at 240 UI units, at most "
                    + MaxLines
                    + " lines. opts: anchor (\"top_left\" \"top\" \"top_right\" \"left\" \"center\" \"right\" \"bottom_left\" "
                    + "\"bottom\" \"bottom_right\"; default top_left), x, y (UI units from the anchor's edges, -2000 to 2000; "
                    + "left out, the host stacks the widget clear of the game's HUD and other widgets), color (or colour), size (1-3), "
                    + "plate (true: on the game's plate), draggable (the player may drag it; right click puts it back). "
                    + "Options given once stay until given again. false when an option was clamped or left out (unknown "
                    + "option or anchor, a colour that is not one), or nothing was shown (the id is another kind, or the "
                    + "mod already has "
                    + MaxWidgets
                    + " widgets)",
                c => Upsert(c, HudKind.Text)
            );
            s.Function(
                "HudIcon( id:string, icon:number|string, [opts:table] ) -> ok:boolean",
                "shows an icon on the HUD: an item id (opts.meta picks the koi for item 9999) or a PNG file of the mod "
                    + "(\"files/x.png\", at most "
                    + Limits.IconSide
                    + "x"
                    + Limits.IconSide
                    + " pixels; "
                    + Limits.IconFilesPerMod
                    + " different files per mod, list icons included); opts and false as HudText, also false for an "
                    + "id below 1 or a file that is missing, outside the mod or too big",
                c => Upsert(c, HudKind.Icon)
            );
            s.Function(
                "HudBar( id:string, fraction:number, [opts:table] ) -> ok:boolean",
                "shows a bar filled to fraction (0 to 1; outside is drawn as 0 or 1); opts as HudText without size, "
                    + "plus width (8-200, default 60) and height (2-16, default 4) in UI units; false as HudText",
                c => Upsert(c, HudKind.Bar)
            );
            s.Function("HudRemove( id:string )", "removes a HUD widget of this mod; an unknown id is ignored", Remove);
            s.Function(
                "HudShow( id:string, visible:boolean )",
                "hides or shows a HUD widget of this mod without removing it; an unknown id is ignored",
                Show
            );
        }

        HudWidget Find(Mod mod, string id) => widgets.FirstOrDefault(w => w.ModId == mod.Id && w.Id == id);

        int Upsert(ApiCall c, HudKind kind)
        {
            string id = c.String(1, "id");
            var w = Find(c.Mod, id);
            bool isNew = w == null;
            if (isNew)
            {
                if (widgets.Count(x => x.ModId == c.Mod.Id) >= MaxWidgets)
                {
                    c.Refuse(
                        0,
                        "the mod already has "
                            + MaxWidgets
                            + " HUD widgets, so \""
                            + TextEscape.Clip(id, 40)
                            + "\" and any further ones are not shown"
                    );
                    return c.ReturnOk();
                }
                w = new HudWidget
                {
                    ModId = c.Mod.Id,
                    Id = id,
                    Seq = seq++,
                    Kind = kind,
                };
            }
            else if (w.Kind != kind)
            {
                c.Refuse(
                    1,
                    "\""
                        + TextEscape.Clip(id, 40)
                        + "\" is already a "
                        + KindName(w.Kind)
                        + ", so it was not changed; use another id"
                );
                return c.ReturnOk();
            }
            switch (kind)
            {
                case HudKind.Text:
                    w.Text = c.Text(2, "text");
                    if (w.Text.Length > MaxTextChars)
                        w.Text = w.Text.Substring(0, MaxTextChars);
                    break;
                case HudKind.Icon:
                    var icon = IconArg(c, 2, "icon");
                    if (icon == null)
                        return c.ReturnOk();
                    w.Icon = icon;
                    break;
                default:
                    w.Fraction = Math.Max(0, Math.Min(1, c.Number(2, "fraction")));
                    break;
            }
            var opts = c.Arg(3);
            if (opts.Type != LuaValueType.Nil)
                ApplyOptions(
                    c,
                    w,
                    c.Table(3, "opts"),
                    kind == HudKind.Text ? TextOpts
                        : kind == HudKind.Icon ? IconOpts
                        : BarOpts
                );
            if (kind == HudKind.Text)
                w.Lines = TextFit.Wrap(TextEscape.PlainLines(w.Text), MaxTextWidth, MaxLines, s =>
                    stage.MeasureText(s) * w.Scale
                );
            w.Dirty = true;
            layoutDirty = true;
            if (isNew)
            {
                var st = c.Mod.Get<State>();
                st.Positions = st.Positions ?? new UiPositions(c.Mod.DataDir);
                if (st.Positions.TryGet("hud", id, out int sx, out int sy))
                {
                    w.SavedX = sx;
                    w.SavedY = sy;
                }
                widgets.Add(w);
                var mod = c.Mod;
                mod.Resources.Add("hud", id, () => Drop(w));
            }
            return c.ReturnOk();
        }

        static LuaValue? Opt(LuaTable t, string key)
        {
            var v = t[key];
            return v.Type == LuaValueType.Nil ? (LuaValue?)null : v;
        }

        static string KindName(HudKind k) =>
            k == HudKind.Text ? "HudText"
            : k == HudKind.Icon ? "HudIcon"
            : "HudBar";

        // An item id (number), or a PNG of the mod given by path; null (refused) for an id below 1 or a bad file.
        public static IconRef IconArg(ApiCall c, int n, string name)
        {
            var v = c.Arg(n);
            if (v.Type == LuaValueType.Number)
            {
                int? id = c.Whole(n, name);
                if (id == null || id <= 0)
                {
                    c.Refuse(n, name + " must be an item id (a whole number above 0), not " + LuaText.Describe(v));
                    return null;
                }
                return new IconRef { ItemId = id.Value };
            }
            if (v.TryRead<string>(out var path))
            {
                var icon = IconRef.OfFile(c.Mod, path, out var why);
                if (icon == null)
                    c.Refuse(n, why);
                return icon;
            }
            throw c.Wrong(n, name, "an item id or a PNG file of the mod");
        }

        void ApplyOptions(ApiCall c, HudWidget w, LuaTable t, string[] allowed)
        {
            OptionCheck.Known(c, t, allowed, 3);
            if (Opt(t, "anchor") is LuaValue a)
            {
                if (!a.TryRead<string>(out var an))
                    throw c.Error("option anchor must be text like \"top_left\", but it is " + LuaText.Describe(a));
                if (Anchors.TryParse(an, out var anchor))
                    w.Anchor = anchor;
                else
                    c.Refuse(
                        "opts.anchor",
                        "the anchor \""
                            + TextEscape.Clip(an, 40)
                            + "\" is not known, so it is left out. Anchors: "
                            + Anchors.List
                    );
            }
            w.X = OptionCheck.Int(c, t, "x", -2000, 2000) ?? w.X;
            w.Y = OptionCheck.Int(c, t, "y", -2000, 2000) ?? w.Y;
            w.Color = OptionCheck.Color(c, t, "color") ?? w.Color;
            if (Array.IndexOf(allowed, "size") >= 0)
                w.Scale = OptionCheck.Int(c, t, "size", 1, 3) ?? w.Scale;
            if (Array.IndexOf(allowed, "width") >= 0)
            {
                w.BarW = OptionCheck.Int(c, t, "width", 8, 200) ?? w.BarW;
                w.BarH = OptionCheck.Int(c, t, "height", 2, 16) ?? w.BarH;
            }
            if (w.Icon != null)
                w.Icon.Meta = OptionCheck.Int(c, t, "meta", 0, 1_000_000) ?? 0;
            w.Plate = OptionCheck.Bool(c, t, "plate") ?? w.Plate;
            w.Draggable = OptionCheck.Bool(c, t, "draggable") ?? w.Draggable;
        }

        int Remove(ApiCall c)
        {
            c.Mod.Resources.Remove("hud", c.String(1, "id"));
            return c.Return();
        }

        int Show(ApiCall c)
        {
            var w = Find(c.Mod, c.String(1, "id"));
            bool on = c.Boolean(2, "visible");
            if (w != null && w.Visible != on)
            {
                w.Visible = on;
                w.Dirty = true;
                layoutDirty = true;
            }
            return c.Return();
        }

        void Drop(HudWidget w)
        {
            widgets.Remove(w);
            stage.Remove(w);
            layoutDirty = true;
        }

        protected internal override void ModStopping(Mod mod) => layoutDirty = true;

        // After Lua ran this frame: draw what changed and lay out again when sizes, the canvas or the game's HUD moved.
        public void Flush(double now)
        {
            if (!stage.Ready)
            {
                laidW = 0;
                return;
            }
            if (stage.CanvasW != laidW || stage.CanvasH != laidH)
            {
                foreach (var w in widgets)
                    w.Dirty = true;
                layoutDirty = true;
            }
            if (now >= nextHudCheck)
            {
                nextHudCheck = now + 0.5;
                var hud = stage.GameHud();
                if (!hud.SequenceEqual(gameHud))
                {
                    gameHud = hud;
                    layoutDirty = true;
                }
            }
            foreach (var w in widgets)
                if (w.Dirty)
                {
                    int ow = w.W,
                        oh = w.H;
                    long t0 = HostClock.Now;
                    stage.Show(w);
                    var mod = Host.Find(w.ModId);
                    if (mod != null)
                        Host.ChargeWork(mod, WindowArea.DrawingWork, ModLoad.MsSince(t0));
                    w.Dirty = false;
                    if (w.W != ow || w.H != oh)
                        layoutDirty = true;
                }
            if (!layoutDirty)
                return;
            layoutDirty = false;
            Layout();
        }

        void Layout()
        {
            laidW = stage.CanvasW;
            laidH = stage.CanvasH;
            var order = widgets.Where(w => w.Visible).OrderBy(w => Host.OrderOf(w.ModId)).ThenBy(w => w.Seq).ToList();
            var slots = order
                .Select(w => new HudSlot
                {
                    Anchor = w.Anchor,
                    W = w.W,
                    H = w.H,
                    X = w.SavedX ?? w.X,
                    Y = w.SavedY ?? w.Y,
                })
                .ToList();
            HudLayout.Place(slots, laidW, laidH, gameHud);
            for (int i = 0; i < order.Count; i++)
            {
                order[i].Rect = slots[i].Placed;
                stage.Place(order[i]);
            }
        }

        // The player dragged a widget to r (canvas units); it stays there, also after a restart.
        public void Dragged(HudWidget w, URect r)
        {
            var mod = Host.Find(w.ModId);
            if (mod == null || !widgets.Contains(w))
                return;
            var (dx, dy) = HudLayout.Inward(w.Anchor, HudLayout.Clamp(r, laidW, laidH), laidW, laidH);
            w.SavedX = dx;
            w.SavedY = dy;
            var st = mod.Get<State>();
            st.Positions = st.Positions ?? new UiPositions(mod.DataDir);
            st.Positions.Set("hud", w.Id, dx, dy);
            layoutDirty = true;
        }

        // Right click on a dragged widget: back to where the mod put it.
        public void ResetPosition(HudWidget w)
        {
            var mod = Host.Find(w.ModId);
            if (mod == null || !widgets.Contains(w))
                return;
            w.SavedX = w.SavedY = null;
            var st = mod.Get<State>();
            st.Positions = st.Positions ?? new UiPositions(mod.DataDir);
            st.Positions.Forget("hud", w.Id);
            layoutDirty = true;
        }
    }
}
