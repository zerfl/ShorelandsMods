using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;

// Settings (settings.lua, controls, text settings, writes) and keys.
static partial class Program
{
    static void Settings()
    {
        List<SettingDef> Read(string src)
        {
            string dir = TempDir("settings");
            File.WriteAllText(Path.Combine(dir, "settings.lua"), src);
            var rt = ModRuntime.ForSettings("s", dir, (_, _) => { });
            rt.RunFile("settings.lua", Limits.Init);
            return SettingsSchema.Read(rt.Global("mod_settings"), n => n == "F7");
        }
        var defs = Read(
            @"
mod_settings = {
  { id = 'show', ui_name = 'Show', ui_description = 'd', value_default = true, scope = MOD_SETTING_SCOPE_RUNTIME },
  { id = 'header', ui_name = 'Section', not_setting = true },
  { category_id = 'more', ui_name = 'More', settings = {
    { id = 'speed', ui_name = 'Speed', value_default = 2, value_min = 0, value_max = 5 },
    { id = 'mode', ui_name = 'Mode', value_default = 'b', values = { { 'a', 'Option A' }, { 'b', 'Option B' } } },
  } },
  { id = 'name', ui_name = 'Name', value_default = 'me', text_max_length = 10 },
  { id = 'toggle', ui_name = 'Toggle', type = 'key', value_default = 'F7' },
}
"
        );
        Check(
            string.Join(",", defs.Select(d => d.Id + ":" + d.Type))
                == "show:Boolean,speed:Number,mode:Choice,name:Text,toggle:Key",
            "settings.lua types, categories flattened, headers skipped",
            string.Join(",", defs.Select(d => d.Id + ":" + d.Type))
        );
        Check(defs[2].Choices.Count == 2 && defs[2].Choices[1].Value == "Option B", "choice labels");
        Check(
            defs[1].Check(6.0) != null && defs[1].Check(5.0) == null && defs[3].Check("12345678901") != null,
            "range and length checks"
        );
        var bad = new (string src, string expect)[]
        {
            ("x = 1", "must set mod_settings"),
            ("mod_settings = { { id = 'n', value_default = 3 } }", "value_min and value_max"),
            (
                "mod_settings = { { id = 'm', value_default = 'z', values = { { 'a', 'A' } } } }",
                "value_default must be one of \"a\""
            ),
            (
                "mod_settings = { { id = 'a', value_default = true }, { id = 'a', value_default = false } }",
                "used twice"
            ),
            ("mod_settings = { { id = 'k', type = 'key', value_default = 'NoSuchKey' } }", "key name"),
            ("mod_settings = { { value_default = true } }", "has no id"),
            ("mod_settings = { { id = 'b', type = 'number', value_default = true } }", "does not match"),
        };
        foreach (var (src, expect) in bad)
        {
            string p = Problem(() => Read(src));
            Check(p.Contains(expect), "settings.lua problem: " + expect, p);
        }

        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "s",
            @"
Out(ModSettingGet('speed'), ModSettingGet('s.show'), ModSettingGet('mode'))
Out(ModSettingSet('speed', 4), ModSettingGet('speed'))
Out(ModSettingSet('speed', 9), ModSettingGet('speed'))
Out(pcall(ModSettingSet, 'show', 'yes'))
Out(ModSettingGet('other.speed'), ModSettingGet('sped'))
Out(ModSettingSet('toggle', 'no key'), ModSettingGet('toggle'), ModSettingSet('mode', 'c'), ModSettingGet('mode'))
function OnSettingChanged(id, value) Out('changed', id, value) end
"
        );
        rig.Write(
            "s",
            "settings.lua",
            "mod_settings = { { id = 'show', value_default = true }, { id = 'speed', value_default = 2, value_min = 0, value_max = 5 },"
                + " { id = 'mode', value_default = 'a', values = { { 'a', 'A' }, { 'b', 'B' } } }, { id = 'toggle', type = 'key', value_default = 'F7' } }"
        );
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        string all = string.Join("|", o);
        Check(o.Count == 6 && o[0] == "2\ttrue\ta" && o[1] == "true\t4", "ModSettingGet / Set, both id forms", all);
        Check(o.Count == 6 && o[2] == "false\t5", "a number out of range is clamped and set; false", all);
        Check(
            o.Count == 6 && o[3].StartsWith("false\t") && o[3].Contains("must be true or false for \"show\""),
            "a value of another type is an error",
            all
        );
        Check(o.Count == 6 && o[4] == "nil\tnil", "another mod's or an unknown setting reads nil", all);
        Check(o.Count == 6 && o[5] == "false\tF7\tfalse\ta", "unknown key and choice values are not set", all);
        string chat = rig.Env.ChatRead;
        Check(
            chat.Contains("ModSettingSet: speed must be a number from 0 to 5; 9 was changed to 5"),
            "the clamp is told to the player",
            chat
        );
        Check(
            chat.Contains("only use its own settings") && !chat.Contains("did you mean \"speed\""),
            "one notice per function and argument: the other mod's id, not also the misspelt one",
            chat
        );
        Check(rig["s"].Status == ModStatus.Loaded, "refused values leave the mod running");
        Check(rig.Env.Settings["s"].Values["speed"] is double d && d == 5, "value stored in the settings store");
        rig.Env.Settings["s"].Edit("mode", "b");
        rig.Run(1.2);
        Check(o.Contains("changed\tmode\tb"), "external change calls OnSettingChanged", string.Join("|", o));
        rig.Env.Settings["s"].Edit("toggle", "no such key");
        rig.Run(1.2);
        Check(
            o.Contains("changed\ttoggle\tF7"),
            "a key name in the cfg that is no key reads as the default",
            string.Join("|", o)
        );
    }

    static void Controls()
    {
        var few = new SettingDef
        {
            Type = SettingType.Number,
            Min = 1,
            Max = 5,
            Default = 3.0,
        };
        Check(ControlSpec.For(few).Kind == ControlKind.Stepper, "a few whole numbers: stepper");
        var many = new SettingDef
        {
            Type = SettingType.Number,
            Min = 0,
            Max = 100,
            Default = 50.0,
        };
        var ms = ControlSpec.For(many);
        Check(ms.Kind == ControlKind.Slider && ms.Step == 1 && ms.Decimals == 0, "many whole numbers: slider by 1");
        var frac = new SettingDef
        {
            Type = SettingType.Number,
            Min = 0,
            Max = 1,
            Default = 0.5,
        };
        var fs = ControlSpec.For(frac);
        Check(
            fs.Kind == ControlKind.Slider && Math.Abs(fs.Step - 0.01) < 1e-9 && fs.Decimals == 2,
            "a 0..1 range slides by 0.01",
            fs.Step + " " + fs.Decimals
        );
        Check(fs.Snap(frac, 0.734) == 0.73 && fs.Snap(frac, 7) == 1 && fs.Snap(frac, -1) == 0, "snap and clamp");
        Check(fs.Show(0.5) == "0.50", "two decimals shown", fs.Show(0.5));
        var odd = new SettingDef
        {
            Type = SettingType.Number,
            Min = 0,
            Max = 10,
            Default = 2.5,
        };
        var os = ControlSpec.For(odd);
        Check(
            Math.Abs(os.Snap(odd, 2.5) - 2.5) < 1e-9,
            "the default value is reachable on the slider",
            os.Step + " -> " + os.Snap(odd, 2.5)
        );
    }

    // A text setting never reaches the mod longer than its text_max_length (default 200): not from settings.lua's
    // ModSettingSet, nor from a cfg edited by hand.
    static void TextSettings()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "t",
            @"
