using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Threading;
using Lua;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;

// Time and memory budgets: runaway calls, host time, the per-mod budget over time, fair timers, the
// Lua data a mod keeps, the per-mod caps, and the shipped examples far below every limit.
static partial class Program
{
    // Real time: the watchdog thread, the step hook and the memory check stop every shape of runaway.
    static void Runaway()
    {
        var shapes = new (string name, string body)[]
        {
            ("plain", "while true do end"),
            ("pcall", "while true do pcall(function() while true do end end) end"),
            ("xpcall", "xpcall(function() while true do end end, function(e) return e end) Out('swallowed')"),
            ("xpcall loop", "while true do xpcall(function() while true do end end, function(e) return e end) end"),
            (
                "sort",
                "local t = {} for i = 1, 50 do t[i] = 51 - i end table.sort(t, function(a, b) while true do end end)"
            ),
            ("gsub", "string.gsub('abc', '%a', function(c) while true do end end)"),
            (
                "metamethod",
                "local t = setmetatable({}, { __index = function(t, k) while true do end end }) local x = t.y"
            ),
            ("library", "for i = 1, 1e9 do local s = string.rep('x', 10000) end"),
            ("recursion", "local function f() return f() + 1 end f()"),
            ("memory", "local t = {} for i = 1, 1e9 do t[i] = string.rep('x', 1000000) end"),
        };
        foreach (var (name, body) in shapes)
        {
            using var rig = new Rig(new SpyArea());
            rig.Mod("loop", "function OnTick() " + body + " end");
            rig.Mod("fine", "n = 0 function OnTick() n = n + 1 if n == 3 then Out('fine ticks') end end");
            rig.Start();
            var sw = Stopwatch.StartNew();
            rig.Run(0.35);
            var loop = rig["loop"];
            Check(
                loop.Status == ModStatus.Failed
                    && (
                        loop.Reason.Contains("ran")
                        || loop.Reason.Contains("memory")
                        || loop.Reason.Contains("stack overflow")
                    ),
                "runaway " + name + " stopped",
                loop.Reason
            );
            Check(
                sw.ElapsedMilliseconds < 3000,
                "runaway " + name + " stopped within seconds",
                sw.ElapsedMilliseconds + " ms"
            );
            Check(rig.Host.Area<SpyArea>().Out.Contains("fine ticks"), "other mod keeps running after runaway " + name);
            Check(!rig.Host.Area<SpyArea>().Out.Contains("swallowed"), "the stop is not swallowed (" + name + ")");
            Check(rig.Env.ChatLines.Count == 1, "one chat line for runaway " + name, rig.Env.ChatText);
        }

        // init.lua gets the longer budget, and is stopped too.
        using var rig2 = new Rig();
        rig2.Mod("slowinit", "local x = 0 for i = 1, 1e6 do x = x + i end");
        rig2.Mod("loopinit", "while true do end");
        var sw2 = Stopwatch.StartNew();
        rig2.Start();
        Check(
            rig2["slowinit"].Status == ModStatus.Loaded,
            "a million-iteration loop fits the init budget",
            rig2["slowinit"].Reason
        );
        Check(
            rig2["loopinit"].Status == ModStatus.Failed && sw2.ElapsedMilliseconds < 5000,
            "endless init stopped",
            sw2.ElapsedMilliseconds + " ms"
        );
    }

