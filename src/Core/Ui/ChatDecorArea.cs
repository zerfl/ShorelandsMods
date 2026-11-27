using System;
using System.Collections.Generic;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core.Ui
{
    // ChatSetTimestamps, ChatHighlight, ChatHighlightClear: the host decorates every chat line it draws (player,
    // server, info and NPC lines alike). Nothing about the lines or the matches goes back to Lua: the functions return
    // only whether their arguments were taken, and no callback is involved.
    internal sealed class ChatDecorArea : Area
    {
        public const int MaxHighlights = 32,
            MaxWordChars = 40;

        sealed class State
        {
            public TimestampFormat Stamp;
            public readonly List<(string Word, string Hex)> Words = new List<(string, string)>();
        }

        TimestampFormat stamp;
        List<Highlight> highlights = new List<Highlight>();

        // The decoration changed: lines already shown are drawn again.
        public event Action Changed;

        public override string Name => "Chat display";

        public bool Active => stamp != null || highlights.Count > 0;

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "ChatSetTimestamps( format:string|true|nil ) -> ok:boolean",
                "puts the local time a line arrived before every chat line (also lines already shown; not lines from "
                    + "before the host started), in os.date's notation: \"%H:%M\", \"%H:%M:%S\", \"%I:%M %p\"; codes %H %M %S %I "
                    + "%p %d %m %y %Y %%, between them letters, digits, spaces and "
                    + TimestampFormat.Punctuation.Trim()
                    + ", at most "
                    + TimestampFormat.MaxLength
                    + " characters; true = \"%H:%M\", nil or false = off. The last mod in load order with a format wins. "
                    + "false (the format before stays) for an empty, too long or unknown format or another character",
                SetTimestamps
            );
            s.Function(
                "ChatHighlight( word:string, color ) -> ok:boolean",
                "colours word wherever it appears in chat as a whole word, any case, in every line shown (player, "
                    + "server, info and NPC lines, not the host's notes or mods' lines; the mod is never told which lines "
                    + "match). The same word again changes "
                    + "its colour; of two mods, the later in load order wins. false (not highlighted) for an empty word, "
                    + "one over "
                    + MaxWordChars
                    + " characters, a colour that is not one, or a word beyond the "
                    + MaxHighlights
                    + " a mod may have",
                AddHighlight
            );
            s.Function(
                "ChatHighlightClear()",
                "removes all of this mod's highlights (its timestamps stay)",
                ClearHighlights
            );
        }

        int SetTimestamps(ApiCall c)
        {
            var st = c.Mod.Get<State>();
            var v = c.Arg(1);
            if (v.Type == LuaValueType.Nil || (v.Type == LuaValueType.Boolean && !v.Read<bool>()))
                st.Stamp = null;
            else if (v.Type == LuaValueType.Boolean)
                st.Stamp = TimestampFormat.Parse(TimestampFormat.Default, out _);
            else
            {
                string format = c.String(1, "format");
                var parsed = TimestampFormat.Parse(format, out var problem);
                if (parsed == null)
                {
                    c.Refuse(
                        1,
                        "the timestamp format \""
                            + TextEscape.Clip(format, 40)
                            + "\" "
                            + problem
                            + ", so it is not used"
                    );
                    return c.ReturnOk();
                }
                st.Stamp = parsed;
            }
            Track(c.Mod);
            Recompute();
            return c.ReturnOk();
        }

        int AddHighlight(ApiCall c)
        {
            string word = c.String(1, "word").Trim();
            var color = c.Color(2, "color");
            if (word.Length == 0 || word.Length > MaxWordChars)
            {
                c.Refuse(
                    1,
                    "the word \""
                        + TextEscape.Clip(word, 50)
                        + "\" has "
                        + word.Length
                        + " characters; 1 to "
                        + MaxWordChars
                        + " can be highlighted, so it is not"
                );
                return c.ReturnOk();
            }
            if (!(color is Rgba col))
                return c.ReturnOk();
            var st = c.Mod.Get<State>();
            string drawn = TextEscape.Tmp(word);
            st.Words.RemoveAll(w => string.Equals(w.Word, drawn, StringComparison.OrdinalIgnoreCase));
            if (st.Words.Count >= MaxHighlights)
            {
                c.Refuse(
                    0,
                    "the mod already highlights "
                        + MaxHighlights
                        + " words, so \""
                        + TextEscape.Clip(word, 40)
                        + "\" and any further ones are not"
                );
                return c.ReturnOk();
            }
            st.Words.Add((drawn, OptionCheck.Hex(new Rgba(col.R, col.G, col.B, 1))));
            Track(c.Mod);
            Recompute();
            return c.ReturnOk();
        }

        int ClearHighlights(ApiCall c)
        {
            c.Mod.Find<State>()?.Words.Clear();
            Recompute();
            return c.Return();
        }

        void Track(Mod mod) => mod.Resources.Add("chat", "decoration", () => { });

        protected internal override void ModStopping(Mod mod)
        {
            if (mod.Find<State>() == null)
                return;
            mod.Find<State>().Stamp = null;
            mod.Find<State>().Words.Clear();
            Recompute();
        }

        // The last mod in load order that set a format wins; highlights of all mods count, a later mod's colour wins
        // for the same word.
        void Recompute()
        {
            TimestampFormat s = null;
            var words = new Dictionary<string, Highlight>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in Host.Mods)
            {
                var st = mod.Find<State>();
                if (st == null || mod.Status != ModStatus.Loaded)
                    continue;
                if (st.Stamp != null)
                    s = st.Stamp;
                foreach (var (w, hex) in st.Words)
                    words[w] = new Highlight(w, hex);
            }
            bool changed =
                s?.Source != stamp?.Source
                || words.Count != highlights.Count
                || highlights.Any(h => !words.TryGetValue(h.Word, out var n) || n.Hex != h.Hex);
            stamp = s;
            highlights = words.Values.ToList();
            if (changed)
                Changed?.Invoke();
        }

        // One line as the game formatted it, decorated; arrived: when the line arrived (null: unknown, no timestamp).
        public string Decorate(string line, DateTime? arrived) =>
            ChatDecorator.Decorate(
                line,
                stamp != null && arrived.HasValue ? stamp.Format(arrived.Value) : null,
                highlights
            );
    }
}
