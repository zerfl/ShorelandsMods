using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;

// What the game plugin provides, in memory. Now is a fake clock the test moves.
sealed class TestEnv : IHostEnv
{
    public readonly string Root;
    public double Clock;
    public readonly List<string> ChatLines = new List<string>();
    public readonly List<string> LogLines = new List<string>();
    public readonly List<string> Order = new List<string>();
    public readonly HashSet<string> Off = new HashSet<string>();
    public readonly Dictionary<string, MemorySettings> Settings = new Dictionary<string, MemorySettings>();

    public TestEnv(string root)
    {
        Root = root;
        Directory.CreateDirectory(ModsDir);
    }

    public string ModsDir => Path.Combine(Root, "mods");
    public string DataRoot => Path.Combine(Root, "mods_data");
    public string HostVersion { get; set; } = "9.8.7";
    public double Now => Clock;

    public void Log(LogLevel level, string text) => LogLines.Add(level + " " + text);

    public void Chat(string richText) => ChatLines.Add(richText);

    public IReadOnlyList<string> SavedOrder => Order;

    public bool IsSwitchedOff(string id) => Off.Contains(id);

    public void SaveOrder(IReadOnlyList<string> ids)
    {
        Order.Clear();
        Order.AddRange(ids);
    }

    public void SetSwitchedOff(string id, bool switchedOff)
    {
        if (switchedOff)
            Off.Add(id);
        else
            Off.Remove(id);
    }

    public ISettingsStore OpenSettings(string modId, IReadOnlyList<SettingDef> defs)
    {
        if (!Settings.TryGetValue(modId, out var s))
            Settings[modId] = s = new MemorySettings();
        return s;
    }

    public bool IsKeyName(string name) => name.Length >= 1 && name.All(char.IsLetterOrDigit) && char.IsUpper(name[0]);

    public string ChatText => string.Join("\n", ChatLines);

    // Chat as read, with the escaped quotes back.
    public string ChatRead => ChatText.Replace('\uFF02', '"');
}

sealed class MemorySettings : ISettingsStore
{
    public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    public readonly List<string> ExternalChanges = new List<string>();

    public object Get(SettingDef def) => Values.TryGetValue(def.Id, out var v) ? v : def.Default;

    public void Set(SettingDef def, object value)
    {
        Values[def.Id] = value;
        Dirty = true;
    }

    public bool Dirty { get; private set; }

    // Times the settings file was written.
    public int Writes;

    // Thrown by Flush when set (a settings file that cannot be written).
    public Exception FailFlush;

    public void Flush()
    {
        if (FailFlush != null)
            throw FailFlush;
        Dirty = false;
        Writes++;
    }

    // Simulates the player editing the cfg file.
    public void Edit(string id, object value)
    {
        Values[id] = value;
        ExternalChanges.Add(id);
    }

    public IList<string> ReloadIfChanged()
    {
        var r = ExternalChanges.ToList();
        ExternalChanges.Clear();
        return r;
    }
}

sealed class FakeCommands : ICommandSink
{
    public readonly Dictionary<string, Action<string>> Added = new Dictionary<string, Action<string>>();
    public readonly HashSet<string> Game = new HashSet<string> { "ignore", "unignore", "w" };
    public readonly HashSet<string> Server = new HashSet<string> { "channels", "trade" };

    public bool Available => true;

    public string Owner(string name) =>
        Game.Contains(name) ? "game"
        : Server.Contains(name) ? "server"
        : null;

    public void Add(string name, Action<string> run) => Added[name] = run;

    public void Remove(string name) => Added.Remove(name);

    public event Action ServerCommandsChanged;
    public event Action<string> Unknown;

    public void ServerTree(params string[] names)
    {
        Server.Clear();
        Server.UnionWith(names);
        ServerCommandsChanged?.Invoke();
    }

    // A line as the game's chat hands it over: the text after the slash.
    public void Type(string line)
    {
        int sp = line.IndexOf(' ');
        string name = sp < 0 ? line : line.Substring(0, sp);
        if (Added.TryGetValue(name, out var run))
            run(sp < 0 ? "" : line.Substring(sp + 1));
        else if (!Game.Contains(name) && !Server.Contains(name))
            Unknown?.Invoke(name);
    }
}

// A host over a temporary mods folder.
sealed class Rig : IDisposable
{
    public readonly TestEnv Env;
    public readonly FakeCommands Commands = new FakeCommands();
    public ModHost Host;
    readonly List<Area> extra;

    public Rig(params Area[] extraAreas)
    {
        Env = new TestEnv(Program.TempDir("rig"));
        extra = extraAreas.ToList();
    }

    public void WriteBytes(string mod, string file, byte[] bytes)
    {
        string path = Path.Combine(Env.ModsDir, mod, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, bytes);
    }