    // The per-call budget (Watchdog) on a clock the test moves: 20 ms; time in host functions not charged up to 60 ms;
    // a call that saw a collection gets its budget again, twice.
    static void CallBudget()
    {
        using var clock = new ManualClock();
        CancellationTokenSource Armed()
        {
            var cts = new CancellationTokenSource();
            Watchdog.Arm(cts, Limits.Callback.Ms);
            return cts;
        }
        bool After(double ms, CancellationTokenSource cts)
        {
            clock.Advance(ms);
            Watchdog.Poll();
            return cts.IsCancellationRequested;
        }

        var call = Armed();
        Check(!After(20, call), "20 ms of a 20 ms budget runs on");
        Check(After(0.01, call), "past 20 ms the call is stopped");
        Watchdog.Disarm();

        call = Armed();
        Watchdog.HostEnter();
        Check(!After(80, call), "inside a host function: the budget plus 60 ms of credit");
        Check(After(0.01, call), "past the credit inside a host function it is stopped");
        Watchdog.Disarm();

        call = Armed();
        long t0 = HostClock.Now;
        Watchdog.HostEnter();
        clock.Advance(50);
        Watchdog.HostLeave(HostClock.Now - t0);
        Check(!After(15, call), "host time is not charged");
        double charged = Watchdog.Disarm();
        Check(Math.Abs(charged - 15) < 1e-6, "the call is charged its own time only", charged + " ms");

        call = Armed();
        t0 = HostClock.Now;
        Watchdog.HostEnter();
        clock.Advance(70);
        Watchdog.HostLeave(HostClock.Now - t0);
        Check(!After(10, call) && After(0.01, call), "host time past 60 ms is charged");
        Watchdog.Disarm();

        call = Armed();
        clock.Advance(25);
        clock.Collect();
        Watchdog.Poll();
        Check(!call.IsCancellationRequested, "a collection in the call: its budget again");
        clock.Collect();
        Check(!After(25, call), "a second collection: again");
        clock.Collect();
        Check(After(25, call), "a third does not");
        Watchdog.Disarm();
    }

    // The guarded call path stays cheap: no garbage per call (20 mods with OnFrame).
    static void Cost()
    {
        using var rig = new Rig();
        for (int i = 0; i < 20; i++)
            rig.Mod(
                "perf" + i,
                "local n = 0 function OnFrame(dt) for i = 1, 10 do n = n + dt * i end local x = TimeGetSeconds() end"
            );
        rig.Start();
        rig.Run(1, 0.01);
        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 500;
        for (int f = 0; f < frames; f++)
            rig.Host.Fire("OnFrame", 0.016);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
        double msPerFrame = sw.Elapsed.TotalMilliseconds / frames;
        Note(
            "cost: 20 mods OnFrame " + msPerFrame.ToString("0.000") + " ms/frame, " + bytes / (frames * 20) + " B/call"
        );
        Check(rig.Host.Mods.All(m => m.Status == ModStatus.Loaded), "perf mods stay loaded");
        Check(bytes / (frames * 20) < 1024, "under 1 KB garbage per guarded call", bytes / (frames * 20) + " B");
    }

    // Host work inside a callback is not the script's time; a loop of host calls still stops.
    static void HostTime()
    {
        using var clock = new ManualClock();
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod("h", "function OnTick() Busy(15) Busy(15) Busy(15) Out('done') end");
        rig.Mod("loop", "function OnTick() while true do Busy(4) end end");
        rig.Start();
        rig.Run(0.1);
        Check(
            rig["h"].Status == ModStatus.Loaded && spy.Out.Contains("done") && rig["h"].Lua.LastMs == 0,
            "45 ms of host work in a 20 ms callback is fine, and not charged to it",
            rig["h"].Reason + " " + rig["h"].Lua.LastMs + " ms"
        );
        Check(
            rig["loop"].Status == ModStatus.Failed && rig["loop"].Reason.Contains("longer than 20 ms"),
            "a loop of host calls still stops",
            rig["loop"].Reason
        );
        rig.Mod("setup", "function OnBig() Spend(50) Out('built') end");
        rig.Host.Reload("setup");
        rig.GiveSpend();
        rig.Host.FireSetup("OnBig");
        Check(
            rig["setup"].Status == ModStatus.Loaded && spy.Out.Contains("built"),
            "a setup callback has init.lua's budget",
            rig["setup"].Reason
        );
        rig.Host.Fire("OnBig");
        Check(rig["setup"].Status == ModStatus.Failed, "the same work in an ordinary callback is stopped");
    }