Out(ModSettingSet('name', string.rep('n', 30)), #ModSettingGet('name'))
function OnSettingChanged(id, value) Out('changed', id, #value) end
"
        );
        rig.Write(
            "t",
            "settings.lua",
            "mod_settings = { { id = 'name', value_default = 'me', text_max_length = 10 }, { id = 'free', value_default = '' },"
                + " { id = 'huge', value_default = '', text_max_length = 5000 } }"
        );
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        Check(o.Count > 0 && o[0] == "false\t10", "ModSettingSet cuts a long text and says false", string.Join("|", o));
        Check(rig.Env.ChatRead.Contains("a text of 30 characters was changed to a text of 10 characters"), "told");
        var defs = rig["t"].Find<SettingsArea.State>().Defs;
        Check(defs[1].MaxLength == 200 && defs[2].MaxLength == 1000, "text_max_length is 200 by default, at most 1000");
        rig.Env.Settings["t"].Edit("name", new string('c', 50));
        rig.Run(1.2);
        Check(o.Contains("changed\tname\t10"), "a long text from the cfg reaches the mod cut", string.Join("|", o));
        Check(
            rig.Host.Area<SettingsArea>().SetFromPage(rig["t"], defs[0], new string('p', 11)) == false,
            "the page refuses a long text"
        );
    }

    static void SettingsWriteFailure()
    {
        const string Settings =
            "mod_settings = { { id = 'n', ui_name = 'N', value_default = 1, value_min = 0, value_max = 9 } }";
        using var rig = new Rig();
        foreach (var id in new[] { "a_bad", "b_good" })
        {
            rig.Mod(id, "ModSettingSet('n', 5)");
            rig.Write(id, "settings.lua", Settings);
        }
        rig.Env.Settings["a_bad"] = new MemorySettings { FailFlush = new UnauthorizedAccessException("denied") };
        rig.Start();
        rig.Run(2);
        Check(rig.Env.Settings["b_good"].Writes >= 1, "a mod's failed settings write does not stop the next mod's");
        Check(Notices(rig, "a_bad", "saving its settings failed") == 1, "one notice", rig.Env.ChatRead);
        rig.Host.Reload("a_bad");
        Check(
            rig.Env.LogLines.Count(l => l.Contains("[a_bad] saving its settings failed")) >= 2,
            "the write at stop fails the same way, logged",
            string.Join("\n", rig.Env.LogLines)
        );
    }

    static void KeyApi()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "k",
            "function OnKey(id) Out('key', id) end function OnSettingChanged(id, v) Out('changed', id, v) end"
        );
        rig.Write(
            "k",
            "settings.lua",
            "mod_settings = { { id = 'open', ui_name = 'Open', type = 'key', value_default = 'F7' } }"
        );
        rig.Start();
        rig.Ui.Down.Add("F7");
        rig.Ui.Down.Add("F8");
        rig.Run(0.05);
        Check(
            spy.Out.SequenceEqual(new[] { "key\topen" }),
            "OnKey for the declared key only",
            string.Join(";", spy.Out)
        );
        rig.Ui.KeysBlocked = true;
        rig.Ui.Down.Add("F7");
        rig.Run(0.05);
        Check(spy.Out.Count == 1, "no OnKey while typing");
        rig.Ui.KeysBlocked = false;
        var mod = rig["k"];
        var def = mod.Find<ShorelandsMods.Core.Areas.SettingsArea.State>().Defs[0];
        bool set = rig.Host.Area<ShorelandsMods.Core.Areas.SettingsArea>().SetFromPage(mod, def, "F9");
        Check(
            set && spy.Out.Last() == "changed\topen\tF9",
            "rebinding on the Mods page reaches the mod as OnSettingChanged"
        );
        rig.Ui.Down.Add("F7");
        rig.Run(0.05);
        rig.Ui.Down.Add("F9");
        rig.Run(0.05);
        Check(
            spy.Out.Count(o => o.StartsWith("key")) == 2 && spy.Out.Last() == "key\topen",
            "after rebinding the new key fires and the old one does not"
        );
        Check(
            !rig.Host.Area<ShorelandsMods.Core.Areas.SettingsArea>().SetFromPage(mod, def, "notakey"),
            "a key name the game does not know is refused"
        );
    }

    static void KeyClashes()
    {
        using var rig = new Rig();
        string Settings(params (string id, string name, string key)[] keys) =>
            "mod_settings = { "
            + string.Join(
                ", ",
                keys.Select(k =>
                    "{ id = '" + k.id + "', ui_name = '" + k.name + "', type = 'key', value_default = '" + k.key + "' }"
                )
            )
            + " }";
        foreach (var id in new[] { "a", "b", "c", "d", "e" })
            rig.Mod(id, "", "<Mod name=\"Mod " + id.ToUpperInvariant() + "\" />");
        rig.Write("a", "settings.lua", Settings(("open", "Open the list", "K")));
        rig.Write("b", "settings.lua", Settings(("hide", "Hide the text", "K")));
        rig.Write("c", "settings.lua", Settings(("show", "Show", "F7")));
        rig.Write("d", "settings.lua", Settings(("use", "Use", "E")));
        rig.Write("e", "settings.lua", Settings(("one", "One", "J"), ("two", "Two", "J")));
        rig.Ui.TakenKeys.Add(("F2", "the key of inspect mode"));
        rig.Ui.TakenKeys.Add(("E", "the game's key for Interact"));
        rig.Start();
        rig.Run(0.1);
        var keys = rig.Host.Area<KeyArea>();
        SettingDef Def(string mod, int i = 0) => rig[mod].Find<SettingsArea.State>().Defs[i];
        List<string> Said() => rig.Env.ChatLines.Select(l => l.Replace('＂', '"').Replace('＇', '\'')).ToList();
        var said = Said();
        Check(
            said.Count(l =>
                l.Contains(
                    "[b] K is also Mod A's key for \"Open the list\": one press does both. Change one on the Mods page."
                )
            ) == 1
                && !said.Any(l => l.Contains("[a] K is also")),
            "two mods on one key: one chat line, from the later mod",
            string.Join("|", said)
        );
        Check(
            said.Any(l =>
                l.Contains(
                    "[d] E is also the game's key for Interact: one press does both. Change it on the Mods page."
                )
            ),
            "a mod key the game uses is said",
            string.Join("|", said)
        );
        Check(
            said.Any(l => l.Contains("[e] J is also this mod's key for \"One\"")),
            "two settings of one mod on one key are said"
        );
        Check(!said.Any(l => l.Contains("F7")), "a key nothing else uses is not mentioned");
        Check(
            keys.ClashOf(rig["a"], Def("a")) == "K is also Mod B's key for \"Hide the text\": one press does both."
                && keys.ClashOf(rig["b"], Def("b"))
                    == "K is also Mod A's key for \"Open the list\": one press does both.",
            "the Mods page names the other mod on both settings",
            keys.ClashOf(rig["a"], Def("a"))
        );
        Check(
            keys.ClashOf(rig["c"], Def("c")) == null && keys.HasClash(rig["a"]) && !keys.HasClash(rig["c"]),
            "the list marks only the mods with a shared key"
        );
        int before = rig.Env.ChatLines.Count;
        rig.Run(2);
        rig.Host.Reload("b");
        rig.Run(1);
        Check(rig.Env.ChatLines.Skip(before).All(l => !l.Contains(" is also ")), "said once, also across a reload");
        rig.Write("b", "init.lua", "x = = 1");
        rig.Host.Reload("b");
        rig.Run(1);
        rig.Write("b", "init.lua", "");
        rig.Host.Reload("b");
        rig.Run(1);
        Check(
            rig.Env.ChatLines.Skip(before).All(l => !l.Contains(" is also ")),
            "a mod that failed and was fixed does not repeat it",
            string.Join("|", rig.Env.ChatLines.Skip(before))
        );
        var settings = rig.Host.Area<SettingsArea>();
        settings.SetFromPage(rig["b"], Def("b"), "L");
        keys.CheckClashes();
        Check(keys.ClashOf(rig["a"], Def("a")) == null, "a new key ends the clash at once");
        settings.SetFromPage(rig["b"], Def("b"), "K");
        rig.Run(0.6);
        Check(
            rig.Env.ChatLines.Skip(before).Count(l => l.Contains("[b] K is also")) == 1,
            "a clash made again after it was resolved is said again"
        );
        settings.SetFromPage(rig["c"], Def("c"), "K");
        keys.CheckClashes();
        Check(
            keys.ClashOf(rig["a"], Def("a"))
                == "K is also Mod B's key for \"Hide the text\" and Mod C's key for \"Show\": one press does all of them.",
            "three on one key",
            keys.ClashOf(rig["a"], Def("a"))
        );
        settings.SetFromPage(rig["c"], Def("c"), "F2");
        rig.Host.SwitchOff("a", true);
        keys.CheckClashes();
        Check(
            keys.ClashOf(rig["b"], Def("b")) == null,
            "a switched-off mod's key reacts to nothing, so it clashes with nothing"
        );
        Check(
            keys.ClashOf(rig["c"], Def("c")) == "F2 is also the key of inspect mode: one press does both.",
            "the inspect key is named"
        );
    }

    // No two shipped examples share a default key, and none uses a key of the game, inspect mode, or F6 to F10, which
    // other plugins often take. The guide's sample key is free too.
    static void ShippedKeys()
    {
        string dir = Files.RepoRoot();
        if (dir == null)
        {
            Check(false, "the repository is found from the test's folder");
            return;
        }
        var taken = new HashSet<string>
        {
            "W",
            "A",
            "S",
            "D",
            "E",
            "Tab",
            "P",
            "O",
            "Enter",
            "Escape",
            "F2",
            "F6",
            "F7",
            "F8",
            "F9",
            "F10",
        };
        for (int i = 1; i <= 7; i++)
            taken.Add("Digit" + i);
        List<string> KeysOf(string modDir)
        {
            var rt = ModRuntime.ForSettings("x", modDir, (_, _) => { });
            rt.RunFile("settings.lua", Limits.Init);
            return SettingsSchema
                .Read(rt.Global("mod_settings"), _ => true)
                .Where(d => d.Type == SettingType.Key)
                .Select(d => (string)d.Default)
                .ToList();
        }
        var shipped = Directory
            .GetDirectories(Path.Combine(dir, "examples"))
            .Where(mod => File.Exists(Path.Combine(mod, "settings.lua")))
            .SelectMany(mod => KeysOf(mod).Select(key => (Mod: Path.GetFileName(mod), Key: key)))
            .ToList();
        CheckEach("the examples' keys are not the game's or a plugin's, nor another example's", shipped, 2, k =>
            taken.Contains(k.Key) ? "taken"
            : shipped.Count(o => o.Key == k.Key) > 1 ? "used twice"
            : null
        );
        var owner = shipped.GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.First().Mod);
        string guide = File.ReadAllText(Path.Combine(dir, "guide", "guide.md"));
        string tmp = TempDir("guide-keys");
        var samples = Regex
            .Matches(guide, "```lua sample=\\S+ file=settings.lua\\n(.*?)```", RegexOptions.Singleline)
            .SelectMany(m =>
            {
                File.WriteAllText(Path.Combine(tmp, "settings.lua"), m.Groups[1].Value);
                return KeysOf(tmp);
            })
            .ToList();
        CheckEach("the guide's sample keys are free", samples, 1, key =>
            taken.Contains(key) ? "taken"
            : owner.TryGetValue(key, out var other) ? "used by " + other
            : null
        );
    }

    static void KeySettingsOnlyByPlayer()
    {
        using var rig = new Rig();
        rig.Mod("k", "ok = ModSettingSet('open', 'F9') ok2 = ModSettingSet('open', 'F10')");
        rig.Write(
            "k",
            "settings.lua",
            "mod_settings = { { id = 'open', ui_name = 'Open', type = 'key', value_default = 'F7' } }"
        );
        rig.Start();
        var mod = rig["k"];
        var settings = rig.Host.Area<SettingsArea>();
        var def = mod.Find<SettingsArea.State>().Defs[0];
        Check(
            mod.Lua.Global("ok").ToString() == "false"
                && (string)SettingsArea.Value(mod.Find<SettingsArea.State>(), def) == "F7",
            "ModSettingSet leaves a key setting as it is and returns false"
        );
        Check(Notices(rig, "k", "key setting") == 1, "one notice", rig.Env.ChatRead);
        Check(settings.SetFromPage(mod, def, "F9"), "the player still changes it on the Mods page");
    }
}
