using System.Collections.Generic;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    // At most `perSecond` events in any one-second window; the rest are dropped.
    internal sealed class RateLimit
    {
        readonly int perSecond;
        double windowStart = double.NegativeInfinity;
        int used;

        public RateLimit(int perSecond)
        {
            this.perSecond = perSecond;
        }

        public bool Take(double now)
        {
            if (now - windowStart >= 1)
            {
                windowStart = now;
                used = 0;
            }
            if (used < perSecond)
            {
                used++;
                return true;
            }
            return false;
        }
    }

    // print's allowance per mod: at most Limits.LogLinesPerSecond lines and Limits.LogCharsPerSecond characters in
    // any one-second window; the rest of the window is dropped.
    internal sealed class LogBudget
    {
        double windowStart = double.NegativeInfinity;
        int lines,
            chars;
        bool cut;

        // True: write the line. False: drop it; firstCut is true for the first line dropped in a window.
        public bool Take(double now, int length, out bool firstCut)
        {
            firstCut = false;
            if (now - windowStart >= 1)
            {
                windowStart = now;
                lines = chars = 0;
                cut = false;
            }
            if (!cut && lines < Limits.LogLinesPerSecond && chars + length <= Limits.LogCharsPerSecond)
            {
                lines++;
                chars += length;
                return true;
            }
            firstCut = !cut;
            cut = true;
            return false;
        }
    }

    internal sealed class OutputArea : Area
    {
        sealed class State
        {
            public readonly RateLimit Chat = new RateLimit(Limits.ChatLinesPerSecond);
            public readonly LogBudget Log = new LogBudget();
        }

        readonly ChatLinks links = new ChatLinks();

        public override string Name => "Output";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "GamePrint( text:string )",
                "prints a line in your own chat only; nothing is sent. Shown as typed (no colour tags) after your mod's "
                    + "id in brackets, in the mods' violet, so players can tell it from the game's lines; at most "
                    + Limits.ChatLineChars
                    + " characters; at most "
                    + Limits.ChatLinesPerSecond
                    + " lines a second per mod (more are dropped)",
                c => Chat(c, c.Text(1, "text"))
            );
            s.Function(
                "GamePrintLink( text:string, label:string, fn:function, [label2:string, fn2:function, label3:string, fn3:function] ) -> ok:boolean",
                "prints a line like GamePrint with up to "
                    + Limits.ChatLinksPerLine
                    + " clickable labels after the text, each on a violet box; a click on one calls its fn (with no arguments, as a "
                    + "window button does: it may open a window). Nothing is sent. A label has at most "
                    + Limits.ChatLinkLabelChars
                    + " characters. The links of a mod work until it reloads or stops, and only its newest "
                    + Limits.ChatLinksPerMod
                    + " (a click on an older one says so in chat). Counts as a GamePrint line. false when a label was cut, "
                    + "empty or one too many (left out), or the line was dropped (more than "
                    + Limits.ChatLinesPerSecond
                    + " lines a second)",
                PrintLink
            );
            s.Function(
                "print( ... )",
                "writes its arguments, separated by tabs, as one line of the BepInEx log (BepInEx/LogOutput.log) after "
                    + "the mod id; line breaks become spaces, a line is cut at "
                    + Limits.LogLineChars
                    + " characters; at most "
                    + Limits.LogLinesPerSecond
                    + " lines and "
                    + Limits.LogCharsPerSecond
                    + " characters a second per mod (the rest of that second is dropped, with one line saying so)",
                c =>
                {
                    var parts = new LuaValue[c.Count];
                    for (int i = 0; i < parts.Length; i++)
                        parts[i] = c.Arg(i + 1);
                    Print(c.Mod, LuaText.Join(parts, Limits.LogLineChars, out long length), length);
                    return c.Return();
                }
            );
        }

        int PrintLink(ApiCall c)
        {
            string text = c.Text(1, "text");
            var made = new List<(string Label, LuaFunction Fn)>();
            for (int i = 0; ; i++)
            {
                int n = 2 + 2 * i;
                if (i > 0 && !c.Has(n) && !c.Has(n + 1))
                    break;
                if (i >= Limits.ChatLinksPerLine)
                {
                    c.Refuse(
                        "links",
                        "a line has at most " + Limits.ChatLinksPerLine + " links, so the others are left out"
                    );
                    break;
                }
                string label = c.Text(n, "label");
                var fn = c.Func(n + 1, "fn");
                if (label.Length > Limits.ChatLinkLabelChars)
                {
                    c.Refuse(
                        "label",
                        "the label \""
                            + TextEscape.Clip(label, Limits.ChatLinkLabelChars)
                            + "\" is longer than "
                            + Limits.ChatLinkLabelChars
                            + " characters, so it is cut"
                    );
                    label = TextEscape.Clip(label, Limits.ChatLinkLabelChars);
                }
                label = TextEscape.Plain(label).Trim();
                if (label.Length == 0)
                {
                    c.Refuse("empty", "an empty label cannot be clicked, so that link is left out");
                    continue;
                }
                made.Add((label, fn));
            }
            var st = c.Mod.Get<State>();
            if (!st.Chat.Take(Host.Env.Now))
                return c.Return(false);
            var shown = new List<(string Id, string Label)>();
            foreach (var (label, fn) in made)
                shown.Add((links.Add(c.Mod, fn, label), label));
            Host.Env.Chat(ModHost.Color.Mod, Prefix(c.Mod) + TextEscape.Clip(text, Limits.ChatLineChars), shown);
            return c.ReturnOk();
        }

        // A click on a link in the chat, from the game's click handler. True: the id is the host's, so the game must
        // not handle it. The mod's function runs in the host's next Update, as the player's own act.
        public bool Click(string id)
        {
            if (!ChatLinkId.IsHost(id))
                return false;
            Host.Post(() =>
            {
                if (links.Find(id, out var mod, out var fn, out var what))
                    Host.CallByPlayer(mod, what, fn);
                else
                    Host.Env.Chat(
                        ModHost.Color.Note,
                        "Mods: this link no longer works (its mod was reloaded or switched off, or printed many links since)."
                    );
            });
            return true;
        }

        public int LinkCount(Mod mod) => links.Count(mod);

        protected internal override void ModStopping(Mod mod) => links.Drop(mod);

        protected internal override IEnumerable<LuaFunction> Held(Mod mod) => links.Held(mod);

        string Prefix(Mod mod) => (Host.Area<ToolsArea>()?.Trying == mod ? "[test] " : "") + "[" + mod.Id + "] ";

        int Chat(ApiCall c, string text)
        {
            var st = c.Mod.Get<State>();
            if (st.Chat.Take(Host.Env.Now))
                Host.Env.Chat(ModHost.Color.Mod, Prefix(c.Mod) + TextEscape.Clip(text, Limits.ChatLineChars));
            return c.Return();
        }

        // print, also from settings.lua; length: of the text before Join cut it.
        public void Print(Mod mod, string text, long length)
        {
            string line = TextEscape.LogLine(text, Limits.LogLineChars, length) ?? "";
            if (mod.Get<State>().Log.Take(Host.Env.Now, line.Length, out bool firstCut))
                Host.Env.Log(LogLevel.Info, "[" + mod.Id + "] " + line);
            else if (firstCut)
                Host.Env.Log(
                    LogLevel.Warning,
                    "["
                        + mod.Id
                        + "] output was cut: over "
                        + Limits.LogLinesPerSecond
                        + " lines or "
                        + Limits.LogCharsPerSecond
                        + " characters in a second; print is dropped for the rest of that second"
                );
        }
    }
}
