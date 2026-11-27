using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lua;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;

// Text and values a mod hands over: everything that puts its text into rich text, a label or the log, and every
// walk of its tables and files, bounded before or while it runs (the watchdog only stops Lua).

sealed class HostileArea : Area
{
    public override string Name => "Hostile";

    protected internal override void Register(AreaSetup s)
    {
        s.Function("HN() -> n", "count", c => c.Return(Program.Hostile.Length));
        s.Function("H( i ) -> text", "one hostile text", c => c.Return(Program.Hostile[(int)c.Number(1, "i") - 1]));
    }
}

static partial class Program
{
    internal static readonly string[] Hostile =
    {
        "<b>bold</b>",
        "<link=1>bob hi</link>",
        "<link=1>%H",
        "<link=item:9999:69>[Koi]</link>",
        "<sprite=3>",
        "</noparse><color=red>x",
        "<noparse>%H:%M",
        "</color><size=0>",
        "\\n\\r\\t\\u003Cb\\u003E\\U0000003C",
        "x \\<b>",
        ":@3: emote :@12:",
        "rtl \u202E desrever \u202C",
        "zero\u200Bwidth\u200D<\u200Bb>",
        "line\nbreak <b>",
        "cr\rlf",
        "sep\u2028line\u2029para",
        "nel\u0085vt\u000Bff\u000C",
        "&lt;b&gt;",
        "[Server] restock in 5 minutes",
        new string('<', 2000) + "b>" + new string('x', 5000),
    };

    // What the chat or a tooltip would parse: a tag, a line break, an escape sequence (every backslash is followed by
    // a zero-width space), and in chat an emote. The host's own colour and link tags are allowed.
    static string RichProblem(string s, bool chat)
    {
        string body = Regex.Replace(
            s,
            "</?color(=#[0-9A-Fa-f]{6})?>|<link=smods:[0-9a-f]{16}><nobr><mark=#[0-9A-F]{8}>|</mark></nobr></link>",
            ""
        );
        if (body.Contains('<'))
            return "a tag";
        if (body.IndexOfAny(new[] { '\n', '\r', '\v', '\f', '\u2028', '\u2029', '\u0085' }) >= 0)
            return "a line break";
        for (int i = 0; i < body.Length; i++)
            if (body[i] == '\\' && (i + 1 >= body.Length || body[i + 1] != '\u200B'))
                return "an escape";
        if (chat && body.Contains(":@"))
            return "an emote";
        return null;
    }

    // Labels draw with rich text and escapes off, so only breaks matter there; in the log, any break forges a line.
    static string LineProblem(string s, bool tabs = false) =>
        s.Any(ch => (char.IsControl(ch) && !(tabs && ch == '\t')) || ch == '\u2028' || ch == '\u2029')
            ? "a line break"
            : null;

