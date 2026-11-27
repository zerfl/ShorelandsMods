using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lua;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Core.Ui
{
    internal sealed class ListRow
    {
        // Escaped, one line.
        public string Text;
        public IconRef Icon;
        public Rgba? Color;

        // The stage's cache: the text clipped to a width.
        public string Shown;
        public int ShownW;
    }

    internal sealed class WindowButton
    {
        public string Id,
            Label;
        public LuaFunction Fn;
        public bool Selected;
    }

    internal sealed class ModWindow
    {
        public string ModId,
            Id,
            Title;
        public int ReqW,
            ReqH,
            Seq;
        public string Text = "";
        public List<string> TextLines = new List<string>();
        public List<ListRow> Rows;

        // What the list shows: Rows, or the rows matching the search when the host filters (no search function).
        public List<ListRow> Shown;
        public bool HasSearch;
        public LuaFunction SearchFn;
        public string SearchHint = "",
            SearchText = "";
        public readonly List<WindowButton> Buttons = new List<WindowButton>();
        public bool Open;
        public int? SavedX,
            SavedY;

        // Set by Flush for the stage.
        public WindowLayout Layout;
        public URect Rect;
        public bool Built,
            StructureDirty = true,
            ContentDirty = true,
            RowsDirty = true;
        public int RowsVersion;

        public string Key => ModId + "/" + Id;
    }

    // The game side of mod windows.
    internal interface IWindowStage
    {
        bool Ready { get; }
        int CanvasW { get; }
        int CanvasH { get; }
        int LineH { get; }
        int RowH { get; }
        (int w, int h) CloseSize { get; }
        int MeasureText(string line);

        // Creates or rebuilds the window's objects for its Layout; Refresh updates texts and rows.
        void Build(ModWindow w);
        void Refresh(ModWindow w);
        void SetOpen(ModWindow w, bool open);
        void Place(ModWindow w);
        void Destroy(ModWindow w);
    }

    // WindowCreate, Open, Close, Toggle, IsOpen, SetText, SetList, AddSearch, GetSearch, SetSearch, AddButton, SetButton.
    internal sealed class WindowArea : Area
    {
        public const int MaxWindows = 8,
            MaxRows = 10_000,
            MaxButtons = 8,
            MaxTextChars = 4000,
            MaxRowChars = 200,
            MaxTitleChars = 60,
            MaxSearchChars = 60,
            MaxIconFiles = 64;

        // After the player closes a window of a mod, the mod may open a window again only from the player's own key,
        // command or button, or once this long has passed: not from OnWindowClosed or a timer that fights the player.
        public const double ReopenBlockSeconds = 2;

        readonly IWindowStage stage;
        readonly List<ModWindow> windows = new List<ModWindow>();
        int seq,
            laidW,
            laidH;

        public WindowArea(IWindowStage stage)
        {
            this.stage = stage;
        }

        public override string Name => "Windows";

        public IReadOnlyList<ModWindow> Windows => windows;

        sealed class State
        {
            public UiPositions Positions;

            // When the player last closed one of the mod's windows, and whether the mod was told since that a window
            // stays closed.
            public double ClosedByPlayerAt = double.NegativeInfinity;
            public bool ReopenTold;
        }

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "WindowCreate( id:string, title:string, width:number, height:number ) -> ok:boolean",
                "makes a window in the game's look, closed until WindowOpen; the same id again changes title and size. "
                    + "Size in UI units (the screen is 640x360 or 800x450): drawn at least 120 x 60 and at most the screen "
                    + "less a strip at the top (26 units) that holds the game's menu button; a window is never placed there. "
                    + "The player can drag it (the place is kept) and close it with X or Escape. false when width or height "
                    + "was outside 1 to 4000 (clamped), or no window was made (the mod already has "
                    + MaxWindows
                    + ")",
                Create
            );
            s.Function(
                "WindowOpen( id:string ) -> ok:boolean",
                "opens the window (plays ui_open); windows are drawn only in the world: at the main menu an open window "
                    + "is not shown, and shows again after the login. false for an id WindowCreate did not make, and for a "
                    + "the player closed a window of the mod less than "
                    + ReopenBlockSeconds
                    + " s ago (it stays closed; from OnKey, OnCommand_ or a button it opens at once)",
                c => SetOpen(c, true)
            );
            s.Function(
                "WindowClose( id:string ) -> ok:boolean",
                "closes the window (plays ui_close; OnWindowClosed follows); false for an id WindowCreate did not make",
                c => SetOpen(c, false)
            );
            s.Function(
                "WindowToggle( id:string ) -> ok:boolean",
                "opens the window if it is closed (false and closed, as WindowOpen, right after the player closed a "
                    + "window of the mod), else closes it; false for an id WindowCreate did not make",
                Toggle
            );
            s.Function(
                "WindowIsOpen( id:string ) -> open:boolean|nil",
                "whether the window is open (the player may have closed it); nil for an id WindowCreate did not make",
                IsOpen
            );
            s.Function(
                "WindowSetText( id:string, text:string ) -> ok:boolean",
                "the window's text, above its list; \"\\n\" starts a line, long lines wrap, at most "
                    + MaxTextChars
                    + " characters; false for an id WindowCreate did not make",
                SetText
            );
            s.Function(
                "WindowSetList( id:string, rows:table ) -> ok:boolean",
                "the window's scrolling list, replacing the last one: rows of { text, icon = item id or \"files/x.png\", "
                    + "meta, koi = koi id, color (or colour) } or plain texts, one line each (at most "
                    + MaxRowChars
                    + " characters). false when something was left out: rows beyond "
                    + MaxRows
                    + ", an unknown row field, an icon file that is missing, over "
                    + Limits.IconSide
                    + "x"
                    + Limits.IconSide
                    + " pixels or beyond "
                    + MaxIconFiles
                    + " different files in one list, a colour that is not one; or for an id WindowCreate did not make",
                SetList
            );
            s.Function(
                "WindowAddSearch( id:string, [fn:function], [hint:string] ) -> ok:boolean",
                "adds a search field above the list (again: replaces fn and hint); fn( text ) is called whenever the "
                    + "text changes; without fn the host filters the list itself (rows that have every word of the "
                    + "search as part of its text, any case; about 0.4 ms for 2,000 rows; the text typed stays and filters the next "
                    + "WindowSetList too; WindowGetSearch and OnWindowSearchChanged tell the mod the text and how many "
                    + "rows match). false for an id WindowCreate did not make",
                AddSearch
            );
            s.Function(
                "WindowGetSearch( id:string ) -> text:string|nil, shown:number|nil",
                "the search field's text and how many rows the list shows now: the rows that match the host's search, "
                    + "counted again by every WindowSetList (all rows when the search has fn). nil, nil for an id "
                    + "WindowCreate did not make or a window without WindowAddSearch",
                GetSearch
            );
            s.Function(
                "WindowSetSearch( id:string, text:string ) -> ok:boolean",
                "puts text into the search field (\"\" empties it), as if the player had typed it: the host's search "
                    + "filters the list at once. Neither OnWindowSearchChanged nor the search's fn is called for it. false "
                    + "when the text was cut to "
                    + MaxSearchChars
                    + " characters, or for an id WindowCreate did not make or a window without WindowAddSearch",
                SetSearch
            );
            s.Function(
                "WindowAddButton( id:string, button:string, label:string, fn:function ) -> ok:boolean",
                "adds a button that calls fn() when clicked, in a row under the search field (above the text and the list; "
                    + "the row wraps); button is its name for WindowSetButton; the same button again replaces its label "
                    + "and function. false for an id WindowCreate did not make or a button beyond the "
                    + MaxButtons
                    + " a window may have",
                AddButton
            );
            s.Function(
                "WindowSetButton( id:string, button:string, opts:table ) -> ok:boolean",
                "changes a button: opts.label (its text; the row wraps again) and opts.selected (true: drawn pressed with "
                    + "a gold label, for the current choice of a row like All / Owned / Missing; false: normal). false "
                    + "for an id WindowCreate did not make, a button WindowAddButton did not add, or an unknown option",
                SetButton
            );
            s.Callback(
                "OnWindowClosed( id:string, by_player:boolean )",
                "a window of the mod was closed: by_player true for its X or Escape, false for the mod's own WindowClose "
                    + "or WindowToggle. Called after the call that closed it has finished (the same or the next frame); "
                    + "not if the window is open again by then, and not when the mod unloads (OnModUnload)"
            );
            s.Callback(
                "OnWindowSearchChanged( id:string, text:string, shown:number )",
                "the player changed the text of a host search (WindowAddSearch without fn): shown is how many rows match "
                    + "now. Called on every keystroke, so only define it when the mod shows the text or the count; not "
                    + "for a search with fn (fn is called instead)"
            );
        }

        // Null (refused) for an id WindowCreate did not make.
        ModWindow Get(ApiCall c)
        {
            string id = c.String(1, "id");
            var w = windows.FirstOrDefault(x => x.ModId == c.Mod.Id && x.Id == id);
            if (w == null)
                c.Refuse(1, "there is no window \"" + TextEscape.Clip(id, 40) + "\"; WindowCreate makes one");
            return w;
        }

        int Create(ApiCall c)
        {
            string id = c.String(1, "id");
            string title = TextEscape.Plain(TextEscape.Clip(c.Text(2, "title"), MaxTitleChars));
            int width = (int)Math.Round(c.Clamp(3, "width", 1, 4000));
            int height = (int)Math.Round(c.Clamp(4, "height", 1, 4000));
            var w = windows.FirstOrDefault(x => x.ModId == c.Mod.Id && x.Id == id);
            if (w == null)
            {
                if (windows.Count(x => x.ModId == c.Mod.Id) >= MaxWindows)
                {
                    c.Refuse(
                        0,
                        "the mod already has "
                            + MaxWindows
                            + " windows, so \""
                            + TextEscape.Clip(id, 40)
                            + "\" was not made"
                    );
                    return c.ReturnOk();
                }
                w = new ModWindow
                {
                    ModId = c.Mod.Id,
                    Id = id,
                    Seq = seq++,
                };
                var st = c.Mod.Get<State>();
                st.Positions = st.Positions ?? new UiPositions(c.Mod.DataDir);
                if (st.Positions.TryGet("window", id, out int sx, out int sy))
                {
                    w.SavedX = sx;
                    w.SavedY = sy;
                }
                windows.Add(w);
                c.Mod.Resources.Add("window", id, () => Drop(w));
            }
            w.Title = title;
            w.ReqW = width;
            w.ReqH = height;
            w.StructureDirty = true;
            return c.ReturnOk();
        }

        int SetOpen(ApiCall c, bool open)
        {
            var w = Get(c);
            if (w != null && open && !w.Open && KeptClosed(c.Mod, w))
                return c.Return(false);
            if (w != null)
                SetOpen(w, open, false);
            return c.ReturnOk();
        }

        int Toggle(ApiCall c)
        {
            var w = Get(c);
            if (w != null && !w.Open && KeptClosed(c.Mod, w))
                return c.Return(false);
            if (w != null)
                SetOpen(w, !w.Open, false);
            return c.ReturnOk();
        }

        // The player closed a window of the mod just now and this call is not their own key, command or button: it
        // stays closed, and the mod's first try after each close is said once.
        bool KeptClosed(Mod mod, ModWindow w)
        {
            var st = mod.Get<State>();
            if (Host.ByPlayer || Host.Env.Now - st.ClosedByPlayerAt >= ReopenBlockSeconds)
                return false;
            Fought = (mod.Id, Host.Env.Now);
            if (!st.ReopenTold)
            {
                st.ReopenTold = true;
                Host.Notice(
                    mod,
                    "tried to open \""
                        + w.Title
                        + "\" again right after you closed it; it stays closed. Escape three times quickly opens "
                        + "the Mods page on this mod; /mods off "
                        + mod.Id
                        + " switches it off."
                );
            }
            return true;
        }

        // The last mod whose reopening was refused, and when.
        public (string ModId, double At) Fought { get; private set; } = (null, double.NegativeInfinity);

        int IsOpen(ApiCall c) => Get(c) is ModWindow w ? c.Return(w.Open) : c.Return(LuaValue.Nil);

        void SetOpen(ModWindow w, bool open, bool byPlayer)
        {
            if (w.Open == open)
                return;
            w.Open = open;
            Host.Area<SoundArea>()?.Event(open ? "ui_open" : "ui_close");
            if (stage.Ready && w.Built)
                stage.SetOpen(w, open);
            if (!open)
                Host.Post(() => FireClosed(w, byPlayer));
        }

        // Posted, so a close by the mod's own call does not run Lua inside that call.
        void FireClosed(ModWindow w, bool byPlayer)
        {
            if (w.Open || !windows.Contains(w))
                return;
            var mod = Host.Find(w.ModId);
            if (mod?.Lua != null && mod.Lua.HasFunction("OnWindowClosed"))
                Host.Call(mod, "OnWindowClosed", mod.Lua.Global("OnWindowClosed"), 2, new Args(w.Id, byPlayer));
        }

        int SetText(ApiCall c)
        {
            var w = Get(c);
            string text = c.Text(2, "text");
            if (w == null)
                return c.ReturnOk();
            if (text.Length > MaxTextChars)
                text = text.Substring(0, MaxTextChars);
            text = TextEscape.PlainLines(text);
            if (text != w.Text)
            {
                w.Text = text;
                // The text's height decides the list's, so the parts are laid out again.
                w.StructureDirty = true;
            }
            return c.ReturnOk();
        }

        int SetList(ApiCall c)
        {
            var w = Get(c);
            var t = c.Table(2, "rows");
            if (w == null)
                return c.ReturnOk();
            int n = t.ArrayLength;
            if (n > MaxRows)
            {
                c.Refuse(2, "the list has " + n + " rows; the first " + MaxRows + " are shown");
                n = MaxRows;
            }
            var rows = new List<ListRow>(n);
            var read = new RowRead();
            for (int i = 1; i <= n; i++)
                rows.Add(Row(c, t[i], i, read));
            bool hadList = w.Rows != null;
            w.Rows = rows;
            w.Shown = w.HasSearch && w.SearchFn == null ? RowFilter.Apply(rows, w.SearchText) : rows;
            w.RowsVersion++;
            w.RowsDirty = true;
            if (!hadList)
                w.StructureDirty = true;
            return c.ReturnOk();
        }

        static readonly string[] RowKeys = { "text", "icon", "meta", "koi", "color" };

        // What one WindowSetList learnt so far, so a problem shared by many rows costs one sentence and a file one
        // lookup: the icon files named (null: refused) and the refusals made.
        sealed class RowRead
        {
            public readonly Dictionary<string, IconRef> Files = new Dictionary<string, IconRef>();
            public int UnknownTold;
            readonly HashSet<string> told = new HashSet<string>();

            public void Refuse(ApiCall c, string what, Func<string> sentence)
            {
                if (told.Add(what))
                    c.Refuse(what, sentence());
            }
        }

        static ListRow Row(ApiCall c, LuaValue v, int i, RowRead read)
        {
            if (v.TryRead<string>(out var plain))
                return new ListRow { Text = OneLine(plain) };
            if (v.Type == LuaValueType.Number)
                return new ListRow { Text = OneLine(v.ToString()) };
            if (!v.TryRead<LuaTable>(out var t))
                throw c.Error(
                    "row " + i + " must be a text or a table { text = ... }, but it is " + LuaText.Describe(v)
                );
            var text = t["text"];
            var icon = t["icon"];
            var metaV = t["meta"];
            var koiV = t["koi"];
            var colorV = OptionCheck.Get(t, "color", out _);
            int known =
                (text.Type != LuaValueType.Nil ? 1 : 0)
                + (icon.Type != LuaValueType.Nil ? 1 : 0)
                + (metaV.Type != LuaValueType.Nil ? 1 : 0)
                + (koiV.Type != LuaValueType.Nil ? 1 : 0)
                + (colorV.Type != LuaValueType.Nil ? 1 : 0);
            // Only a table with more entries than the known fields is searched for the unknown one, and only as far
            // as a table of fields can reach.
            if (t.ArrayLength != 0 || t.HashMapCount != known)
            {
                int seen = 0;
                foreach (var kv in t)
                {
                    if (++seen > RowKeys.Length + 1)
                    {
                        read.Refuse(c, "row.size", () =>
                            "row " + i + " has more entries than rows have fields; they are left out"
                        );
                        break;
                    }
                    if (!kv.Key.TryRead<string>(out var key) || Array.IndexOf(RowKeys, OptionCheck.Spelling(key)) < 0)
                    {
                        string k = TextEscape.Clip(kv.Key.ToString(), 40);
                        if (read.UnknownTold == OptionCheck.MaxUnknownTold || c.Told("row." + k))
                            continue;
                        read.UnknownTold++;
                        read.Refuse(c, "row." + k, () =>
                        {
                            string near = ErrorText.Suggest(k, RowKeys);
                            return "row "
                                + i
                                + " has \""
                                + k
                                + "\", which is not known"
                                + (near != null ? " (did you mean \"" + near + "\"?)" : "")
                                + " and is left out. Rows have: "
                                + string.Join(", ", RowKeys);
                        });
                    }
                }
            }
            var row = new ListRow
            {
                Text = OneLine(
                    text.Type == LuaValueType.Nil ? ""
                    : text.TryRead<string>(out var s) ? s
                    : text.ToString()
                ),
            };
            if (icon.Type == LuaValueType.Number && icon.TryRead<double>(out var id) && id >= 1)
                row.Icon = new IconRef { ItemId = (int)id, Meta = metaV.TryRead<double>(out var meta) ? (int)meta : 0 };
            else if (icon.TryRead<string>(out var file))
                row.Icon = IconFile(c, i, file, read);
            else if (icon.Type != LuaValueType.Nil)
                throw c.Error(
                    "row "
                        + i
                        + ": icon must be an item id or a PNG file of the mod, but it is "
                        + LuaText.Describe(icon)
                );
            if (koiV.Type == LuaValueType.Number && koiV.TryRead<double>(out var koi))
                row.Icon = new IconRef { ItemId = KoiItemId, Meta = (int)koi };
            if (colorV.Type == LuaValueType.Nil)
                return row;
            if (!colorV.TryRead<string>(out var hex))
                row.Color = OptionCheck.Color(c, t, "color") ?? row.Color;
            else if (Rgba.TryParseHex(hex, out var col))
                row.Color = col;
            else
                read.Refuse(c, "row.color", () =>
                    "row "
                    + i
                    + ": \""
                    + TextEscape.Clip(hex, 40)
                    + "\" is not a colour like \"#ffcc00\"; it is left out"
                );
            return row;
        }

        public const int KoiItemId = 9999;

        static IconRef IconFile(ApiCall c, int i, string file, RowRead read)
        {
            // A long path is refused before it is hashed for the lookup.
            if (file.Length <= ModPaths.MaxPathChars && read.Files.TryGetValue(file, out var known))
                return known;
            if (read.Files.Count >= MaxIconFiles)
            {
                read.Refuse(c, "row.files", () =>
                    "a list may name at most " + MaxIconFiles + " different icon files; row " + i + " has no icon"
                );
                return null;
            }
            var r = IconRef.OfFile(c.Mod, file, out var why);
            if (r == null)
                read.Refuse(c, "row.icon", () => "row " + i + ": " + why + "; the row has no icon");
            if (file.Length <= ModPaths.MaxPathChars)
                read.Files[file] = r;
            return r;
        }

        static string OneLine(string s)
        {
            if (s.Length > MaxRowChars)
                s = s.Substring(0, MaxRowChars);
            foreach (char ch in s)
                if (char.IsControl(ch))
                {
                    s = TextEscape.Plain(s);
                    break;
                }
            return s;
        }

        int AddSearch(ApiCall c)
        {
            var w = Get(c);
            if (w == null)
                return c.ReturnOk();
            bool had = w.HasSearch;
            w.HasSearch = true;
            // ( id, fn [, hint] ), ( id [, hint] ): without a function the host filters the list itself.
            int hintArg = 3;
            if (c.Arg(2).Type == LuaValueType.String)
            {
                w.SearchFn = null;
                hintArg = 2;
            }
            else
                w.SearchFn = c.Has(2) ? c.Func(2, "fn") : null;
            w.SearchHint = c.Has(hintArg) ? TextEscape.Plain(TextEscape.Clip(c.Text(hintArg, "hint"), 60)) : "search";
            if (w.Rows != null)
            {
                w.Shown = w.SearchFn == null ? RowFilter.Apply(w.Rows, w.SearchText) : w.Rows;
                w.RowsVersion++;
                w.RowsDirty = true;
            }
            if (!had)
                w.StructureDirty = true;
            else
                w.ContentDirty = true;
            return c.ReturnOk();
        }

        int GetSearch(ApiCall c)
        {
            var w = Get(c);
            if (w == null || !w.HasSearch)
                return c.Return(LuaValue.Nil, LuaValue.Nil);
            return c.Return(w.SearchText, ShownCount(w));
        }

        static int ShownCount(ModWindow w) => w.Shown?.Count ?? 0;

        int SetSearch(ApiCall c)
        {
            var w = Get(c);
            string text = c.Text(2, "text");
            if (w == null)
                return c.ReturnOk();
            if (!w.HasSearch)
            {
                c.Refuse(1, "the window \"" + w.Id + "\" has no search field; WindowAddSearch adds one");
                return c.ReturnOk();
            }
            if (text.Length > MaxSearchChars)
            {
                c.Refuse(2, "a search is at most " + MaxSearchChars + " characters, so the text was cut");
                text = text.Substring(0, MaxSearchChars);
            }
            text = TextEscape.Plain(text);
            if (text == w.SearchText)
                return c.ReturnOk();
            w.SearchText = text;
            if (w.SearchFn == null && w.Rows != null)
            {
                w.Shown = RowFilter.Apply(w.Rows, text);
                w.RowsVersion++;
                w.RowsDirty = true;
            }
            w.ContentDirty = true;
            return c.ReturnOk();
        }

        int AddButton(ApiCall c)
        {
            var w = Get(c);
            string id = c.String(2, "button");
            string label = Label(c.Text(3, "label"));
            var fn = c.Func(4, "fn");
            if (w == null)
                return c.ReturnOk();
            var b = w.Buttons.Find(x => x.Id == id);
            if (b == null)
            {
                if (w.Buttons.Count >= MaxButtons)
                {
                    c.Refuse(
                        0,
                        "the window already has "
                            + MaxButtons
                            + " buttons, so \""
                            + TextEscape.Clip(id, 40)
                            + "\" was not added"
                    );
                    return c.ReturnOk();
                }
                w.Buttons.Add(b = new WindowButton { Id = id });
                w.StructureDirty = true;
            }
            b.Fn = fn;
            SetLabel(w, b, label);
            return c.ReturnOk();
        }

        static readonly string[] ButtonOptions = { "label", "selected" };

        int SetButton(ApiCall c)
        {
            var w = Get(c);
            string id = c.String(2, "button");
            var t = c.Table(3, "opts");
            OptionCheck.Known(c, t, ButtonOptions, 3);
            var lv = t["label"];
            string label =
                lv.Type == LuaValueType.Nil ? null
                : lv.TryRead<string>(out var ls) ? Label(ls)
                : lv.Type == LuaValueType.Number ? Label(lv.ToString())
                : throw c.Error("option label must be a text, but it is " + LuaText.Describe(lv));
            bool? selected = OptionCheck.Bool(c, t, "selected");
            if (w == null)
                return c.ReturnOk();
            var b = w.Buttons.Find(x => x.Id == id);
            if (b == null)
            {
                c.Refuse(
                    2,
                    "the window \""
                        + TextEscape.Clip(w.Id, 40)
                        + "\" has no button \""
                        + TextEscape.Clip(id, 40)
                        + "\"; WindowAddButton adds one"
                );
                return c.ReturnOk();
            }
            if (label != null)
                SetLabel(w, b, label);
            if (selected.HasValue && selected.Value != b.Selected)
            {
                b.Selected = selected.Value;
                w.ContentDirty = true;
            }
            return c.ReturnOk();
        }

        static string Label(string s) => TextEscape.Plain(TextEscape.Clip(s, 40));

        // A new label may change the buttons' widths, so the parts are laid out again.
        static void SetLabel(ModWindow w, WindowButton b, string label)
        {
            if (b.Label == label)
                return;
            b.Label = label;
            w.StructureDirty = true;
        }

        void Drop(ModWindow w)
        {
            bool wasOpen = w.Open;
            windows.Remove(w);
            stage.Destroy(w);
            if (wasOpen)
                Host.Area<SoundArea>()?.Event("ui_close");
        }

        protected internal override IEnumerable<LuaFunction> Held(Mod mod) =>
            windows
                .Where(w => w.ModId == mod.Id)
                .SelectMany(w => w.Buttons.Select(b => b.Fn).Append(w.SearchFn))
                .Where(f => f != null);

        // After Lua ran this frame. The time a window takes is charged to its mod.
        public void Flush()
        {
            if (!stage.Ready)
            {
                laidW = 0;
                return;
            }
            bool canvasChanged = stage.CanvasW != laidW || stage.CanvasH != laidH;
            laidW = stage.CanvasW;
            laidH = stage.CanvasH;
            foreach (var w in windows)
            {
                if (!w.StructureDirty && !canvasChanged && !w.ContentDirty && !w.RowsDirty)
                    continue;
                long t0 = HostClock.Now;
                Draw(w, canvasChanged);
                var mod = Host.Find(w.ModId);
                if (mod != null)
                    Host.ChargeWork(mod, DrawingWork, ModLoad.MsSince(t0));
            }
        }

        public const string DrawingWork = "drawing its HUD and windows";

        void Draw(ModWindow w, bool canvasChanged)
        {
            if (w.StructureDirty || canvasChanged)
            {
                string before = w.Built ? w.Layout.Signature : null;
                Lay(w);
                if (canvasChanged || before != w.Layout.Signature)
                {
                    stage.Build(w);
                    w.Built = true;
                    stage.Place(w);
                    stage.SetOpen(w, w.Open);
                }
                w.StructureDirty = false;
                w.ContentDirty = w.RowsDirty = false;
                stage.Refresh(w);
            }
            else
            {
                stage.Refresh(w);
                w.ContentDirty = w.RowsDirty = false;
            }
        }

        void Lay(ModWindow w)
        {
            List<string> lines = null;
            int lineH = stage.LineH;
            var (cw, ch) = stage.CloseSize;
            var labels = w.Buttons.Select(b => stage.MeasureText(b.Label)).ToList();
            w.Layout = WindowLayout.Compute(
                w.ReqW,
                w.ReqH,
                laidW,
                laidH,
                lineH,
                cw,
                ch,
                w.HasSearch,
                labels,
                textW =>
                {
                    lines =
                        w.Text.Length == 0 ? new List<string>() : TextFit.Wrap(w.Text, textW, 400, stage.MeasureText);
                    return lines.Count;
                },
                w.Rows != null,
                stage.RowH
            );
            w.TextLines =
                lines == null || lines.Count <= w.Layout.TextLines
                    ? lines ?? new List<string>()
                    : TextFit.Wrap(w.Text, w.Layout.Text.W, w.Layout.TextLines, stage.MeasureText);
            w.Rect = Position(w);
        }

        URect Position(ModWindow w)
        {
            int W = w.Layout.W,
                H = w.Layout.H;
            int cascade = windows.Count(x => x.ModId == w.ModId && x.Seq < w.Seq) % 4 * 12;
            var r = HudLayout.At(Anchor.Center, W, H, w.SavedX ?? cascade, w.SavedY ?? cascade, laidW, laidH);
            return WindowLayout.Keep(r, laidW, laidH);
        }

        // The player closed it (X or Escape).
        public void ClosedByPlayer(ModWindow w)
        {
            var mod = Host.Find(w.ModId);
            if (!windows.Contains(w) || mod == null)
                return;
            var st = mod.Get<State>();
            st.ClosedByPlayerAt = Host.Env.Now;
            st.ReopenTold = false;
            SetOpen(w, false, true);
        }

        // The escape hatch (Escape pressed EscapePresses.Presses times quickly): when a mod fought the player over a
        // window in that time (a refused reopen, or a window open again), every mod window closes as if the player
        // closed it and that mod's id is returned; else null and nothing changes.
        public string Panic(double since)
        {
            string id = Fought.At >= since ? Fought.ModId : null;
            id = id
                ?? windows
                    .Where(w => w.Open && Host.Find(w.ModId)?.Get<State>().ClosedByPlayerAt >= since)
                    .Select(w => w.ModId)
                    .LastOrDefault();
            if (id == null)
                return null;
            foreach (var w in windows.Where(w => w.Open).ToList())
                ClosedByPlayer(w);
            return id;
        }

        public void SearchChanged(ModWindow w, string text)
        {
            if (!windows.Contains(w) || !w.HasSearch || text == w.SearchText)
                return;
            w.SearchText = text;
            var mod = Host.Find(w.ModId);
            if (w.SearchFn != null)
            {
                if (mod != null)
                    Host.Call(mod, "the search function of window \"" + w.Id + "\"", w.SearchFn, 1, new Args(text));
                return;
            }
            if (w.Rows != null)
            {
                w.Shown = RowFilter.Apply(w.Rows, text);
                w.RowsVersion++;
                w.RowsDirty = true;
            }
            if (mod?.Lua != null && mod.Lua.HasFunction("OnWindowSearchChanged"))
                Host.Call(
                    mod,
                    "OnWindowSearchChanged",
                    mod.Lua.Global("OnWindowSearchChanged"),
                    3,
                    new Args(w.Id, text, ShownCount(w))
                );
        }

        public void ButtonPressed(ModWindow w, int index)
        {
            if (!windows.Contains(w) || index < 0 || index >= w.Buttons.Count)
                return;
            var b = w.Buttons[index];
            var mod = Host.Find(w.ModId);
            if (mod != null)
                Host.CallByPlayer(mod, "the button \"" + b.Id + "\" of window \"" + w.Id + "\"", b.Fn, 0, default);
        }

        public void Dragged(ModWindow w, URect r)
        {
            var mod = Host.Find(w.ModId);
            if (mod == null || !windows.Contains(w))
                return;
            w.Rect = WindowLayout.Keep(r, laidW, laidH);
            var (dx, dy) = HudLayout.Inward(Anchor.Center, w.Rect, laidW, laidH);
            w.SavedX = dx;
            w.SavedY = dy;
            var st = mod.Get<State>();
            st.Positions = st.Positions ?? new UiPositions(mod.DataDir);
            st.Positions.Set("window", w.Id, dx, dy);
            stage.Place(w);
        }

        public void ResetPosition(ModWindow w)
        {
            var mod = Host.Find(w.ModId);
            if (mod == null || !windows.Contains(w))
                return;
            w.SavedX = w.SavedY = null;
            var st = mod.Get<State>();
            st.Positions = st.Positions ?? new UiPositions(mod.DataDir);
            st.Positions.Forget("window", w.Id);
            w.Rect = Position(w);
            stage.Place(w);
        }
    }
}

namespace ShorelandsMods.Core.Ui
{
    // Escape pressed Presses times within Seconds: a player trying to get away from something (WindowArea.Panic).
    internal sealed class EscapePresses
    {
        public const int Presses = 3;
        public const double Seconds = 2;
        readonly Queue<double> at = new Queue<double>();

        // When this press completes Presses quick ones: the time of the first of them (and the count starts again).
        public double? Press(double now)
        {
            at.Enqueue(now);
            while (now - at.Peek() > Seconds)
                at.Dequeue();
            if (at.Count < Presses)
                return null;
            double first = at.Peek();
            at.Clear();
            return first;
        }
    }
}

namespace ShorelandsMods.Core.Ui
{
    // The host's own search: a row matches when its text contains every word of the search, in any case.
    internal static class RowFilter
    {
        public static List<ListRow> Apply(List<ListRow> rows, string search)
        {
            var words = (search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return rows;
            var shown = new List<ListRow>();
            foreach (var r in rows)
            {
                bool all = true;
                foreach (var w in words)
                    if (r.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        all = false;
                        break;
                    }
                if (all)
                    shown.Add(r);
            }
            return shown;
        }
    }
}
