using System;
using System.Collections.Generic;
using System.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;

// Windows: layout, list scrolling, buttons, closing, the host search, and the way out of a mod's windows.
static partial class Program
{
    static void Scrolling()
    {
        var v = new ListScroll(10, 2000, 0);
        Check(v.IndexAt(0) == 0 && v.IndexAt(9) == 9 && v.IndexAt(10) == -1, "rows show the first items");
        v = v.ScrollBy(5000);
        Check(v.Offset == 1990 && v.IndexAt(9) == 1999, "scrolling stops at the end", v.Offset.ToString());
        v = v.ScrollBy(-9999);
        Check(v.Offset == 0, "scrolling stops at the top");
        var (my, mh) = new ListScroll(10, 2000, 1990).Mark(140);
        Check(my + mh == 140 && mh >= 4, "the mark reaches the end of the track at the end", my + "+" + mh);
        Check(new ListScroll(10, 2000, 0).AtTrack(139, 140).Offset == 1990, "track bottom scrolls to the end");
        Check(new ListScroll(10, 2000, 500).AtTrack(0, 140).Offset == 0, "track top scrolls to the start");
        Check(
            new ListScroll(10, 5, 3).Offset == 0 && !new ListScroll(10, 5, 0).CanScroll,
            "a short list does not scroll"
        );
        Check(
            new ListScroll(10, 2000, 0).WithCount(3).IndexAt(3) == -1,
            "filtering to fewer rows empties the slots below"
        );
        Check(new ListScroll(10, 100, 0).Reveal(50).IndexAt(9) == 50, "reveal brings an item into view");
    }

    static void WindowParts()
    {
        foreach (var (cw, ch) in new[] { (640, 360), (800, 450) })
        foreach (
            var (text, list, search, buttons) in new[]
            {
                (30, true, true, 6),
                (0, true, false, 0),
                (3, false, true, 2),
                (200, true, true, 3),
            }
        )
        {
            var L = WindowLayout.Compute(
                300,
                300,
                cw,
                ch,
                10,
                11,
                11,
                search,
                Enumerable.Range(0, buttons).Select(i => 40 + 10 * i).ToList(),
                _ => text,
                list,
                14
            );
            var parts = new List<URect> { L.Title, L.Close };
            if (search)
                parts.Add(L.Search);
            parts.AddRange(L.Buttons);
            if (L.TextLines > 0)
                parts.Add(L.Text);
            if (list)
            {
                parts.Add(L.List);
                parts.Add(L.Track);
            }
            bool inside = parts.All(p =>
                p.X >= WindowLayout.Pad
                && p.Right <= L.W - WindowLayout.Pad
                && p.Y >= WindowLayout.Pad
                && p.Bottom <= L.H - WindowLayout.Pad
            );
            bool apart = true;
            for (int i = 0; i < parts.Count; i++)
            for (int j = i + 1; j < parts.Count; j++)
                apart &= !parts[i].Overlaps(parts[j]);
            string name = cw + "x" + ch + " text " + text + " list " + list + " buttons " + buttons;
            Check(inside && apart, "window parts inside the plate and apart: " + name, string.Join(" ", parts));
            if (list)
                Check(
                    L.ListRows >= WindowLayout.MinListRows,
                    "long text leaves the list its rows: " + name,
                    L.ListRows.ToString()
                );
        }
        var big = WindowLayout.Compute(2000, 2000, 640, 360, 10, 11, 11, false, new List<int>(), _ => 0, true, 14);
        Check(
            big.W == 640 - 2 * WindowLayout.CanvasMargin
                && big.H == 360 - WindowLayout.TopBand - WindowLayout.CanvasMargin,
            "a window larger than the screen is shrunk to it, less the strip of the game's menu button"
        );
    }

