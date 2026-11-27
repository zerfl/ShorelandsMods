using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lua;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

// Saved data, session values and exports, and their writes.
static partial class Program
{
    static void Data()
    {
        string root;
        using (var rig = new Rig(new SpyArea()))
        {
            root = rig.Env.Root;
            rig.Mod(
                "d",
                @"
DataSave('stats', { earned = 120, items = { 'Pumpkin', 'Leaf' }, nested = { deep = { ok = true } } })
local s = DataLoad('stats')
s.earned = 0
Out(DataLoad('stats').earned, DataLoad('stats').items[2], DataLoad('stats').nested.deep.ok, DataLoad('none', 'dflt'))
DataSave('gone', 1) DataSave('gone', nil) Out(DataLoad('gone'))
local cyc = {} cyc.self = cyc
for _, v in ipairs({ function() end, cyc, { 1, 2, x = 3 } }) do
  local ok, e = pcall(DataSave, 'bad', v) Out(e)
end
Out(DataSave('bad', { rate = 1/0 }), DataSave('big', string.rep('x', 300 * 1024)), DataSave('fine', 1))
"
            );
            rig.Start();
            var o = rig.Host.Area<SpyArea>().Out;
            Check(
                o.Count > 0 && o[0] == "120\tLeaf\ttrue\tdflt",
                "DataLoad returns copies of saved tables",
                string.Join("|", o)
            );
            Check(o.Count > 1 && o[1] == "nil", "DataSave(key, nil) removes");
            string all = string.Join("|", o);
            Check(
                all.Contains("not a function") && all.Contains("contains itself") && all.Contains("cannot mix"),
                "values of a kind that cannot be stored are errors with a reason",
                all
            );
            Check(o.Count == 6 && o[5] == "false\tfalse\ttrue", "inf and the size cap are refused, not errors", all);
            Check(
                rig.Env.ChatRead.Contains("finite, not inf") && rig.Env.ChatRead.Contains("over 256 KB"),
                "the player is told",
                rig.Env.ChatRead
            );
            rig.Host.Shutdown();
            rig.Host = null;
            var json = JObject.Parse(File.ReadAllText(Path.Combine(root, "mods_data", "d", "data.json")));
            Check((double)json["stats"]["earned"] == 120 && json["bad"] == null, "data.json written on unload");
        }
    }

    // SessionSet values outlive a reload, switching off and on, and the world going away; a new host (the game
    // started again) has none.
    static void SessionStore()
    {
        var rig = new Rig(new SpyArea());
        rig.Mod(
            "s",
            @"
local n = SessionGet('starts', 0) + 1
Out(n, SessionSet('starts', n), DataLoad('starts'), SessionGet('none', 'dflt'))
local t = SessionGet('t', { list = { 1, 2 } }) t.list[1] = 9
SessionSet('t', t) local copy = SessionGet('t') copy.list[2] = 99
Out(SessionGet('t').list[1], SessionGet('t').list[2])
Out(SessionSet('big', string.rep('x', 300 * 1024)))
"
        );
        rig.Start();
        rig.Host.Reload("s");
        rig.Host.SwitchOff("s", true);
        rig.Host.SwitchOff("s", false);
        var o = rig.Host.Area<SpyArea>().Out;
        Check(
            o.Count == 9
                && o[0] == "1\ttrue\tnil\tdflt"
                && o[3] == "2\ttrue\tnil\tdflt"
                && o[6] == "3\ttrue\tnil\tdflt",
            "kept across a reload and switching off and on; apart from saved data",
            string.Join("|", o)
        );
        Check(o.Count == 9 && o[1] == "9\t2", "SessionGet gives copies", string.Join("|", o));
        Check(o.Count == 9 && o[2] == "false" && rig.Env.ChatRead.Contains("over 256 KB"), "capped like saved data");

        var env = rig.Env;
        rig.Host.Shutdown();
        var host = new ModHost(env, CoreAreas.Create(new FakeCommands()).Concat(new Area[] { new SpyArea() }));
        host.Start(false);
        var o2 = host.Area<SpyArea>().Out;
        Check(o2.Count > 0 && o2[0].StartsWith("1\t"), "gone in a new run of the game", string.Join("|", o2));
        rig.Host = host;
        rig.Dispose();
    }