    static void BudgetNotice()
    {
        using var clock = new ManualClock();
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        // 2 steps a round: 70,000 rounds are 140,000 of the 200,000 steps (about 2 ms of Lua).
        rig.Mod(
            "heavy",
            @"
local function spin(n) local x = 0 for i = 1, n do x = x + i end Spend(2) return x end
function OnTick() spin(70000) end
function OnLight() spin(100) end
function OnHostWork() Busy(15) end
function OnSlow() Spend(12) end
TimerAfter(0.05, function() spin(60000) end)
CommandRegister('heavy', 'h')
function OnCommand_heavy() spin(55000) end
"
        );
        rig.Mod("light", "function OnTick() end");
        rig.Start();
        rig.GiveSpend();
        rig.Run(0.5);
        var notes = rig.Env.ChatLines.Where(l => l.Contains("[heavy]") && l.Contains(" used ")).ToList();
        Check(
            notes.Count(l => l.Contains("OnTick used 14")) == 1 && notes.Any(l => l.Contains("of 200,000 steps")),
            "over half the steps: one notice per callback, however often it runs",
            string.Join("|", notes)
        );
        Check(
            notes.Any(l => l.Contains("the TimerAfter function (init.lua:7) used 12")),
            "a timer's function is named too"
        );
        rig.Commands.Added["heavy"]("");
        rig.Host.Fire("OnLight");
        rig.Host.Fire("OnHostWork");
        notes = rig.Env.ChatLines.Where(l => l.Contains("[heavy]") && l.Contains(" used ")).ToList();
        Check(notes.Any(l => l.Contains("OnCommand_heavy used 11")), "and a command", string.Join("|", notes));
        Check(
            !notes.Any(l => l.Contains("OnLight") || l.Contains("OnHostWork")),
            "a light callback and host work (15 ms of it) give none",
            string.Join("|", notes)
        );
        string line = CallCosts.Line(rig["heavy"]);
        Check(
            line.StartsWith("Slowest calls: OnTick 14")
                && line.IndexOf("the TimerAfter") < line.IndexOf("OnCommand_heavy")
                && !line.Contains("OnLight"),
            "the slowest calls, largest share first, three of them",
            line
        );
        Check(CommandsArea.ModReport(rig.Host, rig["heavy"]).Contains("  " + line), "/mods <id> shows the line");
        rig.Host.Fire("OnSlow");
        string slow = rig.Env.ChatLines.FirstOrDefault(l => l.Contains("OnSlow used"));
        Check(slow != null && slow.Contains("OnSlow used 12 of 20 ms"), "time is counted in ms of the budget", slow);
        Check(
            CallCosts.Line(rig["light"]) == "Slowest calls: all under 0.1 ms.",
            "a cheap mod says so",
            CallCosts.Line(rig["light"])
        );
        rig.Mod("art", null);
        rig.Host.Reload("art");
        Check(CallCosts.Line(rig["art"]) == null, "a mod without Lua has no line");
    }

    // A mod's time over time on a clock the test moves (host work here; Lua time counts the same): over 1,500 ms in
    // 5 s switches it off, over half of it gives one notice, and 290 ms a second goes on for ever.
    static void WindowBudget()
    {
        using var clock = new ManualClock();
        using var rig = new Rig(new SpyArea());
        foreach (var (id, ms) in new[] { ("quiet", 14), ("noted", 16), ("steady", 29), ("over", 31) })
            rig.Mod(id, "function OnTick() Busy(" + ms + ") end");
        rig.Start();
        double stoppedAt = -1;
        while (rig.Env.Clock < 20)
        {
            rig.Run(0.05);
            if (stoppedAt < 0 && rig["over"].Status != ModStatus.Loaded)
                stoppedAt = rig.Env.Clock;
        }
        string chat = rig.Env.ChatRead;
        Check(
            rig["over"].Status == ModStatus.Failed
                && rig["over"].Reason.StartsWith("it used more than 1,500 ms in 5 seconds, most of it in OnTick")
                && stoppedAt > 4.5
                && stoppedAt < 5,
            "310 ms a second: switched off within 5 s, when it passes 1,500 ms",
            stoppedAt.ToString("0.00") + " s: " + rig["over"].Reason
        );
        Check(
            rig["steady"].Status == ModStatus.Loaded && rig["steady"].Load.PeakMs is > 1400 and <= 1500,
            "290 ms a second for 20 s stays on",
            rig["steady"].Load.PeakMs + " ms"
        );
        Check(
            Count(chat, "[noted] used ") == 1
                && chat.Contains("of 1,500 ms in 5 seconds, most of it in OnTick; a mod over that"),
            "over 750 ms: one notice",
            chat
        );
        Check(
            !chat.Contains("[quiet]") && rig["quiet"].Load.PeakMs is > 600 and <= 750,
            "714 ms in 5 s: no notice",
            rig["quiet"].Load.PeakMs + " ms"
        );
    }