    static void WindowApi()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "k",
            @"
local all = {}
for i = 1, 2000 do all[i] = { text = 'Koi ' .. i, koi = i, color = i % 2 == 0 and '#ffcc00' or nil } end
WindowCreate('w', 'Koi <checklist>', 300, 250)
WindowSetList('w', all)
WindowAddSearch('w', function(text)
  local shown = {}
  for _, r in ipairs(all) do if r.text:find(text, 1, true) then shown[#shown + 1] = r end end
  WindowSetList('w', shown)
  Out('search', text, #shown)
end)
WindowAddButton('w', 'owned', 'Owned', function() Out('owned') end)
WindowAddButton('w', 'broken', 'Broken', function() error('button broke') end)
WindowAddButton('w', 'owned', 'Owned', function() Out('owned 2') end)
WindowOpen('w')
Out(WindowIsOpen('w'))
WindowCreate('bad', 'B', 100, 100)
Out(WindowSetList('bad', { { text = 'x', icn = 3 }, { text = 'y', icon = 'files/none.png' } }))
Out(WindowOpen('nope'), WindowIsOpen('nope'))
"
        );
        rig.Start();
        rig.Run(0.1);
        var w = rig.Windows.Windows.First(v => v.Id == "w");
        Check(w.Title == "Koi <checklist>", "title as typed", w.Title);
        Check(
            w.Rows.Count == 2000 && w.Rows[1].Icon.ItemId == WindowArea.KoiItemId && w.Rows[1].Icon.Meta == 2,
            "koi rows carry the koi icon"
        );
        Check(w.Rows[1].Color.HasValue && !w.Rows[0].Color.HasValue, "row colours");
        Check(w.Buttons.Count == 2, "the same button id replaces its button");
        Check(rig.Ui.Open["k/w"] && spy.Out[0] == "true", "open, and WindowIsOpen says so");
        var bad = rig.Windows.Windows.First(v => v.Id == "bad");
        Check(
            spy.Out[1] == "false" && bad.Rows.Count == 2 && bad.Rows[1].Text == "y" && bad.Rows[1].Icon == null,
            "an unknown row field and a missing icon file are left out; the rows show",
            spy.Out[1]
        );
        Check(
            rig.Env.ChatRead.Contains("\"icn\", which is not known (did you mean \"icon\"?)")
                && rig.Env.ChatRead.Contains("no file files/none.png"),
            "and named in a notice",
            rig.Env.ChatRead
        );
        Check(
            spy.Out[2] == "false\tnil" && rig.Env.ChatRead.Contains("there is no window \"nope\""),
            "unknown window: false, nil, named",
            spy.Out[2]
        );
        rig.Windows.SearchChanged(w, "Koi 19");
        rig.Run(0.05);
        Check(
            spy.Out.Contains("search\tKoi 19\t111") && w.Rows.Count == 111,
            "the search calls Lua with the text, which sets the list",
            w.Rows.Count.ToString()
        );
        rig.Windows.ButtonPressed(w, 0);
        Check(spy.Out.Last() == "owned 2", "a button calls its Lua function");
        int builds = rig.Ui.Builds;
        rig.Run(0.2);
        Check(rig.Ui.Builds == builds, "rows alone do not rebuild the window");
        rig.Windows.Dragged(w, new URect(10, 20, w.Layout.W, w.Layout.H));
        rig.Windows.ClosedByPlayer(w);
        Check(!rig.Ui.Open["k/w"], "closed by the player");
        rig.Windows.ButtonPressed(w, 1);
        Check(
            rig["k"].Status == ModStatus.Failed && rig.Env.ChatText.Contains("button broke"),
            "an error in a button's function is reported like any other"
        );
        Check(rig.Windows.Windows.Count == 0 && rig.Ui.Built.Count == 0, "a failed mod's windows are destroyed");
        Check(new UiPositions(rig["k"].DataDir).TryGet("window", "w", out _, out _), "the window position was saved");
    }