    static void HostileText()
    {
        int n = Hostile.Length;
        CheckEach("Tmp escapes every hostile text", Hostile, n, h => RichProblem(TextEscape.Tmp(h), false));
        CheckEach("Chat escapes every hostile text", Hostile, n, h => RichProblem(TextEscape.Chat(h), true));
        CheckEach("Plain is one line", Hostile, n, h => LineProblem(TextEscape.Plain(h)));
        CheckEach("PlainLines has no other break", Hostile, n, h =>
            TextEscape.PlainLines(h).Split('\n').Select(l => LineProblem(l)).FirstOrDefault(p => p != null)
        );
        CheckEach("LogLine is one capped line", Hostile, n, h =>
        {
            string log = TextEscape.LogLine(h, Limits.LogLineChars);
            return LineProblem(log, true) ?? (log.Length < Limits.LogLineChars + 50 ? null : "too long");
        });
        CheckEach(
            "a timestamp format with a tag, escape or break is refused",
            Hostile.Where(h => RichProblem(h, true) != null),
            17,
            h => TimestampFormat.Parse(h, out _) == null ? null : "accepted"
        );
        var stamps = Hostile
            .Append("%I:%M %p (%d.%m.%y) %%")
            .Append("[%H:%M|%S]-/,")
            .Select(h => TimestampFormat.Parse(h, out _)?.Format(new DateTime(2026, 11, 27, 6, 5, 0)))
            .Where(f => f != null);
        CheckEach("an accepted timestamp format makes safe text", stamps, 2, f => RichProblem(f, true));
        string line =
            "<color=#7FD4FF><link=7>bob</link>:</color> <color=#FFFFFF>"
            + TextEscape.Chat(string.Join(" ", Hostile))
            + "</color>";
        var words = Hostile.Select(h => new Highlight(TextEscape.Tmp(h.Trim()), "#FF0000")).ToList();
        string decorated = ChatDecorator.Decorate(line, "06:05", words);
        Check(
            RichProblem(Regex.Replace(decorated, "</?link(=7)?>", ""), true) == null,
            "highlights of hostile words add only colour tags",
            RichProblem(decorated, true)
        );

        // Through the API, as a mod uses it.
        using var rig = new Rig(new HostileArea());
        rig.Mod(
            "h",
            @"
function step(i)
  local s = H(i)
  GamePrint(s)
  GamePrintLink(s, s, print)
  NoticeShow(s, 0.5)
  ChatSetTimestamps(s)
  ChatHighlight(s, '#ff0000')
  print(s, s)
  HudText('h' .. i, s)
  WindowCreate('w' .. i, s, 200, 100)
  WindowSetText('w' .. i, s)
  WindowSetList('w' .. i, { { text = s } })
  WindowAddSearch('w' .. i, nil, s)
  WindowAddButton('w' .. i, 'b', s, function() end)
  CommandRegister(s, s)
  pcall(DataSave, s, 1)
  pcall(TimerAfter, s, function() end)
end
function OnItemTooltip(item) return H(item.id) end
"
        );
        rig.Mod("e", "function fail(s) error(s) end");
        rig.Write(
            "x",
            "mod.xml",
            "<Mod name=\"x&#10;[Info   :ShorelandsMods] forged\" version=\"1&#10;[Error  :ShorelandsMods] forged\" />"
        );
        rig.Start();
        var notices = new List<string>();
        for (int i = 1; i <= n; i++)
        {
            rig.Host.Call(rig["h"], "step", rig["h"].Lua.Global("step"), 1, new Args(i));
            rig.Run(0.2);
            notices.AddRange(rig.Ui.Notices.SelectMany(x => x.lines));
            rig.Run(1);
        }
        Check(rig["h"].Status == ModStatus.Loaded, "the hostile mod still runs", rig["h"].Reason);
        CheckEach("chat lines are safe", rig.Env.ChatLines, 2 * n, c => RichProblem(c, true));
        CheckEach("no chat line is longer than the host's cap", rig.Env.ChatLines, 2 * n, c =>
            c.Length < 2 * Limits.HostLineChars + 40 ? null : c.Length + " characters"
        );
        CheckEach("notice lines are one line", notices.Distinct(), n, l => LineProblem(l));
        CheckEach("HUD lines are one line", rig.Ui.Shown.Values, n, w =>
            w.Lines.Select(l => LineProblem(l)).FirstOrDefault(p => p != null)
        );
        CheckEach("window parts are one line", rig.Ui.Built.Values, 8, w =>
            new[] { w.Title, w.SearchHint }
                .Concat(w.TextLines)
                .Concat((w.Rows ?? new List<ListRow>()).Select(r => r.Text))
                .Concat(w.Buttons.Select(b => b.Label))
                .Select(l => LineProblem(l))
                .FirstOrDefault(p => p != null)
        );
        var tips = Enumerable
            .Range(1, n)
            .Select(id => rig.Tooltips.Lines(TooltipSource.OwnBag, () =>
                {
                    var t = new Lua.LuaTable();
                    t["id"] = id;
                    t["name"] = "Rod";
                    return t;
                }))
            .ToList();
        CheckEach("tooltip lines are safe", tips, n, t => t == null ? "no line" : RichProblem(t, false));
        CheckEach("an error line from the mod's error text is safe", Hostile, n, h =>
        {
            rig.Host.Reload("e");
            rig.Host.Call(rig["e"], "fail", rig["e"].Lua.Global("fail"), 1, new Args(h));
            return rig["e"].Status != ModStatus.Failed ? "not failed" : RichProblem(rig.Env.ChatLines.Last(), true);
        });
        Check(
            rig["x"].Manifest != null && LineProblem(rig["x"].Manifest.Name + rig["x"].Manifest.Version) == null,
            "mod.xml fields are one line"
        );
        rig.Host.ReloadAll();
        CheckEach("log lines are one line", rig.Env.LogLines, n, l => LineProblem(l, true));
    }

