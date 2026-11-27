using System;
using System.Collections.Generic;
using System.Globalization;
using Lua;

namespace ShorelandsMods.Core
{
    internal enum SettingType
    {
        Boolean,
        Number,
        Choice,
        Text,
        Key,
    }

    internal sealed class SettingDef
    {
        public string Id,
            Name,
            Description;
        public SettingType Type;

        // bool, double or string by type.
        public object Default;
        public double Min,
            Max;
        public List<KeyValuePair<string, string>> Choices;
        public int MaxLength;
        public Func<string, bool> IsKeyName;

        // Null if the value fits this setting, else the sentence that says what is expected.
        public string Check(object value)
        {
            switch (Type)
            {
                case SettingType.Boolean:
                    return value is bool ? null : "true or false";
                case SettingType.Number:
                    return value is double d && d >= Min && d <= Max
                        ? null
                        : "a number from " + Num(Min) + " to " + Num(Max);
                case SettingType.Choice:
                    if (value is string s)
                        foreach (var c in Choices)
                            if (c.Key == s)
                                return null;
                    return "one of " + string.Join(", ", Choices.ConvertAll(c => "\"" + c.Key + "\""));
                case SettingType.Text:
                    return value is string t && t.Length <= MaxLength
                        ? null
                        : "text of at most " + MaxLength + " characters";
                default:
                    return value is string k && (IsKeyName == null || IsKeyName(k)) ? null : "a key name like \"F7\"";
            }
        }

        // The value made to fit: a number clamped, a text cut to MaxLength, anything else that does not fit the default.
        public object Fit(object value)
        {
            if (Type == SettingType.Number && value is double d)
                return d < Min ? Min
                    : d > Max ? Max
                    : d;
            if (Type == SettingType.Text && value is string t && t.Length > MaxLength)
                return t.Substring(0, MaxLength);
            return Check(value) == null ? value : Default;
        }

