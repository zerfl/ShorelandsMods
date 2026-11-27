using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;

// Chat: decoration, the mark on a mod's lines, caps, delivery to OnChatMessage, and GamePrintLink (a click runs
// only the owning mod's function, under the guard; a mod's text never becomes a tag; links die with the mod).
static partial class Program
{
    static void ChatDecoration()
    {
        var hl = new List<Highlight> { new Highlight("bob", "#FF0000") };
        string line =
            "<color=#FFFFFF><link=p1>Bob</link>:</color> <color=#DDDDDD>hi bob, bobby and <link=item:5:0>bob</link> <sprite=3></color>";
        string d = ChatDecorator.Decorate(line, null, hl);
        Check(
            d.Contains("<link=p1><color=#FF0000>Bob</color></link>")
                && d.Contains("hi <color=#FF0000>bob</color>,")
                && d.Contains("bobby and")
                && !d.Contains("<color=#FF0000>bobby"),
            "highlights whole words in any case, also in names",
            d
        );
        Check(d.Contains("<link=p1>") && d.Contains("<sprite=3>"), "tags are left as they are", d);
        Check(
            Count(d, "<color") == Count(d, "</color>"),
            "every colour tag the decoration adds is closed",
            Count(d, "<color") + " vs " + Count(d, "</color>")
        );
        string tagWord = ChatDecorator.Decorate("<color=#bob>x</color> <link=bob>y</link>", null, hl);
        Check(tagWord == "<color=#bob>x</color> <link=bob>y</link>", "words inside tags are never matched", tagWord);
        string broken = ChatDecorator.Decorate("bob <unclosed bob", null, hl);
        Check(broken.StartsWith("<color=#FF0000>bob</color> <unclosed"), "an unclosed tag does not break it", broken);
        string stamped = ChatDecorator.Decorate("x", "12:05", null);
        Check(
            stamped == "<color=" + ChatDecorator.TimestampColor + ">12:05</color> x",
            "timestamp before the line",
            stamped
        );

        var t = new DateTime(2026, 11, 26, 21, 7, 3);
        Check(TimestampFormat.Parse("%H:%M", out _).Format(t) == "21:07", "%H:%M");
        Check(TimestampFormat.Parse("[%I:%M:%S %p]", out _).Format(t) == "[09:07:03 PM]", "%I %p");
        Check(TimestampFormat.Parse("%d.%m.%y 100%%", out _).Format(t) == "26.11.26 100%", "date and %%");
        foreach (
            var (bad, says) in new[]
            {
                ("%H:%", "single %"),
                ("%Q", "%Q"),
                ("", "empty"),
                (new string('x', 40), "longer"),
            }
        )
        {
            TimestampFormat.Parse(bad, out var problem);
            Check(problem != null && problem.Contains(says), "timestamp format refused: " + says, problem);
        }
    }