    const string HostilePrelude =
        @"
BIG = string.rep('a<\\\n:@', 200000)
LIST = {} for i = 1, 100000 do LIST[i] = i end
WIDE = {} for i = 1, 20000 do WIDE['k' .. i] = i end
DEEP = {} local d = DEEP for i = 1, 20000 do d.x = {} d = d.x end
CYC = {} CYC.self = CYC
SPARSE = { [2^31] = 1, [2^53] = 1, [-1] = 1, [0.5] = 1 }
HITS = 0
local function hit() HITS = HITS + 1 end
META = setmetatable({}, {
  __index = function() hit() return 'x' end,
  __len = function() hit() return 2^31 end,
  __pairs = function(t) hit() return function() hit() return 'k', 1 end, t, nil end,
})
function DAG(n) local x = 1 for i = 1, n do x = { x, x } end return x end
local function rows(make) local r = {} for i = 1, 10000 do r[i] = make(i) end return r end
ROWS_BIG = rows(function() return BIG end)
ROWS_TEXT = rows(function() return { text = BIG, color = BIG } end)
ROWS_WIDE = rows(function() return WIDE end)
ROWS_ICON = rows(function() return { text = 'x', icon = BIG } end)
ROWS_FILE = rows(function(i) return { text = 'x', icon = 'files/none' .. i .. '.png' } end)
WindowCreate('w', 't', 200, 200)
WindowAddSearch('w')
WindowAddButton('w', 'b', 'B', function() end)
function OnItemTooltip() return BIG end
";

