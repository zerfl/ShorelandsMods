using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Xml;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

// Loading, reloading and failing mods, and the host's own folders.
static partial class Program
{
    // ---- host behaviour ----

    static void Lifecycle()
    {
        using var rig = new Rig();
        rig.Mod("good", "x = 1");
        rig.Mod("assets", null);
        rig.Write("Bad Name", "mod.xml", "<Mod name=\"x\"/>");
        rig.Write("noxml", "init.lua", "x = 1");
        rig.Write("txtxml", "mod.xml.txt", "<Mod name=\"x\"/>");
        rig.Mod("txtinit", null);
        rig.Write("txtinit", "init.lua.txt", "x = 1");
        rig.Mod("future", "x = 1", "<Mod name=\"f\" needs_host=\"9.9\"/>");
        rig.Mod("broken", "x = = 1");
        rig.Mod("brokenxml", "x = 1", "<Mod name=\"x\"");
        rig.Mod("offmod", "x = 1");
        rig.Env.Off.Add("offmod");
        rig.Start();
        string S(string id) => rig[id].Status + ": " + rig[id].Reason;
        Check(rig["good"].Status == ModStatus.Loaded, "good mod loads");
        Check(
            rig["assets"].Status == ModStatus.Loaded && rig["assets"].Lua == null,
            "mod without init.lua loads without Lua"
        );
        Check(
            rig["Bad Name"].Status == ModStatus.Skipped && rig["Bad Name"].Reason.Contains("lower case"),
            "bad folder name skipped",
            S("Bad Name")
        );
        Check(
            rig["noxml"].Status == ModStatus.Skipped && rig["noxml"].Reason.Contains("mod.xml"),
            "missing mod.xml skipped"
        );
        Check(
            rig["txtxml"].Status == ModStatus.Skipped && rig["txtxml"].Reason.Contains("called mod.xml.txt"),
            "mod.xml.txt named as the cause",
            S("txtxml")
        );
        Check(
            rig["txtinit"].Status == ModStatus.Loaded
                && rig.Env.ChatText.Contains("init.lua is not run: the file is called init.lua.txt"),
            "init.lua.txt named in a notice",
            rig.Env.ChatText
        );
        Check(
            rig["future"].Status == ModStatus.Skipped
                && rig["future"].Reason.Contains("needs host 9.9.0, you have 9.8.7"),
            "needs_host 9.9 skipped with both versions",
            S("future")
        );
        Check(
            rig["broken"].Status == ModStatus.Failed && rig["broken"].Reason.StartsWith("init.lua line 1"),
            "syntax error fails with file and line",
            S("broken")
        );
        Check(rig["brokenxml"].Status == ModStatus.Failed, "broken mod.xml fails");
        Check(rig["offmod"].Status == ModStatus.Off, "switched-off mod is off");
        var order = rig.Host.Mods.Select(m => m.Id).ToList();
        Check(
            order.SequenceEqual(order.OrderBy(i => i, StringComparer.Ordinal)),
            "alphabetical load order",
            string.Join(",", order)
        );
        Check(
            rig.Env.ChatLines.Count(l => !l.Contains("txtinit")) == 1
                && rig.Env.ChatLines.First(l => !l.Contains("txtinit")).Contains("broken")
                && rig.Env.ChatLines.First(l => !l.Contains("txtinit")).Contains("line 1"),
            "one chat line for the failed mod",
            rig.Env.ChatText
        );
        Check(File.Exists(Path.Combine(rig.Env.ModsDir, "_api.txt")), "_api.txt written");
    }

