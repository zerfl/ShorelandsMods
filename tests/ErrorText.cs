using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Lua;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;

// What an author reads when something is wrong: errors, syntax hints, argument problems, soft values,
// unknown options, the spelling "colour".
static partial class Program
{
    static void Errors()
    {
        ModRuntime.WarmUp(e => e.Message);
        var known = new[] { "WalletGetCurrency", "GamePrint", "TimerAfter" };
        string Run(string src)
        {
            string dir = TempDir("errors");
            File.WriteAllText(Path.Combine(dir, "init.lua"), src);
            try
            {
                new ModRuntime("e", dir).RunFile("init.lua", Limits.Callback);
                return "no error";
            }
            catch (Exception e)
            {
                return ErrorText.FromException(e, Limits.Callback, known).ToString();
            }
        }
        // Real Lua-CSharp messages, so a change in its wording shows up here.
        var cases = new (string src, string expect)[]
        {
            ("x = = 1", "init.lua line 1: this is not valid Lua: unexpected symbol near '='"),
            ("function f()\n  local a = 1\n", "init.lua line 3: 'end' is missing (to close the 'function' on line 1)"),
            ("s = \"abc", "init.lua line 1: a text in quotes is not closed"),
            (
                "\nWalletGetCurrecny()",
                "init.lua line 2: there is no function called WalletGetCurrecny. Did you mean WalletGetCurrency?"
            ),
            ("Frobnicate()", "init.lua line 1: there is no function called Frobnicate"),
            ("local t = nil\nprint(t.x)", "init.lua line 2: t is nil, so it has no fields to read (is it set yet?)"),
            (
                "local start\nlocal x = 5 - start",
                "init.lua line 2: tried to subtract numbers, but start is nil (is it set yet?)"
            ),
            (
                "local name\nlocal s = 'a' .. name",
                "init.lua line 2: tried to join text, but one of them is nil (is it set yet?)"
            ),
            (
                "local x = 1 < nil",
                "init.lua line 1: tried to compare a number with nil (< and > need two numbers or two texts)"
            ),
            ("local x = 5\nx()", "init.lua line 2: x is a number, not a function"),
            ("string.rep()", "init.lua line 1: argument 1 of rep is wrong: value expected"),
            ("error('boom')", "init.lua line 1: boom"),
        };
        foreach (var (src, expect) in cases)
        {
            string got = Run(src);
            Check(got == expect, "error text for " + src.Replace("\n", "\\n"), got);
        }
        string loop;
        using (new ManualClock())
            loop = Run("while true do end");
        Check(
            loop == "init.lua line 1: it ran too long (more than 200,000 steps in one call); an endless loop?",
            "error text for an endless loop",
            loop
        );
        Check(ErrorText.Suggest("TimerAftr", known) == "TimerAfter", "suggestion for a typo");
        Check(ErrorText.Suggest("Inventory", known) == null, "no suggestion for an unrelated name");
    }

