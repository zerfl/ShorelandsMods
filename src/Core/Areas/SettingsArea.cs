using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    internal sealed class SettingsArea : Area
    {
        public sealed class State
        {
            public List<SettingDef> Defs;
            public ISettingsStore Store;
            public double WrittenAt = double.NegativeInfinity;
        }

        double nextCheck;

        public override string Name => "Settings";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "ModSettingGet( id:string ) -> value",
                "a setting of this mod from settings.lua (\"speed\" or Noita's \"modid.speed\"): true/false, a number "
                    + "(within value_min..value_max), a text (at most text_max_length characters, default "
                    + SettingsSchema.TextDefaultLength
                    + "), a key name, or for a choice the id of the chosen value; nil for an id settings.lua does not have",
                Get
            );
            s.Function(
                "ModSettingSet( id:string, value ) -> ok:boolean",
                "changes a setting of this mod at once; the settings file is written within a second, at most every "
                    + Limits.SettingsWriteSeconds
                    + " s, and when the mod stops (OnSettingChanged is not called for it); a number "
                    + "outside its range is clamped and a too long text cut (false); a choice that does not exist is not "
                    + "set (false); a key setting is never set (false): only the player changes keys",
                SetValue
            );
            s.Callback(
                "OnSettingChanged( id:string, value )",
                "a setting of this mod was changed outside the mod: on the Mods page, or in its settings file while "
                    + "the game runs (within a second); value as ModSettingGet gives it. Not for the mod's own ModSettingSet"
            );
        }

        protected internal override void ModStarting(Mod mod)
        {
            if (!File.Exists(Path.Combine(mod.Dir, "settings.lua")))
                return;
            const string Prefix = "settings.lua: ";
            var rt = ModRuntime.ForSettings(mod.Id, mod.Dir, (line, length) =>
                Host.Area<OutputArea>()?.Print(mod, Prefix + line, Prefix.Length + length)
            );
            rt.RunFile("settings.lua", Limits.Init);
            Host.ChargeWork(mod, "settings.lua", rt.LastWallMs);
            var defs = SettingsSchema.Read(rt.Global("mod_settings"), Host.Env.IsKeyName);
            var st = mod.Get<State>();
            st.Defs = defs;
            st.Store = Host.Env.OpenSettings(mod.Id, defs);
        }

        // Every second: edits of the settings files, then the mods' own changes, each file at most every
        // Limits.SettingsWriteSeconds.
        protected internal override void Update(double now)
        {
            if (now < nextCheck)
                return;
            nextCheck = now + 1;
            var mods = Host.Mods;
            for (int m = 0; m < mods.Count; m++)
            {
                var mod = mods[m];
                var st = mod.Find<State>();
                if (mod.Status != ModStatus.Loaded || st?.Store == null)
                    continue;
                var edited = st.Store.ReloadIfChanged();
                if (st.Store.Dirty && now >= st.WrittenAt + Limits.SettingsWriteSeconds)
                    Write(mod, st, now);
                foreach (var id in edited)
                {
                    var def = st.Defs.FirstOrDefault(d => d.Id == id);
                    var fn = mod.Lua?.Global("OnSettingChanged") ?? LuaValue.Nil;
                    if (def == null || fn.Type != LuaValueType.Function)
                        continue;
                    if (!Host.Call(mod, "OnSettingChanged", fn, 2, new Args(id, SettingsSchema.ToLua(Value(st, def)))))
                        break;
                }
            }
        }

        protected internal override void ModStopping(Mod mod)
        {
            var st = mod.Find<State>();
            if (st?.Store != null && st.Store.Dirty)
                Write(mod, st, Host.Env.Now);
        }

        void Write(Mod mod, State st, double now)
        {
            try
            {
                st.Store.Flush();
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                Host.WriteFailed(mod, "saving its settings", e);
            }
            st.WrittenAt = now;
        }

        // What the mod sees: the stored value made to fit (a cfg edited by hand may hold anything).
        public static object Value(State st, SettingDef def) => def.Fit(st.Store.Get(def));

        // The Mods page changed a setting: saved at once, and the mod hears it as OnSettingChanged. False if the value
        // does not fit the setting.
        public bool SetFromPage(Mod mod, SettingDef def, object value)
        {
            var st = mod.Find<State>();
            if (st?.Store == null || def.Check(value) != null)
                return false;
            if (def.Type == SettingType.Key && !Host.Env.IsKeyName((string)value))
                return false;
            if (Equals(st.Store.Get(def), value))
                return true;
            st.Store.Set(def, value);
            Write(mod, st, Host.Env.Now);
            var fn = mod.Lua?.Global("OnSettingChanged") ?? LuaValue.Nil;
            if (mod.Status == ModStatus.Loaded && fn.Type == LuaValueType.Function)
                Host.Call(mod, "OnSettingChanged", fn, 2, new Args(def.Id, SettingsSchema.ToLua(value)));
            return true;
        }

        // Null (refused) for an id this mod's settings.lua does not have.
        static SettingDef Find(ApiCall c, out State st)
        {
            string id = c.String(1, "id");
            st = c.Mod.Find<State>();
            int dot = id.IndexOf('.');
            if (dot >= 0)
            {
                if (id.Substring(0, dot) != c.Mod.Id)
                {
                    c.Refuse(1, "a mod can only use its own settings, not \"" + TextEscape.Clip(id, 80) + "\"");
                    return null;
                }
                id = id.Substring(dot + 1);
            }
            if (st?.Defs == null)
            {
                c.Refuse(1, "the mod has no settings.lua, so there is no setting \"" + TextEscape.Clip(id, 80) + "\"");
                return null;
            }
            var def = st.Defs.FirstOrDefault(d => d.Id == id);
            if (def == null)
            {
                string near = ErrorText.Suggest(id, st.Defs.Select(d => d.Id));
                c.Refuse(
                    1,
                    "settings.lua has no setting \""
                        + TextEscape.Clip(id, 80)
                        + "\""
                        + (near != null ? "; did you mean \"" + near + "\"?" : "")
                );
            }
            return def;
        }

        static int Get(ApiCall c)
        {
            var def = Find(c, out var st);
            return c.Return(def == null ? LuaValue.Nil : SettingsSchema.ToLua(Value(st, def)));
        }

        int SetValue(ApiCall c)
        {
            var def = Find(c, out var st);
            if (def == null)
                return c.ReturnOk();
            // A mod that could move its keys could listen to any key of the keyboard in turn.
            if (def.Type == SettingType.Key)
            {
                c.Refuse(
                    1,
                    "\""
                        + def.Id
                        + "\" is a key setting, which only the player changes (on the Mods page or in the settings "
                        + "file), so it was not changed"
                );
                return c.ReturnOk();
            }
            object value = SettingsSchema.FromLua(c.Arg(2));
            bool typeFits =
                def.Type == SettingType.Boolean ? value is bool
                : def.Type == SettingType.Number ? value is double
                : value is string;
            if (!typeFits)
                throw c.Wrong(2, "value", def.Check(null) + " for \"" + def.Id + "\"");
            bool known = def.Type != SettingType.Choice || def.Check(value) == null;
            if (!known)
            {
                c.Refuse(
                    2,
                    "\""
                        + TextEscape.Clip((string)value, 40)
                        + "\" is not "
                        + def.Check(null)
                        + " for \""
                        + def.Id
                        + "\", so the setting was not changed"
                );
                return c.ReturnOk();
            }
            object fitted = def.Fit(value);
            if (!Equals(fitted, value))
                c.Refuse(
                    2,
                    def.Id
                        + " must be "
                        + def.Check(value)
                        + "; "
                        + Describe(value)
                        + " was changed to "
                        + Describe(fitted)
                );
            st.Store.Set(def, fitted);
            return c.ReturnOk();
        }

        static string Describe(object v) =>
            v is string t ? "a text of " + t.Length + " characters" : LuaText.Num((double)v);
    }
}