    static void Reload()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "r",
            @"
n = (DataLoad('starts', 0)) + 1
DataSave('starts', n)
CommandRegister('rr', 'test')
TimerAfter(1, function() Out('old timer') end)
TimerEvery(0.2, function() Out('tick v1') end)
function OnModUnload() Out('unload v1') end
"
        );
        rig.Write(
            "r",
            "settings.lua",
            "mod_settings = { { id = 'speed', ui_name = 'Speed', value_default = 2, value_min = 0, value_max = 5 } }"
        );
        rig.Start();
        var spy = rig.Host.Area<SpyArea>();
        rig.Run(0.5);
        Check(spy.Out.Contains("tick v1"), "v1 timer runs");
        rig.Host.Area<SettingsArea>();
        rig.Env.Settings["r"].Values["speed"] = 4.0;
        rig.Mod(
            "r",
            @"
n = (DataLoad('starts', 0)) + 1
DataSave('starts', n)
Out('starts', n, ModSettingGet('speed'))
TimerEvery(0.2, function() Out('tick v2') end)
"
        );
        spy.Out.Clear();
        rig.Host.Reload("r");
        rig.Run(1.5);
        Check(spy.Out.Contains("unload v1"), "OnModUnload runs before the reload");
        Check(
            !spy.Out.Contains("old timer") && !spy.Out.Contains("tick v1"),
            "old timers are gone",
            string.Join("|", spy.Out)
        );
        Check(spy.Out.Contains("tick v2"), "new timer runs");
        Check(spy.Out.Contains("starts\t2\t4"), "saved data and settings survive", string.Join("|", spy.Out));
        Check(!rig.Commands.Added.ContainsKey("rr"), "old command is gone");
        Check(rig["r"].Resources.CountOf("timer") == 1, "one timer registered after reload");
    }

    static void Failures()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("bad", "function OnTick() local t = nil local x = t.field end");
        rig.Mod("typo", "function OnTick() GamePrnt('x') end");
        rig.Mod("timer", "TimerEvery(0.1, function() error('in timer') end)");
        rig.Start();
        rig.Run(2);
        Check(rig["bad"].Status == ModStatus.Failed, "error in a callback switches the mod off");
        Check(
            rig.Env.ChatLines.Count(l => l.Contains("[bad]")) == 1,
            "only one chat line despite 20 ticks",
            rig.Env.ChatText
        );
        Check(
            rig.Env.ChatText.Contains("Did you mean GamePrint?"),
            "unknown function suggests the API name",
            rig.Env.ChatText
        );
        Check(rig.Env.ChatText.Contains("in timer"), "timer error reported");
        Check(
            rig.Env.ChatLines.All(l => !l.Contains("<b") && l.StartsWith("<color=")),
            "chat lines are escaped and coloured by the host"
        );

        // Back when the files change.
        rig.Mod("bad", "function OnTick() Out('fixed') end");
        rig.Host.Reload("bad");
        rig.Run(0.3);
        Check(
            rig["bad"].Status == ModStatus.Loaded && rig.Host.Area<SpyArea>().Out.Contains("fixed"),
            "a fixed mod loads again"
        );
    }

    // _originals (and any folder starting with _) is the host's: not a mod, and writing into it reloads nothing.
    static void HostFolders()
    {
        using var rig = new Rig();
        rig.Mod("real", "");
        rig.WriteBytes("_originals", "textures/hotbar_latest.png", Files.Png(4, 4));
        rig.Start();
        Check(
            rig.Host.Mods.Select(m => m.Id).SequenceEqual(new[] { "real" }),
            "_originals is not listed as a mod",
            string.Join(",", rig.Host.Mods.Select(m => m.Id))
        );
        string root = rig.Env.ModsDir;
        Check(
            ChangeWatch.ModIdOf(root, Path.Combine(root, "_originals", "textures", "x.png")) == null
                && ChangeWatch.ModIdOf(root, Path.Combine(root, "_originals")) == null
                && ChangeWatch.ModIdOf(root, Path.Combine(root, "real", "init.lua")) == "real",
            "a change inside _originals is no mod's change"
        );
    }

    static void BlockedLuaRunsNoSettings()
    {
        using var rig = new Rig();
        rig.Mod("settings_only", null);
        rig.Write("settings_only", "settings.lua", "print('settings ran') mod_settings = {}");
        rig.Mod("both", "x = 1");
        rig.Write("both", "settings.lua", "print('settings ran') mod_settings = {}");
        rig.Mod("assets_only", null);
        rig.Start(host => host.BlockLua("Lua is blocked"));
        Check(
            !rig.Env.Settings.ContainsKey("settings_only")
                && !rig.Env.Settings.ContainsKey("both")
                && !rig.Env.ChatRead.Contains("settings ran")
                && !rig.Env.LogLines.Any(l => l.Contains("settings ran")),
            "no settings.lua runs while Lua is blocked",
            string.Join(", ", rig.Env.Settings.Keys)
        );
        Check(
            rig["settings_only"].Status == ModStatus.Failed && rig["settings_only"].Reason.Contains("Lua is blocked"),
            "a mod with only settings.lua stops with the reason",
            rig["settings_only"].Status + ": " + rig["settings_only"].Reason
        );
        Check(rig["assets_only"].Status == ModStatus.Loaded, "a mod without Lua still loads");
    }

    // A file change that leaves mod.xml unusable is said in chat; a failing init.lua still only once.
    static void ReloadSaysManifestFailure()
    {
        using var rig = new Rig();
        rig.Mod("good", "x = 1");
        rig.Start();
        rig.Mod("blank", "x = 1", "<Mod name=\"b\" needs_host=\"\"/>");
        rig.Host.ReloadChanged("blank");
        Check(
            Notices(rig, "blank", "reloaded: failed: mod.xml: needs_host must be a host version") == 1,
            "a mod.xml that fails after a file change is said in chat",
            rig.Env.ChatRead
        );
        rig.Write("good", "init.lua", "error('boom')");
        rig.Host.ReloadChanged("good");
        Check(
            rig["good"].Status == ModStatus.Failed && rig.Env.ChatLines.Count(l => l.Contains("[good]")) == 1,
            "an init.lua that fails after a file change is said once",
            rig.Env.ChatRead
        );
    }
}