    // Before the bounds (offline): the shared sub-tables 4.6 s, 10,000 rows of long texts about 28 s, of wide tables
    // about 160 s, wide options 20,000 notices.
    static readonly (string Name, string Lua)[] HostileCalls =
    {
        ("GamePrint long text", "GamePrint(BIG)"),
        ("GamePrintLink long text and labels", "GamePrintLink(BIG, BIG, print, BIG, print, BIG, print, BIG, print)"),
        ("print long texts", "print(unpack(ROWS_BIG, 1, 200))"),
        ("NoticeShow long text", "NoticeShow(BIG)"),
        ("HudText long text", "HudText('a', BIG)"),
        ("HudText long id", "HudText(BIG, 'x')"),
        ("HudText wide opts", "HudText('a', 'x', WIDE)"),
        ("HudText metatable opts", "HudText('a', 'x', META)"),
        ("HudText long anchor and colour", "HudText('a', 'x', { anchor = BIG, color = BIG })"),
        ("HudIcon long path", "HudIcon('i', BIG)"),
        ("HudBar wide opts", "HudBar('b', 0.5, WIDE)"),
        ("WindowCreate long id and title", "WindowCreate(BIG, BIG, 100, 100)"),
        ("WindowSetText long text", "WindowSetText('w', BIG)"),
        ("WindowSetList 100,000 rows", "WindowSetList('w', LIST)"),
        ("WindowSetList long texts", "WindowSetList('w', ROWS_BIG)"),
        ("WindowSetList long fields", "WindowSetList('w', ROWS_TEXT)"),
        ("WindowSetList wide rows", "WindowSetList('w', ROWS_WIDE)"),
        ("WindowSetList long icon paths", "WindowSetList('w', ROWS_ICON)"),
        ("WindowSetList missing icon files", "WindowSetList('w', ROWS_FILE)"),
        ("WindowSetList metatable", "WindowSetList('w', META)"),
        ("WindowAddSearch long hint", "WindowAddSearch('w', BIG)"),
        ("WindowSetSearch long text", "WindowSetSearch('w', BIG)"),
        ("WindowAddButton long id and label", "WindowAddButton('w', BIG, BIG, print)"),
        ("WindowSetButton wide opts", "WindowSetButton('w', 'b', WIDE)"),
        ("WindowSetButton long label", "WindowSetButton('w', 'b', { label = BIG })"),
        ("ChatHighlight long word", "ChatHighlight(BIG, '#ff0000')"),
        ("ChatHighlight long colour", "ChatHighlight('x', BIG)"),
        ("ChatSetTimestamps long format", "ChatSetTimestamps(BIG)"),
        ("CommandRegister long name", "CommandRegister(BIG, BIG)"),
        ("SoundPlay long path", "SoundPlay(BIG)"),
        ("SoundLoop long id and path", "SoundLoop(BIG, BIG)"),
        ("ExportText long text", "ExportText('a', BIG)"),
        ("ExportText long name", "ExportText(BIG, 'x')"),
        ("DataSave shared sub-tables", "DataSave('k', DAG(22))"),
        ("DataSave long list", "DataSave('k', LIST)"),
        ("DataSave wide map", "DataSave('k', WIDE)"),
        ("DataSave deep", "DataSave('k', DEEP)"),
        ("DataSave cycle", "DataSave('k', CYC)"),
        ("DataSave sparse keys", "DataSave('k', SPARSE)"),
        ("DataSave metatable", "DataSave('k', META)"),
        ("DataSave long text", "DataSave('k', BIG)"),
        ("DataSave long key", "DataSave(BIG, 1)"),
        ("DataSave rows of long texts", "DataSave('k', ROWS_BIG)"),
        ("DataLoad long key", "DataLoad(BIG)"),
        ("SessionSet shared sub-tables", "SessionSet('k', DAG(22))"),
        ("SessionSet long list", "SessionSet('k', LIST)"),
        ("ModSettingGet long id", "ModSettingGet(BIG)"),
        ("ModSettingSet long id", "ModSettingSet(BIG, 1)"),
        ("ColorFromHex long text", "ColorFromHex(BIG)"),
        ("TextDuration long style", "TextDuration(5, BIG)"),
    };

    // Each call, with the frame after it, ends far under the old costs and gives at most four notices (a log and a
    // chat line each).
    static void HostileArguments()
    {
        using var rig = new Rig(new SoundArea(new FakeAudio()));
        rig.Mod(
            "hostile",
            HostilePrelude
                + "CASES = {\n"
                + string.Join(",\n", HostileCalls.Select(c => "function() " + c.Lua + " end"))
                + "\n}"
        );
        rig.Write(
            "hostile",
            "settings.lua",
            "mod_settings = { { id = 'speed', value_default = 1, value_min = 0, value_max = 5 } }"
        );
        rig.Start();
        var mod = rig["hostile"];
        Check(mod.Status == ModStatus.Loaded, "hostile mod loads", mod.Reason);
        if (mod.Lua == null)
            return;
        var cases = mod.Lua.Global("CASES").Read<LuaTable>();
        for (int i = 0; i < HostileCalls.Length; i++)
        {
            int lines = rig.Env.LogLines.Count + rig.Env.ChatLines.Count;
            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            string stopped = "";
            try
            {
                mod.Lua.Call(cases[i + 1], Limits.Init);
            }
            catch (LuaRuntimeException) { }
            catch (BudgetExceeded e)
            {
                stopped = ", stopped: " + e.What;
            }
            rig.Run(0.05);
            double ms = sw.Elapsed.TotalMilliseconds;
            long made = GC.GetAllocatedBytesForCurrentThread() - a0;
            int said = rig.Env.LogLines.Count + rig.Env.ChatLines.Count - lines;
            Check(
                made < 64L << 20 && said <= 8 && stopped == "",
                "hostile call bounded: " + HostileCalls[i].Name,
                (made >> 20) + " MB allocated, " + ms.ToString("0") + " ms, " + said + " lines" + stopped
            );
        }
        long tipBytes = GC.GetAllocatedBytesForCurrentThread();
        string tip = rig.Tooltips.Lines(ShorelandsMods.Core.Ui.TooltipSource.OwnBag, () => new LuaTable(0, 4));
        tipBytes = GC.GetAllocatedBytesForCurrentThread() - tipBytes;
        Check(
            tipBytes < 256 << 10 && tip.Length < 200,
            "a long tooltip result is cut before it is escaped",
            (tipBytes >> 10) + " KB allocated"
        );
        Check(mod.Lua.Global("HITS").ToString() == "0", "the host's walks run no metamethod");
    }