    static void SyntaxHints()
    {
        string dir = TempDir("hints");
        string Run(string src)
        {
            File.WriteAllText(Path.Combine(dir, "init.lua"), src);
            try
            {
                new ModRuntime("e", dir).RunFile("init.lua", Limits.Callback);
                return "no error";
            }
            catch (Exception e)
            {
                return ErrorText.FromException(e, Limits.Callback, new string[0], dir).ToString();
            }
        }
        // Real Lua-CSharp messages for each slip.
        var cases = new (string src, string expect)[]
        {
            (
                "x = 1\n# a note",
                "init.lua line 2: this is not valid Lua: unexpected symbol near '#'. Comments start with --, not #."
            ),
            (
                "x = 1 // a note",
                "init.lua line 1: this is not valid Lua: unexpected symbol near '/'. Comments start with --, not //."
            ),
            ("if x != 1 then end", "init.lua line 1: 'then' is missing before '!'. Not equal is ~= in Lua, not !=."),
            (
                "x = a != b",
                "init.lua line 1: this is not valid Lua: unexpected symbol near '!'. Not equal is ~= in Lua, not !=."
            ),
            (
                "if !done then end",
                "init.lua line 1: this is not valid Lua: unexpected symbol near '!'. Lua writes not x, not !x."
            ),
            ("if a && b then end", "init.lua line 1: 'then' is missing before '&'. Lua writes and, not &&."),
            (
                "x = a || b",
                "init.lua line 1: this is not valid Lua: unexpected symbol near '|'. Lua writes or, not ||."
            ),
            (
                "x = 1\nx += 2",
                "init.lua line 2: this is not valid Lua: syntax error near '+'. Lua has no +=: write x = x + 2."
            ),
            (
                "t = {}\nt.n -= 1",
                "init.lua line 2: this is not valid Lua: syntax error near '-'. Lua has no -=: write t.n = t.n - 1."
            ),
            (
                "x = 1\nx++",
                "init.lua line 2: this is not valid Lua: syntax error near '+'. Lua has no ++: write x = x + 1."
            ),
            (
                "if a then\n  x = 1\nelse if b then\n  x = 2\nend",
                "init.lua line 5: 'end' is missing (to close the 'if' on line 1). The else if on line 3 starts a new if that needs its own end; write elseif as one word."
            ),
            (
                "function f()\n  if a then\n    x = 1\nend",
                "init.lua line 4: 'end' is missing (to close the 'function' on line 1). Every function, if, for and while needs its own end."
            ),
            (
                "if a\n  x = 1\nend",
                "init.lua line 2: 'then' is missing before 'x'. Write then after the condition: if x > 1 then ... end."
            ),
            (
                "for i = 1, 3\n  x = i\nend",
                "init.lua line 2: 'do' is missing before 'x'. Write do after for ... and while ...: for i = 1, 3 do ... end."
            ),
            (
                "if a === b then end",
                "init.lua line 1: this is not valid Lua: unexpected symbol near '='. Equal is == in Lua, not ===."
            ),
            (
                "if a then\nelif b then\nend",
                "init.lua line 2: this is not valid Lua: syntax error near 'b'. Lua writes elseif, not elif."
            ),
            (
                "function f() {\n  x = 1\n}",
                "init.lua line 1: this is not valid Lua: unexpected symbol near '{'. Lua has no { } around blocks: write if x then ... end, function f() ... end."
            ),
            ("if a then\n  x = 1\nend\nend", "init.lua line 4: this end has nothing to close (one end too many?)"),
            // No hint where the line does not show the slip.
            ("x = = 1", "init.lua line 1: this is not valid Lua: unexpected symbol near '='"),
            (
                "x = 1 -- a # in a comment\ny = = 2",
                "init.lua line 2: this is not valid Lua: unexpected symbol near '='"
            ),
            ("t = {}\nx = #t", "no error"),
        };
        foreach (var (src, expect) in cases)
        {
            string got = Run(src);
            Check(got == expect, "hint for " + src.Replace("\n", "\\n"), got);
        }

        using var rig = new Rig();
        rig.Mod("h", "dofile_once('files/util.lua')");
        rig.Write("h", "files/util.lua", "local n = 0\nn += 1 // count");
        rig.Start();
        Check(
            rig["h"].Status == ModStatus.Failed
                && rig["h"].Reason
                    == "files/util.lua line 2: this is not valid Lua: syntax error near '+'. Lua has no +=: write n = n + 1.",
            "a failing mod's sentence has the hint for the line of the file that failed",
            rig["h"].Reason
        );
    }