    static void HostSearch()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "s",
            @"
local rows = {}
for i = 1, 2000 do rows[i] = { text = (i % 3 == 0 and 'Red ' or 'Blue ') .. 'Koi ' .. i, koi = i } end
WindowCreate('w', 'Search', 300, 250)
WindowSetList('w', rows)
WindowAddSearch('w', 'name')
"
        );
        rig.Start();
        rig.Run(0.05);
        var w = rig.Windows.Windows.First(v => v.Id == "w");
        Check(w.HasSearch && w.SearchFn == null && w.SearchHint == "name", "a search without a function, with a hint");
        rig.Windows.SearchChanged(w, "red KOI 99");
        Check(
            w.Shown.Select(r => r.Text)
                .SequenceEqual(
                    new[]
                    {
                        "Red Koi 99",
                        "Red Koi 399",
                        "Red Koi 699",
                        "Red Koi 990",
                        "Red Koi 993",
                        "Red Koi 996",
                        "Red Koi 999",
                        "Red Koi 1299",
                        "Red Koi 1599",
                        "Red Koi 1899",
                        "Red Koi 1992",
                        "Red Koi 1995",
                        "Red Koi 1998",
                    }
                )
                && w.Rows.Count == 2000,
            "the host filters by every word, in any case, keeping the full list",
            string.Join(",", w.Shown.Select(r => r.Text))
        );
        rig.Windows.SearchChanged(w, "");
        Check(w.Shown.Count == 2000, "an empty search shows every row");
    }

    static void WindowButtons()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "b",
            @"
WindowCreate('w', 'Buttons', 300, 200)
WindowAddButton('w', 'all', 'All', function() Out('all') end)
WindowAddButton('w', 'colour', 'Colour', function() Out('colour') end)
WindowAddButton('w', 'end', 'End', function() Out('end') end)
WindowOpen('w')
function Relabel(text) Out(WindowSetButton('w', 'colour', { label = text })) end
function Select(on) Out(WindowSetButton('w', 'all', { selected = on })) end
function Wrong() Out(WindowSetButton('w', 'none', { selected = true }), WindowSetButton('w', 'all', { selcted = true })) end
function Again() WindowAddButton('w', 'colour', 'Next', function() Out('next') end) end
"
        );
        rig.Start();
        rig.Run(0.1);
        var w = rig.Windows.Windows.Single();
        int x3 = w.Layout.Buttons[2].X;
        int builds = rig.Ui.Builds;
        rig["b"].Lua.Call(rig["b"].Lua.Global("Relabel"), "Dark blue", Limits.Callback);
        rig.Run(0.1);
        Check(
            spy.Out.Last() == "true" && w.Layout.Buttons[1].W == 5 * 9 + WindowLayout.ButtonPad,
            "a new label resizes its button",
            w.Layout.Buttons[1].ToString()
        );
        Check(w.Layout.Buttons[2].X == x3 + 5 * 3, "the buttons after it move along", w.Layout.Buttons[2].ToString());
        Check(rig.Ui.Builds == builds + 1, "and the window is built again for the new widths");
        builds = rig.Ui.Builds;
        int refreshes = rig.Ui.Refreshes;
        rig["b"].Lua.Call(rig["b"].Lua.Global("Relabel"), "Dark pink", Limits.Callback);
        rig.Run(0.1);
        Check(
            rig.Ui.Builds == builds && rig.Ui.Refreshes > refreshes && w.Buttons[1].Label == "Dark pink",
            "a label of the same width only refreshes"
        );
        builds = rig.Ui.Builds;
        refreshes = rig.Ui.Refreshes;
        rig["b"].Lua.Call(rig["b"].Lua.Global("Select"), true, Limits.Callback);
        rig.Run(0.1);
        Check(
            w.Buttons[0].Selected && rig.Ui.Builds == builds && rig.Ui.Refreshes == refreshes + 1,
            "selecting a button refreshes it, without a rebuild"
        );
        rig["b"].Lua.Call(rig["b"].Lua.Global("Wrong"), Limits.Callback);
        Check(
            spy.Out.Last() == "false\tfalse"
                && rig.Env.ChatRead.Contains("has no button \"none\"")
                && rig.Env.ChatRead.Contains("did you mean \"selected\"?"),
            "an unknown button and an unknown option are refused and named",
            spy.Out.Last()
        );
        rig["b"].Lua.Call(rig["b"].Lua.Global("Again"), Limits.Callback);
        rig.Run(0.1);
        rig.Windows.ButtonPressed(w, 1);
        Check(
            w.Buttons.Count == 3 && w.Buttons[1].Label == "Next" && spy.Out.Last() == "next",
            "adding a button id again replaces its label and function in place"
        );
    }

    static void WindowClosing()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "c",
            @"