    static void ChatDecorApi()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "a",
            @"
ChatSetTimestamps('%H:%M')
ChatHighlight('Bob', '#ff0000')
Out(ChatHighlight('alice', '#00ff00'))
Out(ChatHighlight('carol', 'green'))
"
        );
        rig.Mod("b", "ChatSetTimestamps('[%H:%M:%S]') ChatHighlight('bob', '#0000ff')");
        int changes = 0;
        rig.Start();
        rig.ChatDecor.Changed += () => changes++;
        rig.Run(0.05);
        var at = new DateTime(2026, 1, 1, 8, 30, 15);
        string d = rig.ChatDecor.Decorate("<color=#fff>bob:</color> hi alice", at);
        Check(spy.Out[0] == "true", "ChatHighlight returns true", spy.Out[0]);
        Check(
            spy.Out[1] == "false" && rig["a"].Status == ModStatus.Loaded,
            "a bad colour gives false, the mod running on",
            spy.Out[1]
        );
        Check(rig.ChatDecor.Decorate("carol", null) == "carol", "a word with a bad colour is not highlighted");
        Check(
            d.StartsWith("<color=" + ChatDecorator.TimestampColor + ">[08:30:15]</color>"),
            "the later mod's timestamp format wins",
            d
        );
        Check(
            d.Contains("<color=#0000FF>bob</color>") && d.Contains("<color=#00FF00>alice</color>"),
            "highlights of both mods, the later colour wins",
            d
        );
        rig.Host.SwitchOff("b", true);
        d = rig.ChatDecor.Decorate("bob", at);
        Check(
            changes > 0 && d == "<color=" + ChatDecorator.TimestampColor + ">08:30</color> <color=#FF0000>bob</color>",
            "switching a mod off takes its decoration away and redraws",
            d
        );
        rig.Host.SwitchOff("a", true);
        Check(rig.ChatDecor.Decorate("bob", at) == "bob" && !rig.ChatDecor.Active, "no mods, no decoration");
    }

    static void ModLinesMarked()
    {
        using var rig = new Rig();
        rig.Mod("m", "function say(s) GamePrint(s) NoticeShow(s) end");
        rig.Start();
        rig.Run(0.1);
        int before = rig.Env.ChatLines.Count;
        foreach (
            var s in new[]
            {
                "[Server] restock in 5 minutes",
                "</color><color=#BFE3FF>[other] reloaded: loaded",
                "[test] x",
            }
        )
        {
            rig.Host.Call(rig["m"], "say", rig["m"].Lua.Global("say"), 1, new Args(s));
            rig.Run(0.2);
        }
        var printed = rig.Env.ChatLines.Skip(before).ToList();
        Check(
            printed.Count == 3
                && printed.All(l =>
                    l.StartsWith("<color=" + ModHost.Color.Mod + ">[m] ")
                    && l.EndsWith("</color>")
                    && Count(l, "<color") == 1
                ),
            "a mod's chat line starts with its id in the mod tint, which nothing else has",
            string.Join(" | ", printed)
        );
        string infoLine = "<color=#FFF4B8>[Info]:</color> <color=#FFE46B>" + printed[0] + "</color> restock";
        string decorated = ChatDecorator.Decorate(
            infoLine,
            null,
            new[] { new Highlight("restock", "#FFE46B"), new Highlight("m", "#FFE46B") }
        );
        Check(
            decorated == infoLine.Replace("</color> restock", "</color> <color=#FFE46B>restock</color>"),
            "highlights never recolour a mod's line",
            decorated
        );
        Check(
            new[]
            {
                ModHost.Color.Note,
                ModHost.Color.Warn,
                ModHost.Color.Error,
                ChatDecorator.TimestampColor,
                TooltipArea.LineColor,
            }.All(c => !string.Equals(c, ModHost.Color.Mod, StringComparison.OrdinalIgnoreCase)),
            "the mod tint is no host colour"
        );
        Check(
            rig.Ui.Notices.Count > 0 && rig.Ui.Notices.All(n => n.lines[0] == "[m]"),
            "a notice starts with the mod's id",
            string.Join(" | ", rig.Ui.Notices.Select(n => string.Join("/", n.lines)))
        );
    }

    static void PrintCapped()
    {
        using var rig = new Rig();
        rig.Mod(
            "p",
            "function many() for i = 1, 500 do print('line ' .. i) end end "
                + "function big() print(string.rep('x', 3000000)) end "
                + "function forge() print('a\\n[Info   :ShorelandsMods] [other] forged\\r\\n') end"
        );
        rig.Start();
        int Lines() => rig.Env.LogLines.Count(l => l.Contains("[p] "));
        rig.Host.Call(rig["p"], "forge", rig["p"].Lua.Global("forge"), 0, default);
        Check(
            rig.Env.LogLines.Last().StartsWith("Info [p] a ") && !rig.Env.LogLines.Last().Contains('\n'),
            "a line break in print becomes a space",
            rig.Env.LogLines.Last()
        );
        rig.Run(1.1);
        int before = Lines();
        rig.Host.Call(rig["p"], "many", rig["p"].Lua.Global("many"), 0, default);
        Check(
            Lines() - before == Limits.LogLinesPerSecond + 1
                && rig.Env.LogLines.Count(l => l.Contains("[p] output was cut")) == 1,
            "lines over the second's allowance are dropped, with one line saying so",
            (Lines() - before).ToString()
        );
        rig.Run(1.1);
        long chars = rig.Env.LogLines.Sum(l => (long)l.Length);
        rig.Host.Call(rig["p"], "big", rig["p"].Lua.Global("big"), 0, default);
        rig.Host.Call(rig["p"], "big", rig["p"].Lua.Global("big"), 0, default);
        Check(
            rig.Env.LogLines.Sum(l => (long)l.Length) - chars < 2 * (Limits.LogLineChars + 100),
            "a long line is cut",
            (rig.Env.LogLines.Sum(l => (long)l.Length) - chars).ToString()
        );
        rig.Run(1.1);
        for (int i = 0; i < 40; i++)
            rig.Host.Call(rig["p"], "big", rig["p"].Lua.Global("big"), 0, default);
        Check(
            rig.Env.LogLines.Count(l => l.Contains("[p] output was cut")) == 2,
            "characters per second are capped too"
        );
    }

    static void ChatDelivery()
    {
        bool None(string id) => false;
        bool Bob(string id) => id == "555";
        string K(
            ChatRoute r,
            string type,
            string id,
            string name,
            string speaker = null,
            Func<string, bool> spawned = null
        ) => ChatSenders.Kind(r, type, id, name, speaker, "100", spawned ?? None);
        var cases = new (string what, string got, string want)[]
        {
            ("a merchant's thanks (no id)", K(ChatRoute.Public, "PUBLIC", "", "[NPC]Stream Merchant", "100"), null),
            (
                "an NPC line with a made-up id on a player's model",
                K(ChatRoute.Public, "PUBLIC", "npc-7", "[NPC]Gossip", "100"),
                null
            ),
            (
                "a public line not from the model it came on",
                K(ChatRoute.Public, "PUBLIC", "555", "bob", "100", Bob),
                null
            ),
            ("a spawned player's public line", K(ChatRoute.Public, "PUBLIC", "555", "bob", "555", Bob), "public"),
            ("your own public line", K(ChatRoute.Public, "PUBLIC", "100", "me", "100"), "public"),
            ("a server line", K(ChatRoute.Global, "SERVER", "", "[Server]"), null),
            ("a server line typed GLOBAL", K(ChatRoute.Global, "GLOBAL", "", "[Server]"), null),
            ("an announcement with a made-up id", K(ChatRoute.Global, "GLOBAL", "server", "Announcement"), null),
            (
                "a global line with a player id but a bracketed name",
                K(ChatRoute.Global, "GLOBAL", "12345", "[NPC]Bob"),
                null
            ),
            (
                "a global line from a player out of range",
                K(ChatRoute.Global, "GLOBAL", "28089848", "twinaga"),
                "global"
            ),
            (
                "a global line from a spawned player with an odd id",
                K(ChatRoute.Global, "GLOBAL", "p-555", "bob", null, i => i == "p-555"),
                "global"
            ),
            (
                "a whisper from a player out of range",
                K(ChatRoute.Whisper, "PRIVATE", "69971526", "partyhorst"),
                "private"
            ),
            ("your own whisper", K(ChatRoute.Whisper, "PRIVATE", "100", "me"), "private"),
            ("a whisper typed INFO", K(ChatRoute.Whisper, "INFO", "69971526", "partyhorst"), null),
            ("a global line on the whisper route", K(ChatRoute.Whisper, "GLOBAL", "69971526", "partyhorst"), null),
            ("a whisper on the global route", K(ChatRoute.Global, "PRIVATE", "69971526", "partyhorst"), null),
            ("a whisper with a non-player id", K(ChatRoute.Whisper, "PRIVATE", "npc:1", "Merchant"), null),
        };
        CheckEach("chat delivery: only players' lines, by route and type", cases, cases.Length, c =>
            c.got == c.want ? null : c.what + " gave " + (c.got ?? "not delivered")
        );
    }

    // ChatArea's part (ChatSenders.Deliver): a player's line reaches OnChatMessage with its type, sender and text, and
    // plays the chat or whisper sound, not for your own lines; any other line does neither.
    static void ChatWiring()
    {
        var audio = new FakeAudio();
        var spy = new SpyArea();
        using var rig = new Rig(new SoundArea(audio), spy);
        rig.Mod("snd", null);
        rig.WriteBytes("snd", "sounds/chat.wav", Files.Wav(0.2));
        rig.WriteBytes("snd", "sounds/whisper.wav", Files.Wav(0.2));
        rig.Mod("c", "function OnChatMessage(m) Out(m.type, m.sender, m.text) end");
        rig.Start();
        audio.ReadyAll();
        rig.Run(0.05);
        string Line(ChatRoute route, string type, string id, string sender, string text, string speaker = null)
        {
            spy.Out.Clear();
            audio.Plays.Clear();
            ChatSenders.Deliver(rig.Host, route, type, id, sender, text, speaker, "100", i => i == "555");
            rig.Run(0.2);
            return string.Join("|", spy.Out) + " / " + string.Join("|", audio.Plays.Select(p => p.Split('@')[0]));
        }
        var cases = new (string What, string Got, string Want)[]
        {
            (
                "a spawned player's public line",
                Line(ChatRoute.Public, "PUBLIC", "555", "bob", "hi", "555"),
                "public\tbob\thi / chat.wav"
            ),
            ("your own public line", Line(ChatRoute.Public, "PUBLIC", "100", "me", "yo", "100"), "public\tme\tyo / "),
            (
                "a whisper",
                Line(ChatRoute.Whisper, "PRIVATE", "69971526", "partyhorst", "psst"),
                "private\tpartyhorst\tpsst / whisper.wav"
            ),
            ("your own whisper", Line(ChatRoute.Whisper, "PRIVATE", "100", "me", "x"), "private\tme\tx / "),
            (
                "a global line without sender or text",
                Line(ChatRoute.Global, "GLOBAL", "555", null, null),
                "global\t\t / chat.wav"
            ),
            (
                "a merchant's thanks",
                Line(ChatRoute.Public, "PUBLIC", "", "[NPC]Stream Merchant", "Thanks!", "100"),
                " / "
            ),
        };
        CheckEach("a line reaches the mods and the sounds as its kind says", cases, cases.Length, c =>
            c.Got == c.Want ? null : c.What + ": " + c.Got
        );
    }

    static readonly Regex LinkTag = new Regex("<link=([^>]*)>");

    static List<string> LinkIds(IEnumerable<string> lines) =>
        lines.SelectMany(l => LinkTag.Matches(l).Select(m => m.Groups[1].Value)).ToList();

    static void Click(Rig rig, string id)
    {
        rig.Host.Area<OutputArea>().Click(id);
        rig.Run(0.05);
    }

    static void LinkClickCallsOwner()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("a", "GamePrintLink('Ready.', 'Open', function(...) Out('a', Depth(), select('#', ...)) end)");
        rig.Mod("b", "GamePrintLink('Ready.', 'Open', function() Out('b') end)");
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        string Of(string mod) => LinkIds(rig.Env.ChatLines.Where(l => l.Contains("[" + mod + "] "))).Single();
        Click(rig, Of("a"));
        Check(
            string.Join("|", o) == "a\t1\t0",
            "a click runs the owner's function only, guarded, with no arguments",
            string.Join("|", o)
        );
        Click(rig, Of("b"));
        Click(rig, Of("b"));
        Check(string.Join("|", o) == "a\t1\t0|b|b", "each link calls its own mod, every click", string.Join("|", o));
        Check(rig.Host.Area<OutputArea>().Click(Of("a")), "the host takes a click on its link from the game");
    }

    // The game handles "item:<id>:<meta>" (tooltip) and every other id as a player (its menu can whisper). The host
    // takes only its own form, which neither of those has, and a mod has no way to name an id.
    static void LinkIdsOnlyTheHosts()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("a", "GamePrintLink('x', 'One', function() Out('one') end, 'Two', function() Out('two') end)");
        rig.Start();
        var area = rig.Host.Area<OutputArea>();
        foreach (var game in new[] { "item:9999:3", "item:100:0", "12345", "7123", "", null, "SMODS:0123456789abcdef" })
            Check(!area.Click(game), "a game link stays the game's: " + (game ?? "null"));
        var ids = LinkIds(rig.Env.ChatLines);
        Check(
            ids.Count == 2 && ids.All(ChatLinkId.WellFormed) && ids.Distinct().Count() == 2,
            "the line holds two host ids",
            string.Join(",", ids)
        );
        Check(
            ids.All(id => !id.StartsWith("item:") && !id.All(char.IsDigit)),
            "a host id is neither an item link nor a player id"
        );
        rig.Run(0.05);
        int before = rig.Env.ChatLines.Count;
        Check(area.Click("smods:0123456789abcdef"), "a host-form id no mod made is still kept from the game");
        rig.Run(0.05);
        Check(rig.Host.Area<SpyArea>().Out.Count == 0, "and calls no mod");
        Check(
            rig.Env.ChatLines.Skip(before).Single().Contains("this link no longer works"),
            "the player is told it does nothing",
            string.Join("|", rig.Env.ChatLines.Skip(before))
        );
    }

    static readonly Regex HostTags = new Regex(
        "<color=#[0-9A-F]{6}>|</color>|<link=smods:[0-9a-f]{16}>|</link>|<nobr>|</nobr>|<mark="
            + Regex.Escape(ModHost.Color.LinkBox)
            + ">|</mark>"
    );

    static void LinkTextStaysText()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("a", "function say(text, label) return GamePrintLink(text, label, function() Out('clicked') end) end");
        rig.Start();
        var hostile = Hostile.Concat(
            new[] { " </mark></nobr></link><link=12345>bob</link>", "<link=item:1:0>[Hoe]</link>" }
        );
        foreach (var h in hostile)
        {
            int before = rig.Env.ChatLines.Count;
            rig.Host.Call(rig["a"], "say", rig["a"].Lua.Global("say"), 2, new Args(h, h));
            rig.Run(0.2);
            var line = rig.Env.ChatLines.Skip(before).FirstOrDefault(l => l.Contains("<link="));
            string shown = h.Length > 40 ? h.Substring(0, 40) : h;
            if (line == null)
            {
                Check(false, "a line is printed: " + shown);
                continue;
            }
            string rest = HostTags.Replace(line, "");
            Check(RichProblem(rest, true) == null, "no tag of the mod's in the line: " + shown, line);
            var ids = LinkIds(new[] { line });
            Check(ids.Count == 1 && ChatLinkId.WellFormed(ids[0]), "one host link: " + shown, line);
        }
        var label = " </mark></nobr></link><link=12345>bob</link>";
        rig.Host.Call(rig["a"], "say", rig["a"].Lua.Global("say"), 2, new Args("Hi", label));
        string last = rig.Env.ChatLines.Last();
        var decorated = ChatDecorator.Decorate(
            last,
            "06:05",
            new[] { new Highlight("bob", "#FF0000"), new Highlight("Hi", "#FF0000") }
        );
        Check(
            decorated == "<color=" + ChatDecorator.TimestampColor + ">06:05</color> " + last,
            "highlights never touch a mod line or split its link",
            decorated
        );
    }

    static void LinkCaps()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "a",
            @"