    static void Arguments()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "args",
            @"
for _, f in ipairs({
  function() TimerAfter('soon', function() end) end,
  function() TimerAfter(1) end,
  function() GamePrint() end,
  function() DataSave(5, 1) end,
  function() NoticeShow('x', 'long') end,
  function() ColorFromHex(5) end,
}) do local ok, e = pcall(f) Out(e) end
local c = ColorFromHex('#ff8000')
Out(c.r, c.g > 0.5 and c.g < 0.51, c.b, c.a)
"
        );
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        string[] expect =
        {
            "TimerAfter: argument 1 (seconds) must be a number, but it is the text \"soon\"",
            "TimerAfter: argument 2 (fn) must be a function, but it is nil",
            "GamePrint: argument 1 (text) must be text, but it is nil",
            "DataSave: argument 1 (key) must be text, but it is the number 5",
            "NoticeShow: argument 2 (seconds) must be a number, but it is the text \"long\"",
            "ColorFromHex: argument 1 (hex) must be a colour (\"#rrggbb\" or a table from ColorFromHex), but it is the number 5",
        };
        for (int i = 0; i < expect.Length; i++)
            Check(
                i < o.Count && o[i].EndsWith(expect[i]),
                "argument error: " + expect[i],
                i < o.Count ? o[i] : "missing"
            );
        Check(o.Contains("1\ttrue\t0\t1"), "ColorFromHex values", string.Join("|", o));
    }

    static int softN;

    // Runs one call in a fresh mod of a started rig: what it returned (or "error: ..."), the mod's chat notices, and
    // whether the mod still runs.
    static (string Result, string Notices, bool Loaded) Soft(Rig rig, string call, Action<string> setup = null)
    {
        string id = "soft" + ++softN;
        rig.Mod(
            id,
            "local ok, r = pcall(function() return " + call + " end) R = ok and tostring(r) or 'error: ' .. tostring(r)"
        );
        setup?.Invoke(id);
        rig.Host.Reload(id);
        var mod = rig[id];
        return (
            mod.Lua?.Global("R").ToString() ?? "",
            string.Join("\n", rig.Env.ChatLines.Where(l => l.Contains("[" + id + "]"))).Replace('\uFF02', '"'),
            mod.Status == ModStatus.Loaded
        );
    }

    // A value out of range or unknown is clamped or left out with one notice; a wrong type or missing argument stays
    // an error.
    static void SoftValues()
    {
        using var rig = new Rig();
        rig.Start();
        var cases = new (string Call, string Result, string Notice)[]
        {
            ("NoticeShow('x', 100)", "false", "NoticeShow: seconds must be from 0.5 to 30; 100 was changed to 30"),
            (
                "type(TimerEvery(0, function() end))",
                "number",
                "seconds must be from 0.05 to 86400; 0 was changed to 0.05"
            ),
            ("ChatHighlight(string.rep('x', 59), '#ff0000')", "false", "has 59 characters; 1 to 40 can be highlighted"),
            ("ChatSetTimestamps('%H:%Q')", "false", "%Q, which is not supported"),
            ("HudText('t', 'x', { anchor = 'upper' })", "false", "the anchor \"upper\" is not known"),
            ("HudIcon('i', 0)", "false", "icon must be an item id"),
            ("WindowOpen('nope')", "false", "there is no window \"nope\""),
            ("WindowIsOpen('nope')", "nil", "there is no window \"nope\""),
            ("WindowCreate('w', 't', 9000, 50)", "false", "width must be from 1 to 4000; 9000 was changed to 4000"),
            ("ModSettingGet('speed')", "nil", "has no settings.lua"),
            ("DataSave(string.rep('k', 101), 1)", "false", "a key must be 1 to 100 characters"),
            ("SessionSet('rate', 0/0)", "false", "finite, not nan"),
            ("ExportText('../evil', 'x')", "false", "may only use letters"),
            (
                "CommandRegister('Bad Name', 'x')",
                "false",
                "CommandRegister: a command name must start with a lower case letter"
            ),
            ("ColorFromHex('red')", "nil", "must be a colour like \"#ffcc00\", not \"red\""),
            ("TextDuration(5, 'weird')", "0:05", "the style \"weird\" is not known"),
            ("TextNumber(1.5, 9)", "1.500000", "decimals must be from 0 to 6"),
        };
        CheckEach("a value out of range or unknown is refused with a notice, not an error", cases, cases.Length, c =>
        {
            var r = Soft(rig, c.Call);
            return r.Result == c.Result && r.Loaded && r.Notices.Contains(c.Notice)
                ? null
                : r.Result + " | " + r.Notices;
        });
        var errors = new (string Call, string Error)[]
        {
            ("ChatHighlight(5, '#ffffff')", "argument 1 (word) must be text"),
            ("HudText('t', 'x', { size = 'big' })", "option size must be a number"),
            ("HudText('t', 'x', { [1] = 'top' })", "must be a table of named options"),
            ("WindowCreate('w', 't', 100, 100) and WindowSetList('w', { true })", "row 1 must be a text or a table"),
        };
        CheckEach("a wrong type stays an error", errors, errors.Length, c =>
        {
            var r = Soft(rig, c.Call);
            return r.Result.StartsWith("error: ") && r.Result.Contains(c.Error) ? null : r.Result;
        });

        var once = Soft(
            rig,
            "ChatHighlight(string.rep('a', 50), '#ffffff') and 1 or ChatHighlight(string.rep('b', 60), '#ffffff') and 2 "
                + "or NoticeShow('x', 99) and 3 or 4"
        );
        Check(
            once.Result == "4"
                && once.Notices.Split('\n').Length == 2
                && once.Notices.Contains("has 50 characters")
                && !once.Notices.Contains("has 60"),
            "one notice per function and argument, the first value named",
            once.Notices
        );
        var cap = Soft(
            rig,
            "(function() for i = 1, 33 do ok = ChatHighlight('w' .. i, '#ffffff') end return ok end)()"
        );
        Check(
            cap.Result == "false" && cap.Notices.Contains("already highlights 32 words, so \"w33\""),
            "the 33rd highlight is left out with a notice",
            cap.Result + " " + cap.Notices
        );
    }

    // Three unknown options are named per call; the next call names the ones not named yet.
    static void UnknownOptionsNamed()
    {
        using var rig = new Rig();
        rig.Mod("u", "for i = 1, 2 do HudText('a', 'x', { q1 = 1, q2 = 1, q3 = 1, q4 = 1, anchor = 'top' }) end");
        rig.Start();
        int named = rig.Env.ChatLines.Count(l => l.Contains("is not known"));
        Check(named == 4, "the fourth unknown option is named by the second call", named.ToString());
    }

    static void ColourSpelling()
    {
        using var rig = new Rig();
        rig.Mod(
            "c",
            @"
ok1 = HudText('t', 'x', { colour = '#ff0000' })
WindowCreate('w', 'W', 200, 200)
ok2 = WindowSetList('w', { { text = 'a', colour = '#00ff00' }, { text = 'b', color = '#0000ff' } })
ok3 = HudText('u', 'x', { colour = 'pink' })
"
        );
        rig.Start();
        rig.Run(0.1);
        var lua = rig["c"].Lua;
        var hud = rig.Hud.Widgets.First(x => x.Id == "t");
        var rows = rig.Windows.Windows.First().Rows;
        Check(
            lua.Global("ok1").ToString() == "true" && hud.Color is Rgba h && h.R == 1 && h.G == 0,
            "HudText takes colour",
            lua.Global("ok1").ToString()
        );
        Check(
            lua.Global("ok2").ToString() == "true"
                && rows[0].Color is Rgba r
                && r.G == 1
                && rows[1].Color is Rgba b
                && b.B == 1,
            "a list row takes colour and color"
        );
        Check(
            lua.Global("ok3").ToString() == "false" && rig.Env.ChatRead.Contains("the option colour must be a colour"),
            "a wrong colour names the option as the mod spelt it",
            rig.Env.ChatRead
        );
        Check(!rig.Env.ChatRead.Contains("not known"), "colour is not an unknown option");
    }
}