    // Frames as the game has them: the clock moves by the frame's length (at least 1/60 s), so a slow frame takes game
    // time too. Returns each frame's ms on HostClock; stops after realCapMs in all.
    static List<double> GameFrames(Rig rig, double seconds, Func<bool> until = null, double realCapMs = 30000)
    {
        var frames = new List<double>();
        double end = rig.Env.Clock + seconds,
            last = 0,
            total = 0;
        while (rig.Env.Clock < end && total < realCapMs && (until == null || !until()))
        {
            double dt = Math.Max(1.0 / 60, last / 1000);
            rig.Env.Clock += dt;
            long t0 = HostClock.Now;
            rig.Host.Update(dt);
            rig.Hud.Flush(rig.Env.Clock);
            rig.Windows.Flush();
            rig.Notices.Flush(rig.Env.Clock);
            last = HostClock.MsSince(t0);
            frames.Add(last);
            total += last;
        }
        return frames;
    }

    static string Frames(List<double> f) =>
        f.Count + " frames, max " + f.Max().ToString("0.0") + " ms, total " + f.Sum().ToString("0") + " ms";

    // 200 TimerEvery(0.05) timers of 150,000 steps (about 2 ms) each: a frame runs its timers until 5 ms is used, and
    // the mod does not stay loaded.
    static void TimerFlood()
    {
        using var clock = new ManualClock();
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "flood",
            "local function spin() local x = 0 for i = 1, 75000 do x = x + i end Spend(2) end\n"
                + "made = 0 for i = 1, 200 do if TimerEvery(0.05, spin) then made = made + 1 end end"
        );
        rig.Mod("light", "n = 0 TimerEvery(0.05, function() n = n + 1 end)");
        rig.Start();
        rig.GiveSpend();
        double t0 = rig.Env.Clock;
        var frames = GameFrames(rig, 8, () => rig["flood"].Status != ModStatus.Loaded);
        double took = rig.Env.Clock - t0;
        var flood = rig["flood"];
        Note("timer flood: " + Frames(frames) + ", " + took.ToString("0.0") + " s, " + flood.Reason);
        Check(
            flood.Status == ModStatus.Failed
                && flood.Reason.Contains("in 5 seconds")
                && flood.Reason.Contains("timers"),
            "a flood of timers is switched off, naming its timers",
            flood.Reason
        );
        Check(took < 5, "within 5 s", took.ToString("0.00") + " s");
        Check(
            Math.Abs(frames.Max() - 6) < 1e-6,
            "a frame of the flood runs timers until 5 ms is used: 3 of them",
            Frames(frames)
        );
        var lines = rig.Env.ChatLines.Where(l => l.Contains("[flood]") && l.Contains(" in 5 seconds")).ToList();
        Check(
            lines.Count == 2 && lines[0].Contains(" of 1,500 ms in 5 seconds") && lines[1].Contains("is off"),
            "one warning, then the stop",
            string.Join(" | ", lines)
        );
        double n = rig["light"].Lua.Global("n").Read<double>();
        Check(n >= took / 0.05 * 0.8, "another mod's timer keeps its pace meanwhile", n + " runs in " + took + " s");
    }

    // 256 timers each using its 20 ms plus 60 ms of host-time credit is seconds per frame.
    static void TimersAtCallLimit()
    {
        using var clock = new ManualClock();
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "heavy",
            "local function heavy() Spend(12) Busy(50) end\n"
                + "made = 0 for i = 1, 256 do if TimerEvery(0.05, heavy) then made = made + 1 end end"
        );
        rig.Start();
        rig.GiveSpend();
        double made = rig["heavy"].Lua.Global("made").Read<double>();
        double t0 = rig.Env.Clock;
        var frames = GameFrames(rig, 6, () => rig["heavy"].Status != ModStatus.Loaded);
        double took = rig.Env.Clock - t0;
        var heavy = rig["heavy"];
        Note("timers at the call limit: " + Frames(frames) + ", " + took.ToString("0.0") + " s");
        Check(
            made == 64 && rig.Env.ChatRead.Contains("already has 64 timers"),
            "the 65th timer is refused, and the notice says so"
        );
        Check(Math.Abs(frames.Max() - 62) < 1e-6, "one frame runs one such timer, not all", Frames(frames));
        Check(
            heavy.Status == ModStatus.Failed && heavy.Reason.Contains("timers") && took < 2,
            "switched off within 2 s",
            took.ToString("0.00") + " s: " + heavy.Reason
        );
    }

    // 200 retained 2 MB strings must not keep a mod loaded. Once in a global, once only in the upvalue of a timer's
    // function, which only the host holds.
    static void RetainedStrings()
    {
        foreach (
            var (name, init, where) in new[]
            {
                (
                    "global",
                    "keep = {}\nTimerEvery(0.05, function() if #keep < 200 then keep[#keep + 1] = string.rep('x', 2097152) Out('kept') end end)",
                    "the global \"keep\""
                ),
                (
                    "upvalue",
                    "local keep = {}\nTimerEvery(0.05, function() if #keep < 200 then keep[#keep + 1] = string.rep('y', 2097152) Out('kept') end end)",
                    "functions the host holds"
                ),
            }
        )
        {
            var spy = new SpyArea();
            using var rig = new Rig(spy);
            rig.Mod("hoard", init);
            rig.Start();
            GameFrames(rig, 12, () => rig["hoard"].Status != ModStatus.Loaded);
            var hoard = rig["hoard"];
            int kept = spy.Out.Count;
            Note("retained strings (" + name + "): " + kept + " kept, " + hoard.Reason);
            Check(
                hoard.Status == ModStatus.Failed
                    && hoard.Reason.Contains("MB of Lua data")
                    && hoard.Reason.Contains(where),
                "kept strings switch the mod off (" + name + ")",
                hoard.Reason
            );
            Check(kept < 20, "before 20 of them (" + name + ")", kept + " kept");
            var noticed = Regex.Match(rig.Env.ChatRead, @"\[hoard\] keeps about ([\d.]+) of 32 MB of Lua data");
            Check(
                noticed.Success
                    && double.Parse(noticed.Groups[1].Value, CultureInfo.InvariantCulture) is >= 16 and < 32,
                "a notice past half of it first (" + name + ")",
                rig.Env.ChatRead
            );
        }
    }

    // Data hidden in the string metatable counts too; data replaced every call does not add up.
    static void RetainedElsewhere()
    {
        using var rig = new Rig();
        rig.Mod(
            "meta",
            "getmetatable('').k = {}\nTimerEvery(0.05, function() table.insert(getmetatable('').k, string.rep('q', 2097152)) end)"
        );
        rig.Mod("churn", "TimerEvery(0.05, function() big = string.rep('z', 3145728) end)");
        rig.Start();
        GameFrames(rig, 4);
        Check(
            rig["meta"].Status == ModStatus.Failed && rig["meta"].Reason.Contains(LuaData.OtherRoot),
            "data kept in the string metatable is seen",
            rig["meta"].Reason
        );
        var churn = rig["churn"];
        Check(
            churn.Status == ModStatus.Loaded && churn.Load.Kept > 6L << 20 && churn.Load.Kept < 7L << 20,
            "a value replaced every call counts once",
            (churn.Load.Kept >> 10) + " KB"
        );
    }

    // A collection during a call hides growth, so the mod's kept data is measured again after KeptMinSeconds rather
    // than KeptCheckSeconds. The collection comes from the budgets' clock (HostClock), as the watchdog's does.
    static void CollectionBringsMeasureForward()
    {
        using var clock = new ManualClock();
        using var rig = new Rig();
        const string grow = "t = {} function OnFrame() t[#t + 1] = string.rep('x', 1000) .. #t {0} end";
        rig.Mod("collects", grow.Replace("{0}", "GcRan()"));
        rig.Mod("quiet", grow.Replace("{0}", ""));
        rig.Start();
        rig["collects"].Lua.Globals["GcRan"] = new LuaFunction("GcRan", (c, ct) =>
        {
            clock.Collect();
            return new System.Threading.Tasks.ValueTask<int>(c.Return());
        });
        rig.Run(Limits.KeptMinSeconds * 2);
        long collects = rig["collects"].Load.Kept,
            quiet = rig["quiet"].Load.Kept;
        Check(
            collects - quiet > 10_000,
            "a collection in a call measures the mod's data within KeptMinSeconds",
            collects + " vs " + quiet + " bytes"
        );
    }

    // Due timers past the mod's frame share wait for the next frame, in turn, so none starves.
    static void TimerShares()
    {
        using var clock = new ManualClock();
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "many",
            "runs = {} for i = 1, 10 do runs[i] = 0 TimerEvery(0.05, function() Busy(2) runs[i] = runs[i] + 1 end) end"
        );
        rig.Start();
        var frames = GameFrames(rig, 2);
        var runs = rig["many"].Lua.Global("runs").Read<LuaTable>();
        var counts = Enumerable.Range(1, 10).Select(i => runs[i].Read<double>()).ToList();
        Check(
            Math.Abs(frames.Max() - 6) < 1e-6,
            "a frame runs timers until 5 ms is used, the one that passes it included",
            Frames(frames)
        );
        Check(
            counts.Min() > 0 && counts.Min() >= counts.Max() * 0.8,
            "every timer gets its turn",
            string.Join(",", counts)
        );
    }

    // One heavy frame (a big list after login, opening a big window) and a slow Lua search while the player types
    // stay within the limit.
    static void HeavyMoments()
    {
        using var clock = new ManualClock();
        using var rig = new Rig(new SpyArea());
        rig.Mod("big", "function OnPlayerReady() Spend(200) Busy(50) end function OnOpen() Spend(15) Busy(55) end");
        rig.Mod("search", "function OnKeystroke() Spend(15) end");
        rig.Start();
        rig.GiveSpend();
        rig.Host.FireSetup("OnPlayerReady");
        for (int i = 0; i < 3; i++)
            rig.Host.Fire("OnOpen");
        GameFrames(rig, 1);
        for (int k = 0; k < 50; k++)
        {
            rig.Host.Fire("OnKeystroke");
            GameFrames(rig, 0.1);
        }
        Check(
            rig["big"].Status == ModStatus.Loaded && rig["search"].Status == ModStatus.Loaded,
            "a 460 ms moment and 5 s of 15 ms keystrokes keep the mods on",
            rig["big"].Reason + rig["search"].Reason
        );
        Check(
            Math.Abs(rig["big"].Load.PeakMs - 460) < 1e-6 && rig["search"].Load.PeakMs > 600,
            "both were counted",
            rig["big"].Load.PeakMs + " / " + rig["search"].Load.PeakMs + " ms"
        );
    }

    // The host's own work for a mod (here drawing its HUD widgets, 0.5 ms each) is charged to it.
    sealed class SlowHud : IHudStage
    {
        public bool Ready => true;
        public int CanvasW => 640;
        public int CanvasH => 360;

        public List<URect> GameHud() => new List<URect>();

        public int MeasureText(string line) => 5 * line.Length;

        public void Show(HudWidget w)
        {
            ManualClock.Current.Advance(0.5);
            w.W = 50;
            w.H = 10;
        }

        public void Place(HudWidget w) { }

        public void Remove(HudWidget w) { }
    }

    static void HostWorkCharged()
    {
        using var clock = new ManualClock();
        var env = new TestEnv(TempDir("hostwork"));
        string dir = Path.Combine(env.ModsDir, "busyhud");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.xml"), "<Mod name=\"busyhud\" />");
        File.WriteAllText(
            Path.Combine(dir, "init.lua"),
            "local n = 0 function OnFrame() n = n + 1 for i = 1, 20 do HudText('w' .. i, 'frame ' .. n) end end"
        );
        var areas = CoreAreas.Create(new FakeCommands());
        var hud = new HudArea(new SlowHud());
        areas.Add(hud);
        var host = new ModHost(env, areas);
        host.Start(false);
        var mod = host.Find("busyhud");
        while (env.Clock < 6 && mod.Status == ModStatus.Loaded)
        {
            env.Clock += 1.0 / 60;
            host.Update(1.0 / 60);
            hud.Flush(env.Clock);
        }
        Check(
            mod.Status == ModStatus.Failed && mod.Reason.Contains("most of it in " + WindowArea.DrawingWork),
            "drawing a mod's widgets counts as its time",
            mod.Reason
        );
        Check(env.Clock is > 2.4 and < 2.6, "10 ms a frame: switched off after 2.5 s", env.Clock + " s");
        host.Shutdown();
    }

    static void PerModCaps()
    {
        using var rig = new Rig();
        rig.Mod(
            "caps",
            "cmds = 0 for i = 1, 20 do if CommandRegister('c' .. i, 'x') then cmds = cmds + 1 end end\n"
                + "keys = 0 for i = 1, 1005 do if DataSave('k' .. i, i) then keys = keys + 1 end end\n"
                + "again = DataSave('k1', 'changed') and DataSave('k2', nil) and DataSave('new', 1)\n"
                + "session = 0 for i = 1, 1005 do if SessionSet('s' .. i, i) then session = session + 1 end end"
        );
        string key = "{ id = 'kN', ui_name = 'K', value_default = 'FN', type = 'key' },";
        rig.Mod("keys", "function OnKey() end");
        rig.Write(
            "keys",
            "settings.lua",
            "mod_settings = { "
                + string.Concat(Enumerable.Range(1, 9).Select(i => key.Replace("N", i.ToString())))
                + " }"
        );
        rig.Mod("eight", "function OnKey() end");
        rig.Write(
            "eight",
            "settings.lua",
            "mod_settings = { "
                + string.Concat(Enumerable.Range(1, 8).Select(i => key.Replace("N", i.ToString())))
                + " }"
        );
        rig.Start();
        var caps = rig["caps"].Lua;
        Check(caps.Global("cmds").Read<double>() == 16, "at most 16 commands", caps.Global("cmds").ToString());
        Check(rig.Env.ChatRead.Contains("already has 16 commands, so /c17 was not added"), "the notice says the limit");
        Check(caps.Global("keys").Read<double>() == 1000, "at most 1,000 saved keys");
        Check(rig.Env.ChatRead.Contains("already keeps 1,000 keys, so \"k1001\" was not stored"), "the notice says it");
        Check(caps.Global("again").ToBoolean(), "a kept key can change, and a removed one makes room");
        Check(caps.Global("session").Read<double>() == 1000, "session data the same");
        Check(
            rig["keys"].Status == ModStatus.Failed && rig["keys"].Reason.Contains("at most 8 key settings"),
            "at most 8 key settings",
            rig["keys"].Reason
        );
        Check(rig["eight"].Status == ModStatus.Loaded, "8 are fine", rig["eight"].Reason);
    }

    // The game's reads for the shipped examples: 1,816 koi (and the game's empty #0), bags with 20 koi, a statue of
    // 431 koi, a running offering round.
    sealed class FakeGame : Area
    {
        public readonly Dictionary<int, int> Statue = new Dictionary<int, int>();
        public override string Name => "Fake game";

        public FakeGame()
        {
            for (int i = 1; i <= 431; i++)
                Statue[i * 4] = 1 + i % 3;
        }

        protected internal override void Register(AreaSetup s)
        {
            s.Function("KoiGetAll() -> list", "", c =>
            {
                var list = new LuaTable(1817, 0);
                for (int i = 0; i <= 1816; i++)
                {
                    var k = new LuaTable(0, 3);
                    k["id"] = i;
                    k["name"] = i == 0 ? "None" : "Speckled koi " + i;
                    k["type"] = i == 0 ? 0 : 1 + i % 7;
                    list[i + 1] = k;
                }
                return c.Return(list);
            });
            s.Function("InventoryGetItems( which )", "", c =>
            {
                var list = new LuaTable(40, 0);
                for (int i = 1; i <= 40; i++)
                {
                    var it = new LuaTable(0, 5);
                    bool koi = i > 20;
                    it["id"] = koi ? 9999 : 100 + i;
                    it["name"] = koi ? "Speckled koi " + (i * 7) : "Item " + i;
                    it["count"] = koi ? 1 : i;
                    it["meta"] = koi ? i * 7 : 0;
                    it["category"] = koi ? "koi" : "farming";
                    list[i] = it;
                }
                return c.Return(list);
            });
            s.Function("WalletGetKoiCounts()", "", c =>
            {
                var t = new LuaTable(0, Statue.Count);
                foreach (var kv in Statue)
                    t[kv.Key] = kv.Value;
                return c.Return(t);
            });
            s.Function("WalletGetCurrency()", "", c => c.Return(54380));
            s.Function("WalletGetCitb()", "", c => c.Return(3));
            s.Function("ZoneGet()", "", c => c.Return("town"));
            s.Function("PlayerGetName()", "", c => c.Return("Tester"));
            s.Function("PlayerGetActivity()", "", c => c.Return("idle"));
            s.Function("OfferingGetSecondsLeft()", "", c => c.Return(600 - Host.Env.Now % 600));
            s.Function("OfferingHasOwn()", "", c => c.Return(false));
            s.Function("WeatherIsRaining()", "", c => c.Return(false));
            s.Function("DayStateGet()", "", c => c.Return("day"));
        }
    }

    static LuaTable Item(int id, string name, int count, int meta)
    {
        var t = new LuaTable(0, 5);
        t["id"] = id;
        t["name"] = name;
        t["count"] = count;
        t["meta"] = meta;
        t["category"] = "koi";
        return t;
    }

    // The nine shipped examples, every one with its demo and widgets on, through login, a minute of play with the
    // koi window open, searches, button presses, item and wallet events, tooltips and commands: each far below
    // a tenth of the time and an eighth of the data limit (the game's Mono is about 3 to 5 times slower).
    static void ShippedExamplesLoad()
    {
        string root = Files.RepoRoot();
        var game = new FakeGame();
        using var rig = new Rig(game);
        var lua = new List<string>();
        foreach (var dir in Directory.GetDirectories(Path.Combine(root, "examples")))
        {
            string id = Path.GetFileName(dir);
            if (id.StartsWith("_"))
                continue;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(dir, file);
                rig.WriteBytes(id, rel, File.ReadAllBytes(file));
            }
            if (File.Exists(Path.Combine(dir, "init.lua")))
                lua.Add(id);
        }
        var demo = new MemorySettings();
        demo.Values["demo"] = true;
        rig.Env.Settings["example"] = demo;
        rig.Start();
        rig.Host.FireSetup("OnPlayerReady");
        rig.Host.Fire("OnZoneChanged", "town");
        GameFrames(rig, 5);
        rig.Commands.Type("koi");
        rig.Commands.Type("example window");
        rig.Commands.Type("stats");
        var koiWindow = rig.Windows.Windows.First(w => w.ModId == "koi_checklist");
        for (int round = 0; round < 6; round++)
        {
            for (int b = 0; b < koiWindow.Buttons.Count; b++)
            {
                rig.Windows.ButtonPressed(koiWindow, b);
                GameFrames(rig, 0.2);
            }
            foreach (var typed in new[] { "s", "sp", "spe", "spec", "speckled 1", "" })
            {
                rig.Windows.SearchChanged(koiWindow, typed);
                GameFrames(rig, 0.1);
            }
            game.Statue[1000 + round] = 1;
            rig.Host.Fire("OnInventoryChanged");
            rig.Host.Fire("OnWalletChanged", 4, new Args(54380 + round, 3, 100, 0));
            rig.Host.Fire("OnItemGained", () => Item(9999, "Speckled koi 7", 1, 7));
            for (int h = 0; h < 30; h++)
                rig.Tooltips.Lines(TooltipSource.OwnBag, () => Item(9999, "Speckled koi " + h, 1, h));
            GameFrames(rig, 8);
        }
        foreach (var id in lua)
        {
            var mod = rig[id];
            Note(
                "example "
                    + id
                    + ": at most "
                    + mod.Load.PeakMs.ToString("0.0")
                    + " ms in 5 s, keeps "
                    + (mod.Load.Kept / 1024.0).ToString("0")
                    + " KB"
            );
            Check(mod.Status == ModStatus.Loaded, "example " + id + " runs", mod.Reason);
            Check(
                mod.Load.PeakMs < Limits.WindowMs / 10,
                "example " + id + " far below the time limit",
                mod.Load.PeakMs + " ms"
            );
            Check(
                mod.Load.Kept > 0 && mod.Load.Kept < Limits.KeptBytes / 8,
                "example " + id + " far below the data limit",
                mod.Load.Kept + " bytes"
            );
        }
        Check(
            !rig.Env.ChatRead.Contains(" ms in 5 seconds") && !rig.Env.ChatRead.Contains("of Lua data"),
            "no example gets a notice",
            rig.Env.ChatRead
        );
        var koi = rig["koi_checklist"];
        long t0 = Stopwatch.GetTimestamp();
        LuaData.Measure(
            koi.Lua.Globals,
            Enumerable.Empty<LuaFunction>(),
            koi.Lua.OtherRoots(),
            Limits.KeptBytes,
            out _
        );
        Note("measuring koi_checklist's data: " + ModLoad.MsSince(t0).ToString("0.00") + " ms");
        Check(
            CommandsArea
                .ModReport(rig.Host, koi)
                .Any(l =>
                    l.StartsWith("  Load: ") && l.Contains(" of 1,500 ms in 5 seconds") && l.Contains("Lua data about")
                ),
            "/mods <id> shows the load"
        );
    }
}