    public void Write(string mod, string file, string text)
    {
        string path = Path.Combine(Env.ModsDir, mod, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    // A mod with mod.xml and an init.lua.
    public void Mod(string id, string init, string xml = null)
    {
        Write(id, "mod.xml", xml ?? "<Mod name=\"" + id + "\" version=\"1.0\" />");
        if (init != null)
            Write(id, "init.lua", init);
    }

    public readonly FakeUi Ui = new FakeUi();
    public HudArea Hud;
    public WindowArea Windows;
    public NoticeArea Notices;
    public ChatDecorArea ChatDecor;
    public TooltipArea Tooltips;

    // beforeStart: what the plugin does between making the host and starting it (BlockLua).
    public ModHost Start(Action<ModHost> beforeStart = null)
    {
        var areas = CoreAreas.Create(Commands);
        areas.Add(Hud = new HudArea(Ui));
        areas.Add(Windows = new WindowArea(Ui));
        areas.Add(Notices = new NoticeArea(Ui));
        areas.Add(ChatDecor = new ChatDecorArea());
        areas.Add(Tooltips = new TooltipArea());
        areas.Add(new KeyArea(Ui));
        areas.AddRange(extra);
        Host = new ModHost(Env, areas);
        beforeStart?.Invoke(Host);
        Host.Start(false);
        return Host;
    }

    // Advances the fake clock and runs that many frames.
    public void Run(double seconds, double frame = 0.05)
    {
        for (double t = 0; t < seconds - 1e-9; t += frame)
        {
            Env.Clock += frame;
            Host.Update(frame);
            Hud.Flush(Env.Clock);
            Windows.Flush();
            Notices.Flush(Env.Clock);
            Ui.Down.Clear();
        }
    }

    public Mod this[string id] => Host.Find(id);

    // Gives the running mods Spend(ms): Lua time on the ManualClock. A plain Lua function, not a host function, so the
    // watchdog charges it like a library call (no host credit). Set again after a reload.
    public void GiveSpend()
    {
        foreach (var mod in Host.Mods)
            if (mod.Lua != null)
                mod.Lua.Globals["Spend"] = new Lua.LuaFunction("Spend", (c, ct) =>
                {
                    ManualClock.Current.Advance(c.GetArgument(0).Read<double>());
                    Watchdog.Poll();
                    return new System.Threading.Tasks.ValueTask<int>(c.Return());
                });
    }

    public void Dispose()
    {
        try
        {
            Host?.Shutdown();
            Directory.Delete(Env.Root, true);
        }
        catch (IOException) { }
    }
}

// The game side of art/ in memory: records what the area applies.
sealed class FakeArt : IArtStage
{
    public readonly Dictionary<string, ArtImage> Now = new Dictionary<string, ArtImage>();
    public readonly List<string> Sets = new List<string>();

    // item/koi id -> icon size; null: the tables are not loaded yet.
    public Dictionary<string, (int W, int H)> Icons;

    public void Set(string key, ArtImage image)
    {
        Sets.Add(key + "=" + (image?.ModId ?? "-"));
        if (image == null)
            Now.Remove(key);
        else
            Now[key] = image;
    }

    public bool IconSize(AssetKind kind, int id, out int w, out int h, out string problem)
    {
        w = h = 0;
        problem = null;
        if (Icons == null)
            return false;
        if (!Icons.TryGetValue((kind == AssetKind.Item ? "item/" : "koi/") + id, out var s))
        {
            problem = "there is no such id";
            return false;
        }
        (w, h) = s;
        return true;
    }

    public AssetLine Describe(string key, ArtImage image) => new AssetLine(image.Rel, AssetState.Applied);
}

// The game's audio in memory: clips load when the test says so; plays and loops are recorded.
sealed class FakeAudio : IAudioOut
{
    public readonly Dictionary<int, (string Path, ClipStatus Status)> Clips =
        new Dictionary<int, (string, ClipStatus)>();
    public readonly List<string> Plays = new List<string>();
    public readonly Dictionary<int, string> Loops = new Dictionary<int, string>();
    public readonly List<string> Unloaded = new List<string>();
    int next = 1,
        nextLoop = 1;

    public int Load(string fullPath)
    {
        Clips[next] = (fullPath, ClipStatus.Loading);
        return next++;
    }

    public void ReadyAll()
    {
        foreach (var k in Clips.Keys.ToList())
            if (Clips[k].Status == ClipStatus.Loading)
                Clips[k] = (Clips[k].Path, ClipStatus.Ready);
    }

    public void Fail(string fileName)
    {
        foreach (var k in Clips.Keys.ToList())
            if (Path.GetFileName(Clips[k].Path) == fileName)
                Clips[k] = (Clips[k].Path, ClipStatus.Failed);
    }

    public ClipStatus Status(int clip, out string problem)
    {
        problem = "broken";
        return Clips.TryGetValue(clip, out var c) ? c.Status : ClipStatus.Failed;
    }

    public void Unload(int clip)
    {
        if (Clips.TryGetValue(clip, out var c))
            Unloaded.Add(Path.GetFileName(c.Path));
        Clips.Remove(clip);
    }

    string Name(int clip) => Clips.TryGetValue(clip, out var c) ? Path.GetFileName(c.Path) : "?";

    public void Play(int clip, double volume, string group, int voices)
    {
        Sounding.Add(group);
        Plays.Add(
            Name(clip) + "@" + volume.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + group
        );
    }

    public int StartLoop(int clip, double volume, double fadeSeconds)
    {
        Loops[nextLoop] = Name(clip);
        return nextLoop++;
    }

    public void StopLoop(int loop, double fadeSeconds) => Loops.Remove(loop);

    public readonly HashSet<string> Sounding = new HashSet<string>();

    public void Stop(string group) => Sounding.Remove(group);

    public readonly List<string> Dropped = new List<string>();

    public void Drop(string group)
    {
        Sounding.Remove(group);
        Dropped.Add(group);
    }

    public bool IsPlaying(string group) => Sounding.Contains(group);

    public double MasterVolume { get; set; } = 1;

    public string Playing => string.Join(",", Loops.Values.OrderBy(v => v));
}

sealed class FakeCues : ICueStage
{
    public readonly Dictionary<string, CueDef> Now = new Dictionary<string, CueDef>();
    public readonly List<CueDef> Previews = new List<CueDef>();

    public readonly List<int> Cycles = new List<int>();

    public bool Preview(CueDef cue, int cycles)
    {
        Cycles.Add(cycles);
        Previews.Add(cue);
        return true;
    }

    public void Set(string slot, CueDef cue)
    {
        if (cue == null)
            Now.Remove(slot);
        else
            Now[slot] = cue;
    }
}

// Asset files for test mods.
static class Files
{
    public static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[]
        {
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
            0,
            0,
            0,
            13,
            (byte)'I',
            (byte)'H',
            (byte)'D',
            (byte)'R',
        }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24);
        b[17] = (byte)(w >> 16);
        b[18] = (byte)(w >> 8);
        b[19] = (byte)w;
        b[20] = (byte)(h >> 24);
        b[21] = (byte)(h >> 16);
        b[22] = (byte)(h >> 8);
        b[23] = (byte)h;
        b[24] = 8;
        b[25] = 6;
        return b;
    }