    // The walk stops at the bytes left, so sharing cannot multiply it; a value that fits converts as before.
    static void DataWalkBounded()
    {
        LuaValue x = 1;
        var levels = new List<LuaTable>();
        for (int i = 0; i < 30; i++)
        {
            var t = new LuaTable(2, 0);
            t[1] = x;
            t[2] = x;
            levels.Add(t);
            x = t;
        }
        var json = LuaJson.ToJson(x, Limits.DataBytes, out var problem, out _);
        Check(json == null && problem == JsonProblem.TooBig, "2^30 shared leaves stop at the cap", problem.ToString());
        var map = new LuaTable(0, 2);
        map["n"] = 12;
        map["list"] = levels[16];
        LuaJson.ToJson(map, Limits.DataBytes, out problem, out _);
        Check(problem == JsonProblem.TooBig, "a shared table is charged at every use, also inside a map");
        var small = new LuaTable(0, 2);
        small["a"] = 1.5;
        small["b"] = new LuaTable(0, 0);
        json = LuaJson.ToJson(small, 20, out problem, out _);
        Check(
            problem == JsonProblem.None && json.ToString(Newtonsoft.Json.Formatting.None) == "{\"a\":1.5,\"b\":[]}",
            "a value under the cap converts",
            json?.ToString()
        );
        LuaJson.ToJson(small, 10, out problem, out _);
        Check(problem == JsonProblem.TooBig, "the same value over a smaller cap does not");
    }

    static List<SettingDef> ReadSettings(string src)
    {
        string dir = TempDir("settings-bounds");
        File.WriteAllText(Path.Combine(dir, "settings.lua"), src);
        var rt = ModRuntime.ForSettings("s", dir, (_, _) => { });
        rt.RunFile("settings.lua", Limits.Init);
        return SettingsSchema.Read(rt.Global("mod_settings"), n => n == "F7");
    }