local f = function() end
function four() Out(GamePrintLink('t', 'A', f, 'B', f, 'C', f, 'D', f)) end
function long() Out(GamePrintLink('t', string.rep('x', 50), f)) end
function empty() Out(GamePrintLink('t', '  ', f, 'B', f)) end
function burst() for i = 1, 11 do Out(GamePrintLink('t', 'L' .. i, f)) end end
function one(i) GamePrintLink('line ' .. i, 'L' .. i, function() Out('clicked', i) end) end
"
        );
        rig.Start();
        var mod = rig["a"];
        var area = rig.Host.Area<OutputArea>();
        var o = rig.Host.Area<SpyArea>().Out;
        string Last() => rig.Env.ChatLines.Last(l => l.Contains("[a] "));
        void Call(string fn)
        {
            rig.Host.Call(mod, fn, mod.Lua.Global(fn));
            rig.Run(1.1);
        }

        Call("four");
        Check(o.Last() == "false" && LinkIds(new[] { Last() }).Count == 3, "a fourth link is left out, false", Last());
        Check(rig.Env.ChatText.Contains("a line has at most 3 links"), "and the player is told", rig.Env.ChatText);
        Call("long");
        Check(
            o.Last() == "false" && Last().Contains("> " + new string('x', 37) + "... </mark>"),
            "a label over 40 characters is cut, false",
            Last()
        );
        Call("empty");
        Check(
            o.Last() == "false" && LinkIds(new[] { Last() }).Count == 1 && Last().Contains("> B </mark>"),
            "an empty label is left out, false",
            Last()
        );
        int had = area.LinkCount(mod);
        Call("burst");
        Check(
            o.Skip(o.Count - 11).Count(x => x == "true") == 10
                && o.Last() == "false"
                && area.LinkCount(mod) == had + 10,
            "11 lines in a second: like GamePrint, 10 are printed; a dropped line makes no link",
            string.Join("|", o.Skip(o.Count - 11))
        );

        int start = rig.Env.ChatLines.Count;
        for (int i = 1; i <= Limits.ChatLinksPerMod + 6; i++)
        {
            rig.Host.Call(mod, "one", mod.Lua.Global("one"), 1, new Args(i));
            rig.Run(0.11);
        }
        var ids = LinkIds(rig.Env.ChatLines.Skip(start));
        Check(
            area.LinkCount(mod) == Limits.ChatLinksPerMod,
            "a mod keeps its newest 64 links",
            area.LinkCount(mod).ToString()
        );
        o.Clear();
        Click(rig, ids[5]);
        Click(rig, ids[6]);
        Click(rig, ids.Last());
        Check(
            string.Join("|", o) == "clicked\t7|clicked\t70",
            "an older link does nothing, the newest 64 work",
            string.Join("|", o)
        );
        Check(rig.Env.ChatLines.Count(l => l.Contains("this link no longer works")) == 1, "the old one says so");
    }

    static void LinksDieWithTheMod()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("a", "GamePrintLink('x', 'Open', function() Out('clicked') end)");
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        var area = rig.Host.Area<OutputArea>();
        string first = LinkIds(rig.Env.ChatLines).Single();
        rig.Host.Reload("a");
        rig.Run(0.05);
        Check(area.LinkCount(rig["a"]) == 1, "the reloaded mod has only its new link");
        Click(rig, first);
        Check(o.Count == 0, "a link from before the reload calls nothing", string.Join("|", o));
        string second = LinkIds(rig.Env.ChatLines).Last();
        Click(rig, second);
        Check(o.Count == 1, "the new link works");
        rig.Env.Off.Add("a");
        rig.Host.Reload("a");
        rig.Run(0.05);
        Click(rig, second);
        Check(o.Count == 1 && rig["a"].Status == ModStatus.Off, "a switched-off mod's links call nothing");
    }

    // A whisper through the player menu must not send a command with a mod's target and text: a send made from inside
    // a link's function is refused like any other, and the mod stops.
    static void LinkClickIsGuarded()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "w",
            "GamePrintLink('x', 'Whisper', function() Out('depth', Depth()) SendCommand('System.Void ReworkedCopium.Chat.ChatManager::CmdWhisper()') end)"
        );
        rig.Start();
        Click(rig, LinkIds(rig.Env.ChatLines).Single());
        Check(rig.Host.Area<SpyArea>().Out.SequenceEqual(new[] { "depth\t1" }), "the guard flag is set in the click");
        Check(
            rig["w"].Status == ModStatus.Failed && rig["w"].Reason.Contains("send"),
            "a send from a link's function is refused and stops the mod",
            rig["w"].Reason
        );
        Check(rig.Env.ChatText.Contains("CmdWhisper"), "the refusal names what was sent");
    }

    // A game update that removed the click prefix: GamePrintLink gives false and draws no link the game could take.
    sealed class NoClickHook : Area
    {
        public override string Name => "No click hook";

        protected internal override void Register(AreaSetup s) =>
            Host
                .Api.Find("GamePrintLink")
                .Needs(s.Hook("ChatView.OnPointerClick", "clickable chat lines", () => false), false);
    }

    static void LinkWithoutClickHook()
    {
        using var rig = new Rig(new SpyArea(), new NoClickHook());
        rig.Mod("a", "Out(GamePrintLink('x', 'Open', function() end))");
        rig.Start();
        Check(
            rig.Host.Area<SpyArea>().Out.SequenceEqual(new[] { "false" })
                && !rig.Env.ChatLines.Any(l => l.Contains("<link")),
            "without the click hook no link is printed",
            string.Join("|", rig.Env.ChatLines)
        );
    }
}