WindowCreate('w', 'Closing', 200, 120)
WindowCreate('v', 'Other', 200, 120)
WindowOpen('w')
WindowOpen('v')
function OnWindowClosed(id, by_player) Out('closed', id, by_player, WindowIsOpen(id)) end
function CloseOwn() WindowClose('w') Out('after close') end
function Flicker() WindowClose('v') WindowOpen('v') end
"
        );
        rig.Start();
        rig.Run(0.1);
        var w = rig.Windows.Windows.First(x => x.Id == "w");
        rig.Windows.ClosedByPlayer(w);
        rig.Run(0.05);
        Check(spy.Out.LastOrDefault() == "closed\tw\ttrue\tfalse", "X or Escape: OnWindowClosed( id, true )");
        rig.Windows.ClosedByPlayer(w);
        rig.Run(0.05);
        Check(spy.Out.Count(o => o.StartsWith("closed")) == 1, "closing a closed window calls nothing");
        rig.Run(WindowArea.ReopenBlockSeconds);
        var lua = rig["c"].Lua;
        lua.Call(lua.Global("WindowOpen"), "w", Limits.Callback);
        lua.Call(lua.Global("CloseOwn"), Limits.Callback);
        Check(spy.Out.Last() == "after close", "the mod's own close does not run Lua inside its call");
        rig.Run(0.05);
        Check(spy.Out.Last() == "closed\tw\tfalse\tfalse", "but follows it: OnWindowClosed( id, false )");
        lua.Call(lua.Global("Flicker"), Limits.Callback);
        rig.Run(0.1);
        Check(!spy.Out.Contains("closed\tv\tfalse\ttrue"), "nothing for a window that is open again by then");
        rig.Host.Reload("c");
        rig.Run(0.1);
        Check(!spy.Out.Any(o => o.StartsWith("closed\tv")), "nothing when the mod unloads", string.Join("|", spy.Out));
    }

    static void SearchToldToMod()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "s",
            @"
local rows = {}
for i = 1, 300 do rows[i] = (i % 3 == 0 and 'Red' or 'Blue') .. ' koi ' .. i end
WindowCreate('w', 'Search', 300, 250)
WindowSetList('w', rows)
WindowAddSearch('w', 'name')
WindowCreate('f', 'Lua search', 300, 250)
WindowAddSearch('f', function(text) Out('fn', text) end)
WindowSetList('f', rows)
WindowCreate('n', 'No search', 300, 250)
function OnWindowSearchChanged(id, text, shown) Out('changed', id, text, shown) end
function Read(id) Out(WindowGetSearch(id)) end
function Narrow() local red = {} for i = 1, 50 do red[i] = rows[i * 3] end WindowSetList('w', red) Read('w') end
"
        );
        rig.Start();
        rig.Run(0.05);
        var lua = rig["s"].Lua;
        lua.Call(lua.Global("Read"), "w", Limits.Callback);
        Check(spy.Out.Last() == "\t300", "before typing: no text, every row", spy.Out.Last());
        var w = rig.Windows.Windows.First(x => x.Id == "w");
        rig.Windows.SearchChanged(w, "red 1");
        Check(
            spy.Out.Last() == "changed\tw\tred 1\t47",
            "typing calls OnWindowSearchChanged with the count",
            spy.Out.Last()
        );
        lua.Call(lua.Global("Read"), "w", Limits.Callback);
        Check(spy.Out.Last() == "red 1\t47", "WindowGetSearch reads the same", spy.Out.Last());
        lua.Call(lua.Global("Narrow"), Limits.Callback);
        Check(spy.Out.Last() == "red 1\t23", "a new list is counted again under the same text", spy.Out.Last());
        rig.Windows.SearchChanged(rig.Windows.Windows.First(x => x.Id == "f"), "blue");
        Check(
            spy.Out.Last() == "fn\tblue" && !spy.Out.Any(o => o.StartsWith("changed\tf")),
            "a search with fn calls fn, not OnWindowSearchChanged"
        );
        lua.Call(lua.Global("Read"), "f", Limits.Callback);
        Check(spy.Out.Last() == "blue\t300", "and its count is every row", spy.Out.Last());
        lua.Call(lua.Global("Read"), "n", Limits.Callback);
        Check(spy.Out.Last() == "nil\tnil", "a window without a search: nil, nil", spy.Out.Last());
    }

    static void SearchSetByMod()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "s",
            @"