        static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);
    }

    // The values of one mod's settings, kept by the host (BepInEx/config/mods.<id>.cfg in the game).
    internal interface ISettingsStore
    {
        object Get(SettingDef def);

        // Kept in memory until Flush writes the file.
        void Set(SettingDef def, object value);
        bool Dirty { get; }
        void Flush();

        // Re-reads the file if it changed outside the host; returns the ids whose value changed.
        IList<string> ReloadIfChanged();
    }

    // settings.lua in Noita's form: a global mod_settings table of { id, ui_name, ui_description, value_default,
    // value_min / value_max | values = { { "id", "label" }, ... } }, plus type = "key". Categories
    // ({ category_id, settings = { ... } }) are flattened; entries with not_setting = true are skipped. Everything
    // read is capped before it is walked (the Mods page draws a control per setting), so a hostile table costs at
    // most MaxEntries entries of work.
    internal static class SettingsSchema
    {
        public const int TextDefaultLength = 200,
            TextMaxLength = 1000,
            MaxSettings = 100,
            MaxEntries = 200,
            MaxDepth = 4,
            MaxChoices = 64,
            MaxIdChars = 64,
            MaxNameChars = 100,
            MaxDescriptionChars = 500;

        public static List<SettingDef> Read(LuaValue modSettings, Func<string, bool> isKeyName)
        {
            if (modSettings.Type == LuaValueType.Nil)
                throw new ModProblem("settings.lua must set mod_settings = { ... }");
            if (!modSettings.TryRead<LuaTable>(out var list))
                throw new ModProblem("settings.lua: mod_settings must be a table");
            var r = new Reader { IsKeyName = isKeyName };
            r.Collect(list, "", 0);
            return r.Defs;
        }

        sealed class Reader
        {
            public Func<string, bool> IsKeyName;
            public readonly List<SettingDef> Defs = new List<SettingDef>();
            readonly HashSet<string> ids = new HashSet<string>();
            readonly HashSet<LuaTable> open = new HashSet<LuaTable>();
            int entries,
                keys;

            public void Collect(LuaTable list, string path, int depth)
            {
                string at = path.TrimEnd('.');
                if (depth > MaxDepth)
                    throw new ModProblem(
                        "settings.lua: categories may be nested at most " + MaxDepth + " deep (at entry " + at + ")"
                    );
                if (!open.Add(list))
                    throw new ModProblem("settings.lua: the category at entry " + at + " contains itself");
                int n = list.ArrayLength;
                for (int i = 1; i <= n; i++)
                {
                    if (++entries > MaxEntries)
                        throw new ModProblem(
                            "settings.lua has more than "
                                + MaxEntries
                                + " entries (settings, headers and categories); at most "
                                + MaxSettings
                                + " settings fit on the Mods page"
                        );
                    if (!list[i].TryRead<LuaTable>(out var t))
                        throw new ModProblem(
                            "settings.lua: entry " + path + i + " of mod_settings must be a table { id = ..., ... }"
                        );
                    if (t["settings"].TryRead<LuaTable>(out var inner))
                    {
                        Collect(inner, path + i + ".", depth + 1);
                        continue;
                    }
                    if (t["not_setting"].ToBoolean())
                        continue;
                    var d = One(t, path + i, IsKeyName);
                    if (!ids.Add(d.Id))
                        throw new ModProblem("settings.lua: the id \"" + d.Id + "\" is used twice");
                    if (Defs.Count == MaxSettings)
                        throw new ModProblem(
                            "settings.lua has more than "
                                + MaxSettings
                                + " settings; the Mods page shows at most "
                                + MaxSettings
                        );
                    if (d.Type == SettingType.Key && ++keys > Limits.KeySettingsPerMod)
                        throw new ModProblem(
                            "settings.lua: a mod may have at most "
                                + Limits.KeySettingsPerMod
                                + " key settings, and \""
                                + d.Id
                                + "\" is one more"
                        );
                    Defs.Add(d);
                }
                open.Remove(list);
            }
        }

        static SettingDef One(LuaTable t, string where, Func<string, bool> isKeyName)
        {
            string id = Str(t, "id");
            if (string.IsNullOrEmpty(id))
                throw new ModProblem("settings.lua: setting " + where + " has no id");
            if (id.Length > MaxIdChars)
                throw new ModProblem(
                    "settings.lua: setting "
                        + where
                        + ": the id has "
                        + id.Length
                        + " characters; at most "
                        + MaxIdChars
                );
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    throw new ModProblem("settings.lua: the id \"" + id + "\" may only use letters, digits and _");
            string at = "settings.lua: setting \"" + id + "\": ";
            var d = new SettingDef
            {
                Id = id,
                Name = Capped(t, "ui_name", MaxNameChars, at) ?? id,
                Description = Capped(t, "ui_description", MaxDescriptionChars, at) ?? "",
            };
            var dflt = t["value_default"];
            string type = Capped(t, "type", MaxIdChars, at);
            if (type == "key")
            {
                d.Type = SettingType.Key;
                if (!dflt.TryRead<string>(out var key) || key.Length > MaxIdChars || !isKeyName(key))
                    throw new ModProblem(at + "value_default must be a key name like \"F7\"");
                d.Default = key;
                d.IsKeyName = isKeyName;
            }
            else if (t["values"].TryRead<LuaTable>(out var values))
            {
                d.Type = SettingType.Choice;
                d.Choices = new List<KeyValuePair<string, string>>();
                int n = values.ArrayLength;
                if (n > MaxChoices)
                    throw new ModProblem(at + "values has " + n + " entries; at most " + MaxChoices);
                for (int i = 1; i <= n; i++)
                {
                    if (!values[i].TryRead<LuaTable>(out var pair) || !pair[1].TryRead<string>(out var vid))
                        throw new ModProblem(at + "values must look like { { \"id\", \"Label\" }, ... }");
                    string label = pair[2].TryRead<string>(out var l) ? l : vid;
                    if (vid.Length > MaxNameChars || label.Length > MaxNameChars)
                        throw new ModProblem(
                            at + "value " + i + " has an id or label over " + MaxNameChars + " characters"
                        );
                    d.Choices.Add(new KeyValuePair<string, string>(vid, label));
                }
                if (d.Choices.Count == 0)
                    throw new ModProblem(at + "values is empty");
                d.Default = dflt.TryRead<string>(out var s) ? s : null;
            }
            else if (dflt.Type == LuaValueType.Boolean)
            {
                d.Type = SettingType.Boolean;
                d.Default = dflt.Read<bool>();
            }
            else if (dflt.Type == LuaValueType.Number)
            {
                d.Type = SettingType.Number;
                if (
                    !t["value_min"].TryRead<double>(out d.Min)
                    || !t["value_max"].TryRead<double>(out d.Max)
                    || d.Min > d.Max
                )
                    throw new ModProblem(at + "a number setting needs value_min and value_max (min not above max)");
                d.Default = dflt.Read<double>();
            }
            else if (dflt.Type == LuaValueType.String)
            {
                d.Type = SettingType.Text;
                d.MaxLength = t["text_max_length"].TryRead<double>(out var max)
                    ? (int)Math.Max(1, Math.Min(max, TextMaxLength))
                    : TextDefaultLength;
                d.Default = dflt.Read<string>();
            }
            else
                throw new ModProblem(at + "value_default must be true/false, a number, a text, or one of values");
            if (type != null && type != "key" && type != TypeName(d.Type))
                throw new ModProblem(at + "type = \"" + type + "\" does not match value_default");
            string bad = d.Check(d.Default);
            if (bad != null)
                throw new ModProblem(at + "value_default must be " + bad);
            return d;
        }

        // A text field, or null; longer than max is the author's mistake.
        static string Capped(LuaTable t, string key, int max, string at)
        {
            string s = Str(t, key);
            if (s != null && s.Length > max)
                throw new ModProblem(at + key + " has " + s.Length + " characters; at most " + max);
            return s;
        }

        public static string TypeName(SettingType t) => t.ToString().ToLowerInvariant();

        static string Str(LuaTable t, string key) => t[key].TryRead<string>(out var s) ? s : null;

        public static LuaValue ToLua(object v) =>
            v is bool b ? new LuaValue(b)
            : v is double d ? new LuaValue(d)
            : v is string s ? new LuaValue(s)
            : LuaValue.Nil;

        public static object FromLua(LuaValue v) =>
            v.Type == LuaValueType.Boolean ? v.Read<bool>()
            : v.Type == LuaValueType.Number ? v.Read<double>()
            : v.Type == LuaValueType.String ? (object)v.Read<string>()
            : null;
    }
}
