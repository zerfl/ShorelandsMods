using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    // What /try may use besides the typed words: the mod's settings, item tables and the wallet (all readable by mods
    // anyway). Never a timing event, another player, a market or a puzzle.
    internal interface ITryFacts
    {
        IReadOnlyList<string> SettingIds { get; }
        IReadOnlyList<string> KeyIds { get; }

        // The setting's value as ModSettingGet gives it; Nil for an unknown id.
        LuaValue Setting(string id);

        // As OnItemGained gets it; null for an unknown item.
        LuaTable Item(int id, int count, int meta);

        // Currency and CITB now; null outside the world.
        double[] Wallet();
    }

    // One callback call made up from what the player typed after "/try <mod>".
    internal sealed class TryPlan
    {
        public string Callback;
        public LuaValue[] Args;
        public string Shown;

        // OnPlayerReady gets init.lua's budget, as the game's call does; OnItemTooltip's result is shown.
        public bool Setup,
            Ask;
    }

    // The words of /try: named shortcuts for the common events, or a callback's name with plain values.
    internal static class TryCommand
    {
        public const string DefaultText = "hello";
        public const double NightLevel = 0.5;
        public const int RoundSeconds = 600;

        static readonly string[] Zones = { "town", "playerland", "holyland" };
        static readonly string[] Activities = { "idle", "walking", "farming", "fishing", "talking" };
        static readonly Dictionary<string, string> ChatTypes = new Dictionary<string, string>
        {
            ["whisper"] = "private",
            ["chat"] = "public",
            ["global"] = "global",
        };
        static readonly Dictionary<string, string> TableShortcut = new Dictionary<string, string>
        {
            ["OnChatMessage"] = "whisper <name> <text>",
            ["OnItemGained"] = "item <id>",
            ["OnItemTooltip"] = "tooltip <item id>",
        };

        // Per callback the mod defines, the shortcut words that call it, as the listing shows them.
        public static List<(string Callback, string Words)> Shortcuts(Func<string, bool> has, ITryFacts facts)
        {
            var all = new List<(string, string)>
            {
                ("OnPlayerReady", "ready"),
                ("OnZoneChanged", string.Join(", ", Zones)),
                ("OnActivityChanged", string.Join(", ", Activities)),
                ("OnChatMessage", "whisper <name> <text>, chat <name> <text>, global <name> <text>"),
                ("OnRainChanged", "rain, rain stop"),
                ("OnLightChanged", "day, night, light <0 to 1>"),
                ("OnWalletChanged", "earn <amount>, spend <amount>"),
                ("OnItemGained", "item <id> [count], koi <id>"),
                ("OnInventoryChanged", "inventory"),
                ("OnOfferingHasOwnChanged", "offering, offering done"),
                ("OnOfferingRoundStarted", "round [seconds]"),
                (
                    "OnKey",
                    facts.KeyIds.Count > 0 ? string.Join(", ", facts.KeyIds.Select(k => "key " + k)) : "key <id>"
                ),
                (
                    "OnSettingChanged",
                    facts.SettingIds.Count > 0 ? "setting " + string.Join("|", facts.SettingIds) : "setting <id>"
                ),
                ("OnItemTooltip", "tooltip <item id>"),
                ("OnWindowClosed", "closed <window id>"),
            };
            return all.Where(s => has(s.Item1)).ToList();
        }

        // One command line that works for a shortcut entry: its first form with sample values ("whisper Friend hello").
        public static string Example(string words)
        {
            string first = words.Split(new[] { ", " }, StringSplitOptions.None)[0];
            first = first.Split('|')[0];
            return first
                .Replace("<name> <text>", "Friend " + DefaultText)
                .Replace("<amount>", "100")
                .Replace("<item id>", "101")
                .Replace("<id> [count]", "101")
                .Replace("<window id>", "main")
                .Replace(" [seconds]", "");
        }

        // null plan: the problem says why, in a sentence for chat.
        public static (TryPlan Plan, string Problem) Parse(string what, Func<string, bool> has, ITryFacts facts)
        {
            var words = Words(what);
            if (words.Count == 0)
                return No("say what to try");
            string first = words[0];
            string word = first.ToLowerInvariant();
            string rest = Rest(what, 1);
            TryPlan plan;
            string problem = null;
            if (first.StartsWith("On") && first.Length > 2 && char.IsUpper(first[2]))
                (plan, problem) = Generic(first, words.Skip(1).ToList());
            else if (word == "ready")
                plan = Call("OnPlayerReady");
            else if (Zones.Contains(word))
                plan = Call("OnZoneChanged", word);
            else if (Activities.Contains(word))
                plan = Call("OnActivityChanged", word);
            else if (ChatTypes.TryGetValue(word, out var type))
                (plan, problem) = Chat(word, type, words, what);
            else if (word == "rain")
                (plan, problem) =
                    words.Count == 1 || words[1] == "start" ? Ok(Call("OnRainChanged", true))
                    : words[1] == "stop" ? Ok(Call("OnRainChanged", false))
                    : No("rain or rain stop");
            else if (word == "day" || word == "night")
                plan = Call("OnLightChanged", word == "day" ? 1.0 : NightLevel);
            else if (word == "light")
                (plan, problem) =
                    words.Count == 2 && Number(words[1], out double level) && level >= 0 && level <= 1
                        ? Ok(Call("OnLightChanged", level))
                        : No("light needs a level from 0 to 1, like light 0.5");
            else if (word == "earn" || word == "spend")
                (plan, problem) = Wallet(word, words, facts);
            else if (word == "item" || word == "koi")
                (plan, problem) = Item(word, words, facts);
            else if (word == "inventory")
                plan = Call("OnInventoryChanged");
            else if (word == "offering")
                (plan, problem) =
                    words.Count == 1 ? Ok(Call("OnOfferingHasOwnChanged", true))
                    : words[1] == "done" ? Ok(Call("OnOfferingHasOwnChanged", false))
                    : No("offering or offering done");
            else if (word == "round")
                (plan, problem) =
                    words.Count == 1 ? Ok(Call("OnOfferingRoundStarted", (double)RoundSeconds))
                    : Number(words[1], out double s) && s >= 1 && s <= 3600 ? Ok(Call("OnOfferingRoundStarted", s))
                    : No("round takes the seconds left, 1 to 3600, like round 600");
            else if (word == "key")
                (plan, problem) = Key(words, facts);
            else if (word == "setting")
                (plan, problem) = Setting(words, facts);
            else if (word == "tooltip")
                (plan, problem) = Tooltip(words, facts);
            else if (word == "closed")
                (plan, problem) =
                    words.Count >= 2
                        ? Ok(Call("OnWindowClosed", rest, true))
                        : No("closed needs the window's id, like closed main");
            else
                return No("\"" + first + "\" is nothing /try knows");
            if (plan == null)
                return No(problem);
            if (!has(plan.Callback))
                return No("it has no function " + plan.Callback + ", so " + first + " would call nothing");
            return (plan, null);
        }

        static (TryPlan, string) Ok(TryPlan plan) => (plan, null);

        static (TryPlan, string) No(string why) => (null, why);

        static TryPlan Call(string callback, params LuaValue[] args) =>
            new TryPlan
            {
                Callback = callback,
                Args = args,
                Shown = callback + "(" + string.Join(", ", args.Select(Show)) + ")",
                Setup = callback == "OnPlayerReady",
                Ask = callback == "OnItemTooltip",
            };

        static (TryPlan, string) Generic(string callback, List<string> values)
        {
            if (TableShortcut.TryGetValue(callback, out var shortcut))
                return No(callback + " gets a table; try " + shortcut + " instead");
            if (callback.StartsWith("OnCommand_"))
                return No("type /" + callback.Substring("OnCommand_".Length) + " instead");
            if (values.Count > 4)
                return No("a callback gets at most 4 values");
            return Ok(Call(callback, values.Select(Value).ToArray()));
        }

        static (TryPlan, string) Chat(string word, string type, List<string> words, string what)
        {
            if (words.Count < 2)
                return No(word + " needs a name, like " + word + " Friend hello");
            string text = Rest(what, 2);
            var t = new LuaTable(0, 3);
            t["type"] = type;
            t["sender"] = words[1];
            t["text"] = text.Length > 0 ? text : DefaultText;
            return Ok(Call("OnChatMessage", t));
        }

        static (TryPlan, string) Wallet(string word, List<string> words, ITryFacts facts)
        {
            if (words.Count != 2 || !int.TryParse(words[1], out int n) || n < 1)
                return No(word + " needs a whole amount, like " + word + " 100");
            var now = facts.Wallet() ?? new double[] { 0, 0 };
            double change = word == "earn" ? n : -n;
            return Ok(Call("OnWalletChanged", now[0] + change, now[1], change, 0.0));
        }

        static (TryPlan, string) Item(string word, List<string> words, ITryFacts facts)
        {
            int count = 1;
            if (
                words.Count < 2
                || words.Count > 3
                || !int.TryParse(words[1], out int id)
                || id < 0
                || (words.Count == 3 && (word == "koi" || !int.TryParse(words[2], out count) || count < 1))
            )
                return (
                    null,
                    word == "koi"
                        ? "koi needs the koi's id, like koi 160"
                        : "item needs an id, like item 101 or item 1 5"
                );
            var t = word == "koi" ? facts.Item(9999, 1, id) : facts.Item(id, count, 0);
            if (t == null)
                return No("there is no item " + id + " (_names.txt lists them)");
            return Ok(Call("OnItemGained", t));
        }

        static (TryPlan, string) Tooltip(List<string> words, ITryFacts facts)
        {
            int meta = 0;
            if (
                words.Count < 2
                || words.Count > 3
                || !int.TryParse(words[1], out int id)
                || (words.Count == 3 && !int.TryParse(words[2], out meta))
            )
                return No("tooltip needs an item id, like tooltip 101 (a koi: tooltip 9999 160)");
            var t = facts.Item(id, 1, meta);
            if (t == null)
                return No("there is no item " + id + " (_names.txt lists them)");
            t["count"] = LuaValue.Nil;
            return Ok(Call("OnItemTooltip", t));
        }

        static (TryPlan, string) Key(List<string> words, ITryFacts facts)
        {
            string id =
                words.Count >= 2 ? words[1]
                : facts.KeyIds.Count == 1 ? facts.KeyIds[0]
                : null;
            if (id != null && facts.KeyIds.Contains(id))
                return Ok(Call("OnKey", id));
            return (
                null,
                facts.KeyIds.Count == 0
                    ? "it has no key setting in settings.lua"
                    : "key takes the id of a key setting: " + string.Join(", ", facts.KeyIds)
            );
        }

        static (TryPlan, string) Setting(List<string> words, ITryFacts facts)
        {
            if (words.Count == 2 && facts.SettingIds.Contains(words[1]))
                return Ok(Call("OnSettingChanged", words[1], facts.Setting(words[1])));
            return (
                null,
                facts.SettingIds.Count == 0
                    ? "it has no settings.lua"
                    : "setting takes a setting's id: " + string.Join(", ", facts.SettingIds)
            );
        }

        // A typed value: a number, true, false, nil, or text ("quoted" for spaces).
        public static LuaValue Value(string word)
        {
            if (word == "true" || word == "false")
                return word == "true";
            if (word == "nil")
                return LuaValue.Nil;
            if (Number(word, out double d))
                return d;
            return word;
        }

        static bool Number(string s, out double d) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
            && !double.IsNaN(d)
            && !double.IsInfinity(d);

        // Words split at spaces; "a quoted text" is one word.
        public static List<string> Words(string s)
        {
            var words = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false,
                any = false;
            foreach (char c in s ?? "")
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                }
                else if (c == ' ' && !quoted)
                {
                    if (any)
                        words.Add(cur.ToString());
                    cur.Clear();
                    any = false;
                }
                else
                {
                    cur.Append(c);
                    any = true;
                }
            }
            if (any)
                words.Add(cur.ToString());
            return words;
        }

        // The text after the first n words, as typed.
        static string Rest(string s, int n)
        {
            s = (s ?? "").Trim();
            for (int i = 0; i < n; i++)
            {
                int sp = s.IndexOf(' ');
                s = sp < 0 ? "" : s.Substring(sp + 1).TrimStart();
            }
            return s;
        }

        // Lua's way of writing a value, for the chat line.
        public static string Show(LuaValue v)
        {
            switch (v.Type)
            {
                case LuaValueType.String:
                    return "\"" + v.Read<string>() + "\"";
                case LuaValueType.Number:
                    return v.Read<double>().ToString("0.###", CultureInfo.InvariantCulture);
                case LuaValueType.Boolean:
                    return v.Read<bool>() ? "true" : "false";
                case LuaValueType.Table:
                    var t = v.Read<LuaTable>();
                    var keys = new[] { "type", "sender", "text", "id", "name", "count", "meta", "category" };
                    return "{ "
                        + string.Join(
                            ", ",
                            keys.Where(k => t[k].Type != LuaValueType.Nil).Select(k => k + " = " + Show(t[k]))
                        )
                        + " }";
                default:
                    return "nil";
            }
        }
    }
}