    static void Export()
    {
        var lim = new ExportLimiter();
        Check(lim.Offer("a.txt", "1", 0), "first write goes through");
        Check(!lim.Offer("a.txt", "2", 0.1) && !lim.Offer("a.txt", "3", 0.2), "writes inside the interval wait");
        Check(lim.Due(0.2).Count == 0, "nothing due before the interval");
        var due = lim.Due(0.3);
        Check(due.Count == 1 && due[0].Value == "3", "the newest waiting text is written when due");
        Check(lim.Offer("b.txt", "x", 0.3), "other files are independent");

        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "e",
            @"
ExportText('currency', 100)
Out(ExportText('currency', 200))
Out(ExportText('../evil', 'x'), ExportText('big', string.rep('x', 70 * 1024)))
for i = 1, 31 do ExportText('f' .. i, 'x') end
Out(ExportText('one_too_many', 'x'))
"
        );
        rig.Start();
        string file = Path.Combine(rig.Env.DataRoot, "e", "export", "currency.txt");
        Check(File.ReadAllText(file) == "100", "first export written at once");
        rig.Run(0.4);
        Check(File.ReadAllText(file) == "200", "waiting export written later");
        string all = string.Join("|", rig.Host.Area<SpyArea>().Out);
        Check(all == "true|false\tfalse|false", "export caps refuse with false", all);
        Check(
            rig.Env.ChatRead.Contains("over 64 KB") && rig.Env.ChatRead.Contains("already exports 32"),
            "and tell the player why",
            rig.Env.ChatRead
        );
        Check(!File.Exists(Path.Combine(rig.Env.DataRoot, "e", "export", "big.txt")), "a refused text is not written");
        Check(!File.Exists(Path.Combine(rig.Env.DataRoot, "evil.txt")), "no file outside the export folder");
    }

    // Writes a mod's calls cause are coalesced, and what waits is written when the mod stops. Before: the settings
    // file 20 times a second here (every call), data.json every 2 s, the exports 32 times and 1.9 MB a second.
    static void WriteRates()
    {
        using var rig = new Rig();
        rig.Mod(
            "w",
            "n = 0 local big = string.rep('x', 60000) "
                + "function OnFrame() n = n + 1 ModSettingSet('n', n) DataSave('n', n) "
                + "for i = 1, 8 do ExportText('f' .. i, big .. n) end end"
        );
        rig.Write(
            "w",
            "settings.lua",
            "mod_settings = { { id = 'n', value_default = 0, value_min = 0, value_max = 1e9 } }"
        );
        rig.Start();
        string data = Path.Combine(rig.Env.DataRoot, "w", "data.json");
        string Export(int i) => Path.Combine(rig.Env.DataRoot, "w", "export", "f" + i + ".txt");
        string Read(string f) => File.Exists(f) ? File.ReadAllText(f) : "";
        int dataWrites = 0,
            exportWrites = 0;
        long exportBytes = 0;
        string lastData = Read(data);
        var lastExport = Enumerable.Range(1, 8).Select(i => Read(Export(i))).ToArray();
        for (int f = 0; f < 200; f++)
        {
            rig.Run(0.05);
            string d = Read(data);
            if (d != lastData)
                dataWrites++;
            lastData = d;
            for (int i = 0; i < 8; i++)
            {
                string e = Read(Export(i + 1));
                if (e != lastExport[i])
                {
                    exportWrites++;
                    exportBytes += e.Length;
                }
                lastExport[i] = e;
            }
        }
        var settings = rig.Env.Settings["w"];
        Check(
            settings.Writes <= 10 / Limits.SettingsWriteSeconds + 1,
            "ModSettingSet every frame: the settings file at most every 5 s",
            settings.Writes + " writes in 10 s"
        );
        Check(
            dataWrites <= 10 / Limits.DataWriteSeconds + 1,
            "DataSave every frame: data.json at most every 10 s",
            dataWrites + " writes in 10 s"
        );
        Check(
            exportWrites <= 11 * Limits.ExportWritesPerSecond && exportBytes <= 11L * Limits.ExportBytesPerSecond,
            "ExportText of 8 files every frame: within the mod's allowance",
            exportWrites + " writes, " + exportBytes + " bytes in 10 s"
        );
        string last = rig["w"].Lua.Global("n").ToString();
        rig.Host.Shutdown();
        rig.Host = null;
        Check(
            !settings.Dirty && settings.Values["n"].ToString() == last,
            "the settings file is written when the mod stops"
        );
        Check(
            JObject.Parse(Read(data))["n"].ToString() == last
                && Enumerable.Range(1, 8).All(i => Read(Export(i)) == new string('x', 60000) + last),
            "data.json and every waiting export hold the last value after the mod stopped",
            last
        );
    }

    // A folder where the host writes its temp file makes the write throw UnauthorizedAccessException on Windows.
    static void Block(string file) => Directory.CreateDirectory(file + ".tmp");

    static int Notices(Rig rig, string id, string what) =>
        rig.Env.ChatRead.Split('\n').Count(l => l.Contains("[" + id + "]") && l.Contains(what));

    static void ExportWriteFailure()
    {
        using var rig = new Rig();
        rig.Mod("ex", "ok1 = ExportText('out', 'a') done = true function Again() ok2 = ExportText('out', 'b') end");
        Block(Path.Combine(rig.Env.DataRoot, "ex", "export", "out.txt"));
        rig.Start();
        var mod = rig["ex"];
        Check(
            mod.Status == ModStatus.Loaded && mod.Lua.Global("done").ToString() == "true",
            "an export that cannot be written does not stop the mod",
            mod.Status + ": " + mod.Reason
        );
        rig.Run(1);
        rig.Host.Call(mod, "Again", mod.Lua.Global("Again"));
        rig.Run(1);
        Check(mod.Status == ModStatus.Loaded, "nor does a later one", mod.Reason);
        Check(Notices(rig, "ex", "export out.txt failed") == 1, "one notice for the mod", rig.Env.ChatRead);
        Check(
            rig.Env.LogLines.Count(l => l.Contains("[ex] export out.txt failed")) >= 2,
            "a log line per failed write",
            string.Join("\n", rig.Env.LogLines)
        );
    }

    static void DataWriteFailure()
    {
        using var rig = new Rig();
        rig.Mod("a_bad", "DataSave('k', 1)");
        rig.Mod("b_good", "DataSave('k', 2)");
        Block(Path.Combine(rig.Env.DataRoot, "a_bad", "data.json"));
        rig.Start();
        rig.Run(3);
        Check(
            File.Exists(Path.Combine(rig.Env.DataRoot, "b_good", "data.json")),
            "a mod's failed save does not stop the next mod's"
        );
        rig.Run(12);
        Check(rig["a_bad"].Status == ModStatus.Loaded, "the mod keeps running", rig["a_bad"].Reason);
        Check(Notices(rig, "a_bad", "saving data.json failed") == 1, "one notice", rig.Env.ChatRead);
        int tries = rig.Env.LogLines.Count(l => l.Contains("[a_bad] saving data.json failed"));
        Check(tries == 2, "tried again after 10 s, not every 2 s", tries.ToString());
    }

    // A saved key is a name inside data.json, never a path: keys that look like paths are stored and read back as they
    // are, and no file but data.json is written.
    static void DataKeysAreNotPaths()
    {
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod(
            "d",
            @"
KEYS = { '../../evil', '..\\evil', '/etc/passwd', 'C:/Windows/win.ini', '\\\\server\\share\\x', 'con', 'nul.txt',
  'a\0b', 'line\nbreak', 'data.json', 'export/x.txt', '<b>', string.rep('k', 100) }
for i, k in ipairs(KEYS) do if not DataSave(k, i) then Out('refused', i) end end
function check() for i, k in ipairs(KEYS) do Out(i, DataLoad(k, 'missing')) end end
"
        );
        rig.Start();
        rig.Host.Reload("d");
        rig.Host.Call(rig["d"], "check", rig["d"].Lua.Global("check"));
        var loaded = spy.Out.Where(l => !l.StartsWith("refused")).ToList();
        Check(
            !spy.Out.Any(l => l.StartsWith("refused"))
                && loaded.Count == 13
                && loaded.Select((l, i) => l == (i + 1) + "\t" + (i + 1)).All(ok => ok),
            "every key is stored and read back after a reload",
            string.Join("|", spy.Out)
        );
        var written = Directory
            .GetFiles(rig.Env.Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(rig.Env.Root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("mods/"))
            .ToList();
        Check(
            written.SequenceEqual(new[] { "mods_data/d/data.json" }),
            "only data.json is written",
            string.Join("|", written)
        );
    }
}
