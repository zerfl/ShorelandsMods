using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core
{
    // Finds, starts, stops and reloads mods, and is the only place that calls into Lua for callbacks. Everything runs
    // on the main thread; Update is called once per frame.
    internal sealed class ModHost
    {
        public readonly IHostEnv Env;
        public readonly ApiRegistry Api;
        readonly List<Area> areas;
        readonly List<Mod> mods = new List<Mod>();
        Mod[] snapshot = new Mod[0];
        List<Action> posted = new List<Action>(),
            draining = new List<Action>();
        readonly HashSet<Area> areaFailed = new HashSet<Area>();
        ChangeWatch watch;
        double nextTick,
            lastTick;
        string luaBlocked;

        public double LastOnFrameMs { get; private set; }

        // Counts host Updates: a mod's frame share (ModLoad.FrameMs) is of the current one.
        public long Frame { get; private set; }
        public ChangeWatch Watch => watch;

        public ModHost(IHostEnv env, IEnumerable<Area> areas)
        {
            Env = env;
            Api = new ApiRegistry(text => env.Log(LogLevel.Error, text));
            this.areas = areas.ToList();
            foreach (var a in this.areas)
            {
                a.Host = this;
                a.Register(new AreaSetup(Api, a));
            }
        }

        public IReadOnlyList<Mod> Mods => mods;

        public Mod Find(string id) => mods.FirstOrDefault(m => m.Id == id);

        // Position in the load order; later wins where mods replace the same asset.
        public int OrderOf(string id) => mods.FindIndex(m => m.Id == id);

        public T Area<T>()
            where T : Area => areas.OfType<T>().FirstOrDefault();

        // No Lua runs while this is set (the send guard could not be installed); asset-only mods still load.
        public void BlockLua(string reason) => luaBlocked = reason;

        public void Start(bool watchFiles)
        {
            var missing = new List<GameHook>();
            foreach (var h in Api.Hooks)
            {
                h.Resolve();
                if (!h.Available)
                    missing.Add(h);
            }
            if (missing.Count > 0)
            {
                Env.Log(
                    LogLevel.Warning,
                    "Game hooks missing (the game changed): "
                        + string.Join("; ", missing.Select(h => h.Feature + " [" + h.Name + ": " + h.Problem + "]"))
                );
                Env.Chat(
                    Color.Warn,
                    "Mods: not working until ShorelandsMods is updated for this game version: "
                        + string.Join(", ", missing.Select(h => h.Feature).Distinct())
                );
            }
            if (luaBlocked == null)
                ModRuntime.WarmUp(e => ErrorText.FromException(e, Limits.Callback, Api.KnownNames).ToString());
            foreach (var a in areas)
                a.HostStarted();
            Directory.CreateDirectory(Env.ModsDir);
            WriteApiText();
            if (watchFiles)
                watch = new ChangeWatch(Env.ModsDir, () => Env.Now);
            LoadAll();
            lastTick = nextTick = Env.Now;
            watch?.Start();
        }

        void WriteApiText()
        {
            string file = Path.Combine(Env.ModsDir, "_api.txt");
            string text = Api.ApiText(Env.HostVersion);
            try
            {
                if (File.Exists(file) && ModFiles.ReadText(Env.ModsDir, file, 1 << 20, out _) == text)
                    return;
                ModFiles.CheckWrite(Env.ModsDir, file);
                File.WriteAllText(file, text);
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                Env.Log(LogLevel.Warning, "_api.txt is not written: " + e.Message);
            }
        }

        public void Post(Action action) => posted.Add(action);

        public void Update(double frameSeconds)
        {
            if (LuaGuard.ResetForFrame())
                Env.Log(LogLevel.Warning, "the 'Lua is running' flag was still set at the start of a frame; cleared");
            Frame++;
            double now = Env.Now;
            if (watch != null)
            {
                var due = watch.Due();
                if (due.Count > 0)
                    foreach (var id in due.ToArray())
                        ReloadChanged(id);
            }
            foreach (var mod in snapshot)
                if (mod.Status == ModStatus.Loaded && mod.Lua != null)
                {
                    if (mod.Load.MeasureDue(now))
                        Measure(mod, now);
                    Judge(mod);
                }
            // Areas first: state they poll (player ready) is current before the events posted since the last frame.
            foreach (var a in areas)
            {
                try
                {
                    a.Update(now);
                }
                catch (Exception e)
                {
                    if (areaFailed.Add(a))
                        Env.Log(LogLevel.Error, "area " + a.Name + " failed in Update (logged once): " + e);
                }
            }
            if (posted.Count > 0)
            {
                var run = posted;
                posted = draining;
                draining = run;
                foreach (var a in run)
                    Guard("event", a);
                run.Clear();
            }
            if (now >= nextTick)
            {
                double dt = now - lastTick;
                lastTick = now;
                nextTick += Limits.TickSeconds;
                if (nextTick <= now)
                    nextTick = now + Limits.TickSeconds;
                Fire("OnTick", dt);
            }
            long t0 = Stopwatch.GetTimestamp();
            Fire("OnFrame", frameSeconds);
            LastOnFrameMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        public void Shutdown()
        {
            for (int i = mods.Count - 1; i >= 0; i--)
                StopMod(mods[i], true);
            foreach (var a in areas)
                Guard("shutdown " + a.Name, a.Shutdown);
            watch?.Dispose();
        }

        // ---- finding, starting and stopping ----

        void LoadAll()
        {
            var ids = Directory
                .GetDirectories(Env.ModsDir)
                .Select(Path.GetFileName)
                .Where(n => !ModPaths.IsHostName(n));
            foreach (var id in LoadOrder.Arrange(ids, Env.SavedOrder))
            {
                mods.Add(NewMod(id));
                watch?.Loaded(id);
            }
            Refresh();
            foreach (var mod in mods.ToArray())
                if (Prepare(mod))
                    StartMod(mod);
            Env.Log(LogLevel.Info, "mods: " + string.Join("; ", StatusLines().Select(OneLogLine)));
        }

        Mod NewMod(string id) => new Mod(this, id, Path.Combine(Env.ModsDir, id), Path.Combine(Env.DataRoot, id));

        // Reads mod.xml and decides whether the mod may start; sets its status otherwise.
        bool Prepare(Mod mod)
        {
            string problem = ModId.Problem(mod.Id);
            if (problem != null)
                return Set(mod, ModStatus.Skipped, problem);
            string xml = Path.Combine(mod.Dir, "mod.xml");
            if (!File.Exists(xml))
                return Set(
                    mod,
                    ModStatus.Skipped,
                    File.Exists(xml + ".txt") ? "mod.xml is missing; " + TxtEnding("mod.xml") : "mod.xml is missing"
                );
            string link = ModFiles.Problem(mod.Dir, xml);
            if (link != null)
                return Set(mod, ModStatus.Failed, link);
            try
            {
                mod.Manifest = ModManifest.Read(xml);
            }
            catch (ModProblem p)
            {
                return Set(mod, ModStatus.Failed, p.Message);
            }
            if (mod.Manifest.NeedsHost is SemVer needs)
            {
                // A host version that does not parse is a build error (VersionTests); a mod that asks is not run.
                if (!SemVer.TryParse(Env.HostVersion, out var have))
                    return Set(
                        mod,
                        ModStatus.Skipped,
                        "it needs host "
                            + needs
                            + ", and this host's version \""
                            + TextEscape.Clip(TextEscape.Plain(Env.HostVersion ?? ""), 40)
                            + "\" cannot be compared; update ShorelandsMods"
                    );
                if (SemVer.Unmet(needs, have) is string unmet)
                    return Set(mod, ModStatus.Skipped, unmet);
            }
            if (Env.IsSwitchedOff(mod.Id))
                return Set(mod, ModStatus.Off, "switched off");
            return true;
        }

        // Notepad saves "mod.xml" as mod.xml.txt unless told otherwise, and Explorer hides the ending.
        static string TxtEnding(string name) =>
            "the file is called "
            + name
            + ".txt. Rename it to "
            + name
            + " (to see the .txt, tick View > Show > File name extensions in File Explorer).";

        static bool Set(Mod mod, ModStatus status, string reason)
        {
            mod.Status = status;
            mod.Reason = reason;
            return false;
        }

        void StartMod(Mod mod)
        {
            mod.Status = ModStatus.Loaded;
            mod.Reason = null;
            mod.LoadedAt = Env.Now;
            try
            {
                // settings.lua runs in ModStarting: no Lua file of the mod runs while Lua is blocked.
                if (
                    luaBlocked != null
                    && (
                        File.Exists(Path.Combine(mod.Dir, "init.lua"))
                        || File.Exists(Path.Combine(mod.Dir, "settings.lua"))
                    )
                )
                    throw new ModProblem(luaBlocked);
                foreach (var a in areas)
                    a.ModStarting(mod);
                foreach (var name in new[] { "init.lua", "settings.lua" })
                    if (!File.Exists(Path.Combine(mod.Dir, name)) && File.Exists(Path.Combine(mod.Dir, name + ".txt")))
                        Notice(mod, name + " is not run: " + TxtEnding(name));
                if (File.Exists(Path.Combine(mod.Dir, "init.lua")))
                {
                    mod.Lua = new ModRuntime(mod.Id, mod.Dir);
                    Api.Install(mod.Lua.Globals, mod);
                    mod.Lua.RunFile("init.lua", Limits.Init);
                    if (RefusedSend(mod))
                        return;
                    Account(mod, mod.Lua, "init.lua", Limits.Init);
                    if (!Charge(mod, "init.lua", mod.Lua))
                        return;
                }
            }
            catch (Exception e)
            {
                if (!RefusedSend(mod))
                    Fail(mod, e, Limits.Init);
                return;
            }
            foreach (var a in areas)
            {
                if (mod.Status != ModStatus.Loaded)
                    return;
                try
                {
                    a.ModStarted(mod);
                }
                catch (Exception e)
                {
                    Fail(mod, e, Limits.Callback);
                }
            }
            if (mod.Status == ModStatus.Loaded)
                AnnounceAssetProblems(mod);
        }

        // One chat line when asset files are not used; /mods <id> lists them all.
        void AnnounceAssetProblems(Mod mod)
        {
            var bad = AssetReport(mod).Where(l => l.State == AssetState.Ignored).ToList();
            if (bad.Count == 0)
                return;
            string first = bad[0].File + " is not used: " + bad[0].Detail;
            Notice(
                mod,
                bad.Count == 1
                    ? first + "."
                    : bad.Count + " asset files are not used. " + first + ". /mods " + mod.Id + " lists them all."
            );
        }

        // Every asset file of the mod with what became of it: applied, waiting, ignored (why) or overridden (by whom).
        public List<AssetLine> AssetReport(Mod mod)
        {
            var lines = new List<AssetLine>();
            if (mod.Status != ModStatus.Loaded)
                return lines;
            lines.AddRange(AssetScan.Of(mod).Ignored);
            foreach (var a in areas)
                lines.AddRange(a.AssetLines(mod));
            lines.Sort((x, y) => string.CompareOrdinal(x.File, y.File));
            return lines;
        }

        void StopMod(Mod mod, bool callUnload)
        {
            if (callUnload && mod.Status == ModStatus.Loaded && mod.Lua != null && mod.Lua.HasFunction("OnModUnload"))
            {
                try
                {
                    mod.Lua.Call(mod.Lua.Global("OnModUnload"), Limits.Callback);
                    if (mod.Lua.RefusedSend != null)
                        Log(mod, LogLevel.Warning, "OnModUnload tried to send " + mod.Lua.RefusedSend);
                }
                catch (Exception e)
                {
                    var err = ErrorText.FromException(e, Limits.Callback, Api.KnownNames);
                    Log(mod, LogLevel.Warning, "OnModUnload: " + err + " | " + err.Raw);
                }
            }
            for (int i = areas.Count - 1; i >= 0; i--)
            {
                var a = areas[i];
                Guard("stop " + mod.Id + " in " + a.Name, () => a.ModStopping(mod));
            }
            mod.Resources.DisposeAll(
                (what, e) => Env.Log(LogLevel.Error, "[" + mod.Id + "] removing " + what + " failed: " + e)
            );
            mod.ClearState();
            mod.Lua = null;
        }

        public void ReloadAll()
        {
            for (int i = mods.Count - 1; i >= 0; i--)
                StopMod(mods[i], true);
            mods.Clear();
            LoadAll();
        }

        // Destroys everything the mod made and starts it again from its files; only saved data and settings survive.
        public void Reload(string id)
        {
            watch?.Loaded(id);
            var old = Find(id);
            if (old != null)
            {
                StopMod(old, true);
                mods.Remove(old);
            }
            if (Directory.Exists(Path.Combine(Env.ModsDir, id)))
            {
                var mod = NewMod(id);
                mods.Add(mod);
                var order = LoadOrder.Arrange(mods.Select(m => m.Id), Env.SavedOrder);
                mods.Sort((a, b) => order.IndexOf(a.Id).CompareTo(order.IndexOf(b.Id)));
                Refresh();
                if (Prepare(mod))
                    StartMod(mod);
            }
            Refresh();
        }

        // The Mods page's switch: saved, then the mod stops or starts.
        public void SwitchOff(string id, bool off)
        {
            Env.SetSwitchedOff(id, off);
            Reload(id);
        }

        // The Mods page's order buttons: the mod moves by delta places; it and the mods it passed are reloaded, so
        // their asset claims are decided again in the new order.
        public void Move(string id, int delta)
        {
            var order = mods.Select(m => m.Id).ToList();
            int from = order.IndexOf(id);
            if (from < 0)
                return;
            int to = Math.Max(0, Math.Min(order.Count - 1, from + delta));
            if (to == from)
                return;
            order.RemoveAt(from);
            order.Insert(to, id);
            Env.SaveOrder(order);
            var touched = order.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).ToList();
            mods.Sort((a, b) => order.IndexOf(a.Id).CompareTo(order.IndexOf(b.Id)));
            Refresh();
            foreach (var t in touched)
                Reload(t);
        }

        // FailWith's chat lines: a reload that printed one says nothing more.
        int failLines;

        internal void ReloadChanged(string id)
        {
            int failed = failLines;
            Reload(id);
            var mod = Find(id);
            Env.Log(
                LogLevel.Info,
                "reloaded after a file change: " + (mod == null ? id + " (removed)" : OneLogLine(StatusLine(mod)))
            );
            if (mod != null && failLines == failed)
                Env.Chat(Color.Note, "[" + id + "] reloaded: " + StatusWord(mod));
        }

        void Refresh() => snapshot = mods.ToArray();

        // ---- failures ----

        public void Fail(Mod mod, Exception e, Budget budget) =>
            FailWith(mod, ErrorText.FromException(e, budget, KnownNames(mod), mod.Dir));

        public void FailWith(Mod mod, ModError err)
        {
            Log(mod, LogLevel.Error, err + " | raw: " + err.Raw);
            if (mod.Status == ModStatus.Failed)
                return;
            StopMod(mod, false);
            mod.Status = ModStatus.Failed;
            mod.Reason = err.ToString();
            string text = err.ToString().TrimEnd();
            if (!text.EndsWith(".") && !text.EndsWith("?") && !text.EndsWith("!"))
                text += ".";
            Env.Chat(Color.Error, "[" + mod.Id + "] " + text + " The mod is off until its files change.");
            failLines++;
        }

        IEnumerable<string> KnownNames(Mod mod)
        {
            var own = new List<string>();
            if (mod.Lua != null)
                foreach (var kv in mod.Lua.Globals)
                    if (kv.Value.Type == LuaValueType.Function && kv.Key.TryRead<string>(out var name))
                        own.Add(name);
            return Api.KnownNames.Concat(own);
        }

        bool RefusedSend(Mod mod)
        {
            string what = mod.Lua?.RefusedSend;
            if (what == null)
                return false;
            FailWith(
                mod,
                new ModError
                {
                    Sentence = "it tried to send something to the server (" + what + "), which mods may never do",
                    Raw = "send refused: " + what,
                }
            );
            return true;
        }

        // ---- calling into Lua ----

        public void Fire(string callback) => FireCore(callback, 0, default, null);

        // A callback with init.lua's budget: OnPlayerReady, where a mod first sees the world's tables and builds
        // what it needs from them (a 2,000-row list takes about 20 ms of Lua on the game's Mono).
        public void FireSetup(string callback) => FireCore(callback, 0, default, null, Limits.Init);

        public void Fire(string callback, LuaValue a) => FireCore(callback, 1, new Args(a), null);

        public void Fire(string callback, LuaValue a, LuaValue b) => FireCore(callback, 2, new Args(a, b), null);

        public void Fire(string callback, int argc, Args args) => FireCore(callback, argc, args, null);

        // For a table argument: every mod gets its own copy.
        public void Fire(string callback, Func<LuaValue> fresh) => FireCore(callback, 1, default, fresh);

        void FireCore(string callback, int argc, Args args, Func<LuaValue> fresh, Budget? budget = null)
        {
            foreach (var mod in snapshot)
            {
                if (mod.Status != ModStatus.Loaded || mod.Lua == null)
                    continue;
                var fn = mod.Lua.Global(callback);
                if (fn.Type != LuaValueType.Function)
                    continue;
                Invoke(
                    mod,
                    callback,
                    fn,
                    argc,
                    fresh != null ? new Args(fresh()) : args,
                    out _,
                    budget ?? Limits.Callback
                );
            }
        }

        public void FireOne(Mod mod, string callback, Budget? budget = null)
        {
            if (mod.Status == ModStatus.Loaded && mod.Lua != null && mod.Lua.HasFunction(callback))
                Invoke(mod, callback, mod.Lua.Global(callback), 0, default, out _, budget ?? Limits.Callback);
        }

        // True while a call runs that the player's own input started (CallByPlayer): it may reopen a window the
        // player just closed.
        public bool ByPlayer { get; private set; }

        // Call for a key, command or button the player pressed.
        public bool CallByPlayer(Mod mod, string what, LuaValue fn, int argc = 0, Args args = default)
        {
            bool was = ByPlayer;
            ByPlayer = true;
            try
            {
                return Call(mod, what, fn, argc, args);
            }
            finally
            {
                ByPlayer = was;
            }
        }

        // A Lua function a mod handed the host earlier (timer, command), or one of its callbacks; what names it in
        // the budget notice ("OnKey", "the TimerAfter function (init.lua:12)"). False if the mod failed.
        public bool Call(Mod mod, string what, LuaValue fn, int argc = 0, Args args = default)
        {
            if (mod.Status != ModStatus.Loaded || mod.Lua == null)
                return false;
            return Invoke(mod, what, fn, argc, args, out _, Limits.Callback);
        }

        // A timer's function: charged to "its timers", so the stop names them together.
        public bool CallTimer(Mod mod, string what, LuaValue fn)
        {
            if (mod.Status != ModStatus.Loaded || mod.Lua == null)
                return false;
            return Invoke(mod, what, fn, 0, default, out _, Limits.Callback, Timers);
        }

        const string Timers = "its timers";

        bool Invoke(
            Mod mod,
            string what,
            LuaValue fn,
            int argc,
            Args args,
            out LuaValue result,
            Budget budget,
            string group = null
        )
        {
            result = LuaValue.Nil;
            var lua = mod.Lua;
            try
            {
                result = lua.Call(fn, argc, args, budget);
            }
            catch (Exception e)
            {
                mod.Load.Add(group ?? what, lua.LastWallMs, Env.Now, Frame);
                // A refused send is the more important reason, even if the call also ran out of time.
                if (!RefusedSend(mod))
                    Fail(mod, e, budget);
                return false;
            }
            if (RefusedSend(mod))
                return false;
            Account(mod, lua, what, budget);
            return Charge(mod, group ?? what, lua);
        }

        // Charges a finished call to the mod's load; false if that switched it off.
        bool Charge(Mod mod, string group, ModRuntime lua)
        {
            mod.Load.Add(group, lua.LastWallMs, Env.Now, Frame);
            mod.Load.Grew(lua.LastGrowth, lua.LastCollected);
            return Judge(mod);
        }

        // The host's own work for a mod outside its calls (drawing its widgets, writing its files). Judged at the
        // start of the next frame, so no area's loop loses the mod halfway.
        public void ChargeWork(Mod mod, string group, double ms) => mod.Load.Add(group, ms, Env.Now, Frame);

        // Switches the mod off past its time over time, with one notice at half of it. False if it is off.
        bool Judge(Mod mod)
        {
            if (mod.Status != ModStatus.Loaded)
                return false;
            double now = Env.Now,
                used = mod.Load.WindowMs(now);
            if (used > Limits.WindowMs)
            {
                string sentence = mod.Load.TimeStop(now);
                FailWith(mod, new ModError { Sentence = sentence, Raw = "load: " + mod.Load.Line(now) });
                return false;
            }
            if (used > Limits.WindowMs * CallCosts.NoticeShare && !mod.Load.TimeNoticed)
            {
                mod.Load.TimeNoticed = true;
                string note = mod.Load.TimeNotice(now);
                Log(mod, LogLevel.Warning, note);
                Notice(mod, note);
            }
            return true;
        }

        // Estimates the Lua data the mod keeps (LuaData); the walk is the host's work for it.
        void Measure(Mod mod, double now)
        {
            long t0 = HostClock.Now;
            long kept = LuaData.Measure(
                mod.Lua.Globals,
                areas.SelectMany(a => a.Held(mod)),
                mod.Lua.OtherRoots(),
                Limits.KeptBytes,
                out var mostly
            );
            mod.Load.Measured(kept, mostly, now);
            ChargeWork(mod, "the host's check of its Lua data", ModLoad.MsSince(t0));
            if (kept > Limits.KeptBytes)
                FailWith(mod, new ModError { Sentence = mod.Load.KeptStop(), Raw = "kept: " + kept + " bytes" });
            else if (kept > Limits.KeptBytes / 2 && !mod.Load.KeptNoticed)
            {
                mod.Load.KeptNoticed = true;
                string note = mod.Load.KeptNotice();
                Log(mod, LogLevel.Warning, note);
                Notice(mod, note);
            }
        }

        void Account(Mod mod, ModRuntime lua, string what, Budget budget)
        {
            string note = mod.Costs.Add(what, lua.LastMs, lua.LastSteps, budget);
            if (note != null)
            {
                Log(mod, LogLevel.Warning, note);
                Notice(mod, note);
            }
        }

        // A callback whose result the host uses (OnItemTooltip): its first result, or nil if the mod has no such
        // callback or failed in it (it is then off, as for any other callback).
        public LuaValue Ask(Mod mod, string callback, LuaValue a)
        {
            if (mod.Status != ModStatus.Loaded || mod.Lua == null)
                return LuaValue.Nil;
            var fn = mod.Lua.Global(callback);
            if (fn.Type != LuaValueType.Function)
                return LuaValue.Nil;
            return Invoke(mod, callback, fn, 1, new Args(a), out var result, Limits.Callback) ? result : LuaValue.Nil;
        }

        void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Env.Log(LogLevel.Error, what + " failed: " + e);
            }
        }

        // ---- reporting ----

        public static string StatusWord(Mod mod)
        {
            switch (mod.Status)
            {
                case ModStatus.Loaded:
                    return "loaded";
                case ModStatus.Off:
                    return "off";
                case ModStatus.Skipped:
                    return "skipped: " + mod.Reason;
                default:
                    return "failed: " + mod.Reason;
            }
        }

        public string StatusLine(Mod mod) =>
            mod.Id
            + (string.IsNullOrEmpty(mod.Manifest?.Version) ? "" : " " + mod.Manifest.Version)
            + ": "
            + StatusWord(mod);

        public IEnumerable<string> StatusLines() => mods.Select(StatusLine);

        // A local chat line from the host about a mod.
        public void Notice(Mod mod, string text) => Env.Chat(Color.Note, "[" + mod.Id + "] " + text);

        // A file the host writes for the mod could not be written: a log line each time, and the mod's first such failure
        // one notice. The mod is never switched off for it.
        public void WriteFailed(Mod mod, string what, Exception e)
        {
            Log(mod, LogLevel.Error, what + " failed: " + e.Message);
            var told = mod.Get<WriteFailures>();
            if (told.Told)
                return;
            told.Told = true;
            Notice(mod, what + " failed (" + TextEscape.Clip(e.Message, 120) + "); details in the log");
        }

        sealed class WriteFailures
        {
            public bool Told;
        }

        // A log line about a mod, with text from it: one line, cut at Limits.LogLineChars.
        public void Log(Mod mod, LogLevel level, string text) => Env.Log(level, "[" + mod.Id + "] " + OneLogLine(text));

        static string OneLogLine(string text) => TextEscape.LogLine(text, Limits.LogLineChars);

        // Mod is the tint of the lines a mod prints (GamePrint) and of a notice's mod name; no game or host line has
        // it, and a mod's text cannot change it.
        public static class Color
        {
            public const string Note = "#BFE3FF",
                Warn = "#FFD27F",
                Error = "#FF9C9C",
                Mod = "#D2B4FF",
                // Behind a GamePrintLink label: the chat font draws no underline.
                LinkBox = "#8C5AD880";
        }
    }

    internal static class HostEnvExtensions
    {
        // Every chat line the host prints goes through here: the text as written, escaped and cut here, in one colour.
        public static void Chat(this IHostEnv env, string color, string text) =>
            env.Chat(
                "<color=" + color + ">" + TextEscape.Chat(TextEscape.Clip(text, Limits.HostLineChars)) + "</color>"
            );

        // The same with clickable labels after the text, each on a box that wraps as one piece, inside the line's colour:
        // the only tags are the host's, the ids only ChatLinkId's (ChatDecorator skips the whole span, so no highlight
        // splits a link).
        public static void Chat(
            this IHostEnv env,
            string color,
            string text,
            IReadOnlyList<(string Id, string Label)> links
        )
        {
            string body = TextEscape.Chat(TextEscape.Clip(text, Limits.HostLineChars));
            var sb = new System.Text.StringBuilder("<color=").Append(color).Append('>').Append(body);
            bool space = body.Length > 0 && !body.EndsWith(" ", StringComparison.Ordinal);
            foreach (var (id, label) in links)
            {
                if (!Areas.ChatLinkId.WellFormed(id))
                    throw new ArgumentException("not a host link id");
                if (space)
                    sb.Append(' ');
                space = true;
                sb.Append("<link=")
                    .Append(id)
                    .Append("><nobr><mark=")
                    .Append(ModHost.Color.LinkBox)
                    .Append("> ")
                    .Append(TextEscape.Chat(TextEscape.Clip(label, Limits.ChatLinkLabelChars)))
                    .Append(" </mark></nobr></link>");
            }
            env.Chat(sb.Append("</color>").ToString());
        }
    }
}
