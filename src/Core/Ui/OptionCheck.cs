using System;
using System.Collections.Generic;
using Lua;

namespace ShorelandsMods.Core.Ui
{
    // Reading an options table: unknown keys are named with a suggestion, wrong values say what they should be.
    internal static class OptionCheck
    {
        // "colour" is read as "color", for British authors.
        public static string Spelling(string key) => key == "colour" ? "color" : key;

        // The value of an option under either spelling, with the name the mod used.
        public static LuaValue Get(LuaTable t, string key, out string name)
        {
            name = key;
            var v = t[key];
            if (v.Type == LuaValueType.Nil && key == "color")
            {
                v = t["colour"];
                if (v.Type != LuaValueType.Nil)
                    name = "colour";
            }
            return v;
        }

        // Unknown options newly named in one call, at most; a later call names the next.
        public const int MaxUnknownTold = 3;

        // An unknown option is refused (named with a suggestion) and left out; the known ones apply. A table with
        // more entries than there are options is not walked (it cannot be all options).
        public static void Known(ApiCall c, LuaTable t, string[] allowed, int argN)
        {
            if (t.HashMapCount > allowed.Length + 1)
            {
                c.Refuse(
                    "opts",
                    "argument "
                        + argN
                        + " (opts) has "
                        + t.HashMapCount
                        + " entries, more than there are options, so only the known ones are read"
                );
                return;
            }
            int told = 0;
            foreach (var kv in t)
            {
                if (!kv.Key.TryRead<string>(out var key))
                    throw c.Error(
                        "argument "
                            + argN
                            + " (opts) must be a table of named options like { anchor = \"top_left\" }, but it has "
                            + LuaText.Describe(kv.Key)
                            + " as a key"
                    );
                if (Array.IndexOf(allowed, Spelling(key)) >= 0)
                    continue;
                string name = TextEscape.Clip(key, 40);
                if (c.Told("opts." + name))
                    continue;
                if (told++ == MaxUnknownTold)
                    return;
                string near = ErrorText.Suggest(name, allowed);
                c.Refuse(
                    "opts." + name,
                    "the option \""
                        + name
                        + "\" is not known"
                        + (near != null ? " (did you mean \"" + near + "\"?)" : "")
                        + " and is left out. Options: "
                        + string.Join(", ", allowed)
                );
            }
        }

        public static int? Int(ApiCall c, LuaTable t, string key, int min, int max)
        {
            var v = t[key];
            if (v.Type == LuaValueType.Nil)
                return null;
            if (!v.TryRead<double>(out var d) || v.Type != LuaValueType.Number || double.IsNaN(d))
                throw c.Error("option " + key + " must be a number, but it is " + LuaText.Describe(v));
            if (d < min || d > max)
            {
                double clamped = d < min ? min : max;
                c.Refuse(
                    "opts." + key,
                    "the option "
                        + key
                        + " must be from "
                        + min
                        + " to "
                        + max
                        + "; "
                        + LuaText.Num(d)
                        + " was changed to "
                        + LuaText.Num(clamped)
                );
                d = clamped;
            }
            return (int)Math.Round(d);
        }

        public static bool? Bool(ApiCall c, LuaTable t, string key)
        {
            var v = t[key];
            if (v.Type == LuaValueType.Nil)
                return null;
            if (v.Type != LuaValueType.Boolean)
                throw c.Error("option " + key + " must be true or false, but it is " + LuaText.Describe(v));
            return v.Read<bool>();
        }

        // Null when left out, or refused (a text that is not a colour).
        public static Rgba? Color(ApiCall c, LuaTable t, string option)
        {
            var v = Get(t, option, out var key);
            if (v.Type == LuaValueType.Nil)
                return null;
            if (v.TryRead<string>(out var hex))
            {
                if (Rgba.TryParseHex(hex, out var col))
                    return col;
                c.Refuse(
                    "opts." + key,
                    "the option "
                        + key
                        + " must be a colour like \"#ffcc00\", not \""
                        + TextEscape.Clip(hex, 40)
                        + "\"; it is left out"
                );
                return null;
            }
            if (Rgba.TryRead(v, out var tc))
                return tc;
            throw c.Error(
                "option "
                    + key
                    + " must be a colour like \"#ffcc00\" or ColorFromHex(...), but it is "
                    + LuaText.Describe(v)
            );
        }

        public static string Hex(Rgba c) =>
            "#"
            + ((int)Math.Round(c.R * 255)).ToString("X2")
            + ((int)Math.Round(c.G * 255)).ToString("X2")
            + ((int)Math.Round(c.B * 255)).ToString("X2")
            + (c.A < 1 ? ((int)Math.Round(c.A * 255)).ToString("X2") : "");
    }
}