    // 16-bit PCM mono silence of the given length.
    public static byte[] Wav(double seconds, int rate = 22050)
    {
        int n = (int)(seconds * rate) * 2;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + n);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(n);
        w.Write(new byte[n]);
        return ms.ToArray();
    }

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    // This repository's root, found from the test's folder; null outside it.
    public static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "src", "ShorelandsMods.csproj")))
            dir = Path.GetDirectoryName(dir);
        return dir;
    }
}

// The game's UI in memory: text is 5 units per character, a line 10 units; records what is drawn.
sealed class FakeUi : IHudStage, IWindowStage, INoticeStage, IKeyInput
{
    public bool IsReady = true;
    public int W = 640,
        H = 360;
    public List<URect> Hud = new List<URect>();
    public readonly Dictionary<string, HudWidget> Shown = new Dictionary<string, HudWidget>();
    public readonly Dictionary<string, ModWindow> Built = new Dictionary<string, ModWindow>();
    public readonly Dictionary<string, bool> Open = new Dictionary<string, bool>();
    public int Builds,
        Refreshes;
    public List<(Notice notice, List<string> lines)> Notices = new List<(Notice, List<string>)>();
    public readonly HashSet<string> Down = new HashSet<string>();
    public bool KeysBlocked;

    public bool Ready => IsReady;
    public int CanvasW => W;
    public int CanvasH => H;

    public List<URect> GameHud() => Hud.ToList();

    public int MeasureText(string line) => 5 * (line ?? "").Length;

    public void Show(HudWidget w)
    {
        Shown[w.Key] = w;
        switch (w.Kind)
        {
            case HudKind.Text:
                w.W = w.Lines.Count == 0 ? 0 : w.Lines.Max(MeasureText) * w.Scale + (w.Plate ? 8 : 0);
                w.H = w.Lines.Count * 10 * w.Scale + (w.Plate ? 6 : 0);
                break;
            case HudKind.Icon:
                w.W = w.H = 24 * w.Scale;
                break;
            default:
                w.W = w.BarW;
                w.H = w.BarH;
                break;
        }
    }

    public void Place(HudWidget w) { }

    public void Remove(HudWidget w) => Shown.Remove(w.Key);

    public int LineH => 10;
    public int RowH => 14;
    public (int w, int h) CloseSize => (11, 11);

