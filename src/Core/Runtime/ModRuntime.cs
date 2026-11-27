using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using Lua.Runtime;
using Lua.Standard;

namespace ShorelandsMods.Core
{
    // A budget ran out inside a call: "instructions", "memory" or "time". Derives from OperationCanceledException,
    // which pcall does not catch; xpcall does unless the call's token is cancelled, so the hook cancels it too.
    internal sealed class BudgetExceeded : OperationCanceledException
    {
        public readonly string What;

        public BudgetExceeded(string what, Exception inner = null)
            : base("budget exceeded: " + what, inner)
        {
            What = what;
        }
    }

    // Up to four arguments of a call into Lua, without an array.
    internal readonly struct Args
    {
        public readonly LuaValue A,
            B,
            C,
            D;

        public Args(LuaValue a, LuaValue b = default, LuaValue c = default, LuaValue d = default)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

    // One mod's Lua state (Lua-CSharp 0.5.8, Lua 5.2), restricted to the sandboxed libraries, and every call into it
    // bounded by an instruction-count hook, the watchdog's time budget and a memory-growth check.
    internal sealed class ModRuntime
    {
        static readonly string[] RemovedGlobals = { "load", "loadfile", "dofile", "collectgarbage", "print" };
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        public readonly string ModId,
            Dir;

        // Set by the send guard (through LuaGuard) when a send was dropped during a call of this runtime.
        public string RefusedSend;

        // What the last call used of its budget: charged milliseconds and steps (counted per hook interval).
        public double LastMs { get; private set; }
        public long LastSteps { get; private set; }

        // The last call's real time (Lua, host functions and collections in it), its growth of the managed heap, and
        // whether a collection ran in it.
        public double LastWallMs { get; private set; }
        public long LastGrowth { get; private set; }
        public bool LastCollected { get; private set; }

        readonly LuaState state;
        readonly LuaValue stringMeta;
        readonly Dictionary<string, LuaValue[]> loadedOnce = new Dictionary<string, LuaValue[]>(
            StringComparer.OrdinalIgnoreCase
        );
        CancellationTokenSource cts = new CancellationTokenSource();
        long instructionsLeft,
            memoryStart;
        string tripped;

        public ModRuntime(string modId, string dir)
        {
            ModId = modId;
            Dir = dir;
            state = LuaState.Create();
            state.OpenBasicLibrary();
            state.OpenStringLibrary();
            state.OpenTableLibrary();
            state.OpenMathLibrary();
            state.OpenBitwiseLibrary();
            var env = state.Environment;
            foreach (var name in RemovedGlobals)
                env[name] = LuaValue.Nil;
            env["string"].Read<LuaTable>()["dump"] = LuaValue.Nil;
            env["unpack"] = env["table"].Read<LuaTable>()["unpack"];
            env["bit"] = env["bit32"];
            env["os"] = MakeOs();
            env["dofile"] = new LuaFunction("dofile", DoFile);
            env["dofile_once"] = new LuaFunction("dofile_once", DoFileOnce);
            state.RunAsync(state.Load("__meta = getmetatable('')", "@host"), CancellationToken.None).AsTask().Wait();
            stringMeta = env["__meta"];
            env["__meta"] = LuaValue.Nil;
            state.SetHook(new LuaFunction("budget", Hook), "", Limits.HookInterval);
        }

        public LuaTable Globals => state.Environment;

        static LuaTable MakeOs()
        {
            var os = new LuaTable(0, 3);
            foreach (var f in OperatingSystemLibrary.Instance.Functions)
                if (f.Name == "time" || f.Name == "date")
                    os[f.Name] = f.Func;
            os["clock"] = new LuaFunction("clock", (c, ct) => new ValueTask<int>(c.Return(Clock.Elapsed.TotalSeconds)));
            return os;
        }

        public LuaValue Global(string name) => state.Environment[name];

        // What Lua can keep data in besides its globals and the functions the host holds (LuaData).
        public IEnumerable<LuaValue> OtherRoots()
        {
            yield return stringMeta;
            yield return state.Registry;
            yield return state.LoadedModules;
            yield return state.PreloadModules;
            var meta = state.Environment.Metatable;
            if (meta != null)
                yield return meta;
            foreach (var values in loadedOnce.Values)
            foreach (var v in values)
                yield return v;
        }

        public bool HasFunction(string name) => state.Environment[name].Type == LuaValueType.Function;

        // Runs a file of the mod folder (init.lua, settings.lua) as one call.
        public void RunFile(string relative, Budget budget)
        {
            var chunk = Load(Path.Combine(Dir, relative), relative);
            Invoke(chunk, 0, default, budget);
        }

        public LuaValue Call(LuaValue fn, Budget budget) => Invoke(fn, 0, default, budget);

        public LuaValue Call(LuaValue fn, LuaValue a, Budget budget) => Invoke(fn, 1, new Args(a), budget);

        public LuaValue Call(LuaValue fn, LuaValue a, LuaValue b, Budget budget) =>
            Invoke(fn, 2, new Args(a, b), budget);

        public LuaValue Call(LuaValue fn, int argc, Args args, Budget budget) => Invoke(fn, argc, args, budget);

        LuaValue Invoke(LuaValue fn, int argc, Args args, Budget budget)
        {
            var stack = state.Stack;
            int top = stack.Count;
            stack.Push(fn);
            if (argc > 0)
                stack.Push(args.A);
            if (argc > 1)
                stack.Push(args.B);
            if (argc > 2)
                stack.Push(args.C);
            if (argc > 3)
                stack.Push(args.D);
            if (cts.IsCancellationRequested)
            {
                cts.Dispose();
                cts = new CancellationTokenSource();
            }
            instructionsLeft = budget.Instructions;
            memoryStart = GC.GetTotalMemory(false);
            int collections = HostClock.Collections();
            LastGrowth = 0;
            tripped = null;
            var previous = LuaGuard.Enter(this);
            long started = HostClock.Now;
            Watchdog.Arm(cts, budget.Ms);
            try
            {
                var task = state.CallAsync(top, cts.Token);
                if (!task.IsCompleted)
                    throw new InvalidOperationException("a Lua call did not finish synchronously");
                int results = task.Result;
                LastGrowth = GC.GetTotalMemory(false) - memoryStart;
                if (LastGrowth > Limits.MemoryGrowthBytes)
                    throw new BudgetExceeded("memory");
                return results > 0 ? stack[top] : LuaValue.Nil;
            }
            catch (OperationCanceledException e) when (!(e is BudgetExceeded))
            {
                throw new BudgetExceeded(tripped ?? "time", e) { Data = { ["ms"] = HostClock.MsSince(started) } };
            }
            finally
            {
                LastMs = Watchdog.Disarm();
                LastWallMs = HostClock.MsSince(started);
                LastCollected = HostClock.Collections() != collections;
                LastSteps = budget.Instructions - instructionsLeft;
                LuaGuard.Exit(previous);
                stack.PopUntil(top);
            }
        }

        ValueTask<int> Hook(LuaFunctionExecutionContext c, CancellationToken ct)
        {
            instructionsLeft -= Limits.HookInterval;
            if (instructionsLeft < 0)
                Trip("instructions");
            else if (GC.GetTotalMemory(false) - memoryStart > Limits.MemoryGrowthBytes)
                Trip("memory");
            return new ValueTask<int>(c.Return());
        }

        void Trip(string what)
        {
            tripped = tripped ?? what;
            cts.Cancel();
            throw new BudgetExceeded(what);
        }

        LuaClosure Load(string full, string relative)
        {
            string source = ModFiles.ReadText(Dir, full, Limits.LuaFileBytes, out string problem);
            if (source == null)
                throw new ModProblem(problem);
            if (source.Length > 0 && source[0] == '\u001b')
                throw new ModProblem(relative + " is compiled Lua; only Lua source files are allowed");
            return state.Load(source, "@" + relative);
        }

        ValueTask<int> DoFile(LuaFunctionExecutionContext c, CancellationToken ct) => RunNested(c, ct, false);

        ValueTask<int> DoFileOnce(LuaFunctionExecutionContext c, CancellationToken ct) => RunNested(c, ct, true);

        ValueTask<int> RunNested(LuaFunctionExecutionContext c, CancellationToken ct, bool once)
        {
            string fn = once ? "dofile_once" : "dofile";
            var arg = c.HasArgument(0) ? c.GetArgument(0) : LuaValue.Nil;
            if (!arg.TryRead<string>(out var path))
                throw new LuaRuntimeException(
                    c.State,
                    fn + ": argument 1 (path) must be text, got " + arg.TypeToString()
                );
            string full = ModPaths.ResolveLua(Dir, ModId, path, out var relative, out var error);
            if (full == null)
                throw new LuaRuntimeException(c.State, fn + ": " + error);
            if (once && loadedOnce.TryGetValue(full, out var cached))
                return new ValueTask<int>(c.Return(cached));
            if (!File.Exists(full))
                throw new LuaRuntimeException(c.State, fn + ": there is no file " + relative + " in the mod folder");
            LuaClosure chunk;
            try
            {
                chunk = Load(full, relative);
            }
            catch (ModProblem p)
            {
                throw new LuaRuntimeException(c.State, fn + ": " + p.Message);
            }
            c.State.Stack.PopUntil(c.ReturnFrameBase);
            int n = c.State.RunAsync(chunk, ct).Result;
            if (once)
                loadedOnce[full] = c.State.Stack.AsSpan().Slice(c.ReturnFrameBase, n).ToArray();
            return new ValueTask<int>(n);
        }

        // Runs the common paths once (errors, library functions, dofile refusals) so the JIT does not charge their
        // first compilation to a mod's time budget.
        public static void WarmUp(Func<Exception, string> describe)
        {
            string[] snippets =
            {
                "local t = {} for i = 1, 200 do t[#t + 1] = string.format('%d %s %5.2f', i, tostring(i), i / 3) end "
                    + "table.sort(t, function(a, b) return a > b end) local s = table.concat(t, ','):upper():sub(2, 9) "
                    + "s = s:gsub('%d', function(d) return d end) for w in s:gmatch('%a+') do end "
                    + "local x = math.floor(math.sqrt(2) * 100) + bit32.band(7, 3) + #unpack({ 1, 2 }) "
                    + "pcall(error, 'x') pcall(error, { 1 }) os.date('%H:%M') os.time() os.clock() pcall(dofile, '../x.lua')",
                "local t = nil local x = t.y",
                "UnknownFunctionName()",
                "local a = 1 + nil",
                "x = = 1",
                "while true do end",
            };
            var rt = new ModRuntime("warmup", Path.GetTempPath());
            foreach (var src in snippets)
            {
                try
                {
                    rt.Invoke(rt.state.Load(src, "@warmup.lua"), 0, default, Limits.Init);
                }
                catch (Exception e)
                {
                    describe(e);
                }
            }
        }

        // A runtime for settings.lua: the same libraries, no host API, print to the log.
        public static ModRuntime ForSettings(string modId, string dir, Action<string, long> log)
        {
            var rt = new ModRuntime(modId, dir);
            rt.state.Environment["print"] = new LuaFunction("print", (c, ct) =>
            {
                log(LuaText.Join(c.Arguments, Limits.LogLineChars, out long length), length);
                return new ValueTask<int>(c.Return());
            });
            for (int i = 0; i < 4; i++)
                rt.state.Environment[NoitaScopes[i]] = i;
            return rt;
        }

        static readonly string[] NoitaScopes =
        {
            "MOD_SETTING_SCOPE_NEW_GAME",
            "MOD_SETTING_SCOPE_RUNTIME",
            "MOD_SETTING_SCOPE_RUNTIME_RESTART",
            "MOD_SETTING_SCOPE_ONLY_SET_DEFAULT",
        };
    }
}