WindowCreate('w', 'Search', 300, 250)
WindowSetList('w', { 'Blue koi', 'Red koi', 'Blue fish' })
WindowAddSearch('w', 'name')
WindowCreate('f', 'Lua search', 300, 250)
WindowAddSearch('f', function(text) Out('fn', text) end)
WindowCreate('n', 'No search', 300, 250)
function OnWindowSearchChanged(id, text, shown) Out('changed', id, text, shown) end
function Set(id, text) Out('set', WindowSetSearch(id, text)) Out(WindowGetSearch(id)) end
"
        );
        rig.Start();
        rig.Run(0.05);
        var lua = rig["s"].Lua;
        void Set(string id, string text) => lua.Call(lua.Global("Set"), id, text, Limits.Callback);
        var w = rig.Windows.Windows.First(x => x.Id == "w");
        Set("w", "blue");
        Check(
            spy.Out.SequenceEqual(new[] { "set\ttrue", "blue\t2" }) && w.Shown.Count == 2,
            "the search text filters the host's list at once",
            string.Join("|", spy.Out)
        );
        Check(!spy.Out.Any(o => o.StartsWith("changed")), "OnWindowSearchChanged is not called for it");
        rig.Run(0.05);
        int refreshes = rig.Ui.Refreshes;
        Set("w", "");
        rig.Run(0.05);
        Check(
            spy.Out.Last() == "\t3" && w.Shown.Count == 3 && rig.Ui.Refreshes > refreshes,
            "an empty text shows every row again and the window is redrawn",
            spy.Out.Last()
        );
        Set("f", "red");
        Check(!spy.Out.Any(o => o.StartsWith("fn")), "a search's fn is not called for it");
        spy.Out.Clear();
        Set("w", new string('x', 70));
        Check(
            spy.Out[0] == "set\tfalse" && spy.Out[1] == new string('x', 60) + "\t0",
            "a text longer than the field is cut, and false",
            string.Join("|", spy.Out)
        );
        spy.Out.Clear();
        Set("n", "a");
        Check(
            spy.Out[0] == "set\tfalse" && rig.Env.ChatRead.Contains("has no search field; WindowAddSearch adds one"),
            "a window without a search field: false and a notice",
            rig.Env.ChatRead
        );
        rig.Windows.SearchChanged(w, "red");
        Check(spy.Out.Last() == "changed\tw\tred\t1", "the player's typing still calls OnWindowSearchChanged");
    }

    // A window as large as asked stays inside the screen and below the strip of the game's menu button, also when
    // dragged; at every canvas size.
    static void WindowKeepsMenuReachable()
    {
        foreach (var (cw, ch) in new[] { (640, 360), (800, 450) })
        {
            using var rig = new Rig();
            rig.Ui.W = cw;
            rig.Ui.H = ch;
            rig.Mod("big", "WindowCreate('w', 'Big', 4000, 4000) WindowOpen('w')");
            rig.Start();
            rig.Run(0.1);
            var w = rig.Windows.Windows.Single();
            Check(
                w.Rect.Y >= WindowLayout.TopBand
                    && w.Rect.Y + w.Rect.H <= ch
                    && w.Rect.X >= 0
                    && w.Rect.X + w.Rect.W <= cw,
                "a 4000x4000 window leaves the top strip free at " + cw + "x" + ch,
                w.Rect.ToString()
            );
            rig.Windows.Dragged(w, new URect(cw - 130, 0, w.Rect.W, w.Rect.H));
            Check(w.Rect.Y >= WindowLayout.TopBand, "dragged up, it stops under the strip", w.Rect.ToString());
        }
    }

    // A window the player closed is not reopened by the mod for a moment, except by the player's own key; repeated
    // tries say one thing.
    static void WindowStaysClosed()
    {
        using var rig = new Rig();
        rig.Mod(
            "trap",
            "WindowCreate('w', 'Trap', 200, 100) WindowCreate('v', 'Other', 200, 100) WindowOpen('w') refused = 0 "
                + "function OnWindowClosed(id) if not WindowOpen('w') then refused = refused + 1 end "
                + "if not WindowOpen('v') then refused = refused + 1 end end "
                + "TimerEvery(0.5, function() if not WindowIsOpen('w') and not WindowOpen('w') then refused = refused + 1 end end) "
                + "function OnKey(id) WindowOpen('w') end",
            null
        );
        rig.Write("trap", "settings.lua", "mod_settings = { { id = 'open', type = 'key', value_default = 'F7' } }");
        rig.Start();
        rig.Run(0.1);
        var w = rig.Windows.Windows.First(x => x.Id == "w");
        int Refused() => (int)rig["trap"].Lua.Global("refused").Read<double>();
        int Told() => rig.Env.ChatText.Split('\n').Count(l => l.Contains("right after you closed it"));
        rig.Windows.ClosedByPlayer(w);
        rig.Run(1.5);
        Check(
            !w.Open && !rig.Windows.Windows.Any(x => x.Open) && Refused() >= 4,
            "reopening from OnWindowClosed or a timer, or opening another window, is refused for a moment",
            "refused " + Refused()
        );
        Check(Told() == 1, "the mod's tries are said once", Told().ToString());
        rig.Ui.Down.Add("F7");
        rig.Run(0.05);
        Check(w.Open, "the player's own key opens it at once");
        rig.Windows.ClosedByPlayer(w);
        rig.Run(WindowArea.ReopenBlockSeconds + 0.6);
        Check(w.Open && Told() == 2, "after the moment the mod may open it again; a new close, a new notice");
    }

    // Escape three times quickly: when a mod fought the player over a window, all mod windows close and that mod is
    // named; an honest close followed by Escapes into the game's menu is not taken for that.
    static void EscapeHatch()
    {
        var p = new EscapePresses();
        Check(
            p.Press(10) == null && p.Press(10.5) == null && p.Press(11) == 10 && p.Press(11.2) == null,
            "three presses within the time complete it, then counting starts again"
        );
        Check(p.Press(14) == null && p.Press(16.5) == null, "slow presses do not");

        using var rig = new Rig();
        rig.Mod(
            "trap",
            "WindowCreate('w', 'Trap', 200, 100) WindowOpen('w') function OnWindowClosed() WindowOpen('w') end"
        );
        rig.Mod("fine", "WindowCreate('w', 'Fine', 200, 100) WindowOpen('w')");
        rig.Start();
        rig.Run(0.1);
        double since = rig.Env.Clock;
        rig.Windows.ClosedByPlayer(rig.Windows.Windows.First(w => w.ModId == "fine"));
        rig.Run(0.5);
        Check(rig.Windows.Panic(since) == null, "an honest close is no reason");
        rig.Windows.ClosedByPlayer(rig.Windows.Windows.First(w => w.ModId == "trap"));
        rig.Run(0.5);
        Check(
            rig.Windows.Panic(since) == "trap" && rig.Windows.Windows.All(w => !w.Open),
            "a mod that tried to reopen is named and every window is closed"
        );
    }
}