    public void Build(ModWindow w)
    {
        Builds++;
        Built[w.Key] = w;
    }

    public void Refresh(ModWindow w) => Refreshes++;

    public void SetOpen(ModWindow w, bool open) => Open[w.Key] = open;

    public void Place(ModWindow w) { }

    public void Destroy(ModWindow w)
    {
        Built.Remove(w.Key);
        Open.Remove(w.Key);
    }

    public void Show(IReadOnlyList<(Notice notice, List<string> lines)> shown) => Notices = shown.ToList();

    public void Fade(Notice n, double alpha) { }

    public bool Blocked => KeysBlocked;

    public bool Pressed(string keyName) => Down.Contains(keyName);

    public readonly List<(string key, string what)> TakenKeys = new List<(string key, string what)>();

    public IReadOnlyList<(string key, string what)> Taken() => TakenKeys;
}

// The author tools' game side: records what was asked.
sealed class FakeTools : IAuthorTools
{
    public readonly List<string> Asked = new List<string>();
    public bool InWorld { get; set; } = true;
    public bool Inspecting { get; private set; }

    public void SetInspect(bool on) => Inspecting = on;

    ExportResult Did(string what, string file)
    {
        Asked.Add(what);
        var r = new ExportResult();
        r.Written.Add(file);
        return r;
    }

    public ExportResult ExportTexture(string name) => Did("texture " + name, "x/" + name + ".png");

    public ExportResult ExportItem(int id) => Did("item " + id, "x/" + id + ".png");

    public ExportResult ExportKoi(int id) => Did("koi " + id, "x/" + id + ".png");

    public ExportResult ExportAll() => Did("all", "x/all.png");

    // Items 1 to 999 exist, named "Item <id>".
    public Lua.LuaTable ItemTable(int id, int count, int meta)
    {
        if (id < 1 || (id > 999 && id != 9999))
            return null;
        var t = new Lua.LuaTable(0, 5);
        t["id"] = id;
        t["name"] = id == 9999 ? "Koi " + meta : "Item " + id;
        t["count"] = count;
        t["meta"] = meta;
        t["category"] = "farming";
        return t;
    }

    public double[] WalletNow = { 5000, 3 };

    public double[] Wallet() => WalletNow;
}

// Test-only API for mods: Out(...) records values, Depth() reads the guard flag, Busy(ms) is host work.
sealed class SpyArea : Area
{
    public readonly List<string> Out = new List<string>();
    public override string Name => "Spy";

    protected internal override void Register(AreaSetup s)
    {
        s.Function("Out( ... )", "test output", c =>
        {
            var parts = new Lua.LuaValue[c.Count];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = c.Arg(i + 1);
            Out.Add(LuaText.Join(parts));
            return c.Return();
        });
        s.Function("Depth() -> n", "guard depth", c => c.Return(LuaGuard.Depth));
        // Host work on the ManualClock: the time passes at once and the watchdog looks right away.
        s.Function("Busy( ms )", "host work of that many ms", c =>
        {
            ManualClock.Current.Advance(c.Number(1, "ms"));
            Watchdog.Poll();
            return c.Return();
        });
        // A Command as Mirror would send it, through the send guard's rule and refusal.
        s.Function("SendCommand( name ) -> sent", "simulated send", c =>
        {
            string refused = SendRules.Command(c.String(1, "name"));
            if (refused != null)
                LuaGuard.Refused(refused);
            return c.Return(refused == null);
        });
        s.Function("Who() -> text", "the running mod and the guard depth", c =>
            c.Return((LuaGuard.Current?.ModId ?? "none") + " " + LuaGuard.Depth)
        );
        // A host function that calls into another mod's Lua.
        s.Function("CallMod( id, fn )", "calls another mod", c =>
        {
            var mod = Host.Find(c.String(1, "id"));
            string fn = c.String(2, "fn");
            Host.Call(mod, fn, mod.Lua.Global(fn));
            return c.Return();
        });
    }
}

// The budgets' clock (HostClock) moved by the test: time passes only by Advance, a collection only by Collect.
sealed class ManualClock : IDisposable
{
    public static ManualClock Current;
    readonly Func<long> realTicks = HostClock.Ticks;
    readonly Func<int> realCollections = HostClock.Collections;
    long ticks = 1;
    int collections;

    public ManualClock()
    {
        HostClock.Ticks = () => Interlocked.Read(ref ticks);
        HostClock.Collections = () => Volatile.Read(ref collections);
        Current = this;
    }

    public void Advance(double ms) => Interlocked.Add(ref ticks, HostClock.FromMs(ms));

    public void Collect() => Interlocked.Increment(ref collections);

    public void Dispose()
    {
        HostClock.Ticks = realTicks;
        HostClock.Collections = realCollections;
        Current = null;
    }
}