    // What settings.lua builds is capped before it is walked, cycles are found, each problem is a sentence.
    static void SettingsBounds()
    {
        var cases = new (string Src, string Expect)[]
        {
            (
                "mod_settings = {} for i = 1, 2000 do mod_settings[i] = { id = 's' .. i, value_default = true } end",
                "more than 100 settings"
            ),
            (
                "local h = { not_setting = true } mod_settings = {} for i = 1, 20000 do mod_settings[i] = h end",
                "more than 200 entries"
            ),
            ("local t = {} t[1] = { settings = t } mod_settings = t", "the category at entry 1 contains itself"),
            (
                "local t = { { id = 'a', value_default = true } } "
                    + "for i = 1, 8 do t = { { settings = t } } end mod_settings = t",
                "nested at most 4 deep"
            ),
            (
                "local v = {} for i = 1, 1000 do v[i] = { 'v' .. i, 'V' } end "
                    + "mod_settings = { { id = 'c', value_default = 'v1', values = v } }",
                "values has 1000 entries; at most 64"
            ),
            (
                "mod_settings = { { id = 'a', ui_name = string.rep('n', 5000), value_default = true } }",
                "ui_name has 5000 characters; at most 100"
            ),
            (
                "mod_settings = { { id = 'a', ui_description = string.rep('d', 600), value_default = true } }",
                "ui_description has 600 characters; at most 500"
            ),
            (
                "mod_settings = { { id = string.rep('i', 1000000), value_default = true } }",
                "the id has 1000000 characters; at most 64"
            ),
            (
                "mod_settings = { { id = 'c', value_default = 'a', values = { { 'a', string.rep('L', 200) } } } }",
                "value 1 has an id or label over 100 characters"
            ),
        };
        foreach (var (src, expect) in cases)
        {
            string p = Problem(() => ReadSettings(src));
            Check(p.Contains(expect), "settings.lua bound: " + expect, p);
        }
        var defs = ReadSettings(
            "local c = {} for i = 1, 25 do c[i] = { id = 'c' .. i, value_default = 1, value_min = 0, value_max = 9 } end "
                + "mod_settings = { { category_id = 'a', settings = c }, { not_setting = true } } "
                + "for i = 1, 75 do mod_settings[#mod_settings + 1] = { id = 's' .. i, value_default = true } end"
        );
        Check(defs.Count == 100, "100 settings in a category and with a header load", defs.Count.ToString());
    }

    // mod.xml, sounds.xml and cues.xml: sizes before parsing, entries, attributes and the report capped.
    static void XmlBounds()
    {
        string dir = TempDir("xml-bounds");
        string manifest = Path.Combine(dir, "mod.xml");
        File.WriteAllText(manifest, "<Mod name=\"x\" description=\"" + new string('d', 20000) + "\" />");
        Check(Problem(() => ModManifest.Read(manifest)).Contains("over 16 KB"), "a mod.xml over 16 KB is not parsed");
        Check(
            Problem(() => ModManifest.Parse("<Mod name=\"" + new string('n', 61) + "\" />"))
                .Contains("name has 61 characters; at most 60"),
            "a long mod name is a sentence"
        );
        Check(
            Problem(() => ModManifest.Parse("<Mod name=\"x\" description=\"" + new string('d', 1001) + "\" />"))
                .Contains("at most 1000"),
            "a long description is a sentence"
        );
        string sounds = Path.Combine(dir, "sounds.xml");
        File.WriteAllText(sounds, "<Sounds>" + new string(' ', 70 * 1024) + "</Sounds>");
        var problems = new List<string>();
        Check(
            AssetXml.Read(sounds, problems) == "" && problems.Single().Contains("over 64 KB"),
            "a sounds.xml over 64 KB is not read"
        );

        problems.Clear();
        AssetXml.Sounds("<Sounds>" + string.Concat(Enumerable.Repeat("<x/>", 5000)) + "</Sounds>", problems);
        Check(
            problems.Count == AssetXml.MaxProblems + 1 && problems.Last() == "and 4980 more problems",
            "5,000 unknown elements make a capped report",
            problems.Count + " " + problems.LastOrDefault()
        );
        problems.Clear();
        var attrs = string.Concat(Enumerable.Range(1, 3000).Select(i => " a" + i + "=\"" + i + "\""));
        AssetXml.Cues("<Cues><Cue slot=\"fishing_bite\"" + attrs + " /></Cues>", problems);
        Check(
            problems.Count == 1 && problems[0].Contains("more than 16 attributes"),
            "an entry with 3,000 attributes is one problem",
            string.Join("|", problems.Take(3))
        );
        problems.Clear();
        string longValue = "<Sound name=\"chat\" volume=\"" + new string('9', 2000) + "\"/>";
        AssetXml.Sounds("<Sounds>" + string.Concat(Enumerable.Repeat(longValue, 30)) + "</Sounds>", problems);
        Check(
            problems.Count > 0 && problems.All(p => p.Length < 200),
            "attribute values are cut in the report",
            problems.Max(p => p.Length).ToString()
        );
    }
}
