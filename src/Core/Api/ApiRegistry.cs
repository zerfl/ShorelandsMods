using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lua;

namespace ShorelandsMods.Core
{
    internal delegate int ApiImpl(ApiCall call);

    internal sealed class ApiFunction
    {
        public readonly string Name,
            Signature,
            Description,
            Area;

        public readonly ApiImpl Impl;

        public GameHook Hook { get; private set; }

        public LuaValue Unavailable { get; private set; }

        internal ApiFunction(string signature, string description, string area, ApiImpl impl)
        {
            Name = ApiRegistry.NameOf(signature);
            Signature = signature;
            Description = description;
            Area = area;
            Impl = impl;
        }

        // While the hook is missing (a game update removed it) the function returns this without running.
        public ApiFunction Needs(GameHook hook, LuaValue unavailable)
        {
            Hook = hook;
            Unavailable = unavailable;
            return this;
        }
    }

    internal sealed class CallbackInfo
    {
        public readonly string Name,
            Signature,
            Description,
            Area;

        internal CallbackInfo(string signature, string description, string area)
        {
            Name = ApiRegistry.NameOf(signature);
            Signature = signature;
            Description = description;
            Area = area;
        }
    }

    // A piece of the game a feature needs, resolved by name once at start.
    internal sealed class GameHook
    {
        public readonly string Name,
            Feature;
        readonly Func<bool> resolve;

        public bool Available { get; private set; }
        public string Problem { get; private set; }

        internal GameHook(string name, string feature, Func<bool> resolve)
        {
            Name = name;
            Feature = feature;
            this.resolve = resolve;
        }

        internal void Resolve()
        {
            try
            {
                Available = resolve();
                if (!Available)
                    Problem = "not found";
            }
            catch (Exception e)
            {
                Available = false;
                Problem = e.GetType().Name + ": " + e.Message;
            }
        }
    }

    internal sealed class AreaSetup
    {
        readonly ApiRegistry registry;
        readonly Area area;

        internal AreaSetup(ApiRegistry registry, Area area)
        {
            this.registry = registry;
            this.area = area;
        }

        // The name is the signature up to "(": "GamePrint( text:string )".
        public ApiFunction Function(string signature, string description, ApiImpl impl)
        {
            var f = new ApiFunction(signature, description, area.Name, impl);
            if (registry.Find(f.Name) != null)
                throw new InvalidOperationException("API function " + f.Name + " registered twice");
            registry.functions.Add(f);
            return f;
        }

        public void Callback(string signature, string description) =>
            registry.callbacks.Add(new CallbackInfo(signature, description, area.Name));

        public GameHook Hook(string name, string feature, Func<bool> resolve)
        {
            var h = new GameHook(name, feature, resolve);
            registry.hooks.Add(h);
            return h;
        }
    }

    internal sealed class ApiRegistry
    {
        internal readonly List<ApiFunction> functions = new List<ApiFunction>();
        internal readonly List<CallbackInfo> callbacks = new List<CallbackInfo>();
        internal readonly List<GameHook> hooks = new List<GameHook>();
        readonly Action<string> logError;

        public ApiRegistry(Action<string> logError)
        {
            this.logError = logError;
        }

        public IReadOnlyList<ApiFunction> Functions => functions;
        public IReadOnlyList<CallbackInfo> Callbacks => callbacks;
        public IReadOnlyList<GameHook> Hooks => hooks;

        public ApiFunction Find(string name) => functions.FirstOrDefault(f => f.Name == name);

        // Names an author may mean when a global is missing.
        public IEnumerable<string> KnownNames =>
            functions.Select(f => f.Name).Concat(callbacks.Select(c => c.Name).Where(n => n.IndexOf('<') < 0));

        internal static string NameOf(string signature)
        {
            int p = signature.IndexOf('(');
            if (p <= 0)
                throw new ArgumentException("signature without '(': " + signature);
            return signature.Substring(0, p).Trim();
        }

        internal void Install(LuaTable env, Mod mod)
        {
            foreach (var f in functions)
            {
                var fn = f;
                env[f.Name] = new LuaFunction(f.Name, (ctx, ct) => new ValueTask<int>(Dispatch(fn, mod, ctx)));
            }
        }

        int Dispatch(ApiFunction f, Mod mod, LuaFunctionExecutionContext ctx)
        {
            if (f.Hook != null && !f.Hook.Available)
                return ctx.Return(f.Unavailable);
            long t0 = HostClock.Now;
            Watchdog.HostEnter();
            mod.CallRefused = false;
            try
            {
                return f.Impl(new ApiCall(mod, f, ctx));
            }
            catch (Exception e) when (!(e is LuaRuntimeException) && !(e is OperationCanceledException))
            {
                logError("[" + mod.Id + "] " + f.Name + " failed inside the host: " + e);
                throw new LuaRuntimeException(ctx.State, f.Name + " failed inside the host (details in the log)");
            }
            finally
            {
                Watchdog.HostLeave(HostClock.Now - t0);
            }
        }

        // The order of _api.txt's groups (area names), as an author meets them; areas not named follow.
        static readonly string[] Reading =
        {
            "Lifecycle",
            "Player",
            "Output",
            "Notices",
            "Settings",
            "Keys",
            "Saved data",
            "Session data",
            "Time",
            "Text",
            "Colours",
            "Wallet",
            "Inventory",
            "Data tables",
            "World",
            "Offerings",
            "Chat",
            "Chat display",
            "Commands",
            "HUD",
            "Windows",
            "Item tooltips",
            "Sound",
            "Export",
        };

        static string Ms(double v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        // <game>/mods/_api.txt: one line per function and callback, grouped by area, so the list cannot drift from
        // the code.
        public string ApiText(string hostVersion)
        {
            var sb = new StringBuilder();
            sb.Append("-- ShorelandsMods ")
                .Append(hostVersion)
                .Append(": what a mod can use. Written by the host at start; do not edit.\n")
                .Append("-- A mod that uses something new in this version says so in its mod.xml: needs_host=\"")
                .Append(hostVersion)
                .Append("\".\n")
                .Append(
                    "-- A version is MAJOR.MINOR.PATCH: a new MAJOR can break mods (a new MINOR while MAJOR is 0),\n"
                )
                .Append("-- otherwise MINOR only adds and PATCH only fixes.\n")
                .Append(
                    "-- New to modding? Double-click _guide.html in this folder; the example folder is the mod to copy.\n"
                )
                .Append(
                    "-- name( arguments ) -> results  -- what it does. \"[x]\" = optional. Lines starting with On are\n"
                )
                .Append("-- callbacks: define them as global functions in init.lua and the host calls them.\n")
                .Append(
                    "-- Reads return copies. A read that gives nil: you are not in the world, or the game changed and\n"
                )
                .Append("-- the host cannot read it any more (it says so in chat at start).\n")
                .Append(
                    "-- A wrong type or a missing argument is an error: the mod stops with a chat line. A value that is\n"
                )
                .Append(
                    "-- out of range or not known is never an error: it is clamped or left out, the function returns\n"
                )
                .Append("-- false (or nil), and the player sees one chat notice naming the mod and the value.\n")
                .Append("-- UI units: the screen is 640x360 (1280x720, 1920x1080) or 800x450 (1600x900) of them.\n")
                .Append(
                    "-- Lua 5.2 with string, table, math, bit32 (also as bit) and unpack; os has only time, date and clock.\n"
                )
                .Append(
                    "-- dofile( path ) and dofile_once( path ) run another .lua file of the mod (\"files/x.lua\") and give\n"
                )
                .Append("-- back what it returns (dofile_once runs it only the first time). No io, require or load.\n")
                .Append(
                    "-- A path of a file in the mod folder (dofile, icons, sounds) has at most "
                        + ModPaths.MaxPathChars
                        + " characters.\n"
                )
                .Append(
                    "-- Time: a call may take "
                        + Ms(Limits.Callback.Ms)
                        + " ms (init.lua and OnPlayerReady "
                        + Ms(Limits.Init.Ms)
                        + " ms); all of a mod's calls with the host's\n"
                )
                .Append(
                    "-- work for it at most "
                        + Ms(Limits.WindowMs)
                        + " ms in any "
                        + Ms(Limits.WindowSeconds)
                        + " seconds; Lua data kept between calls at most "
                        + (Limits.KeptBytes >> 20)
                        + " MB.\n"
                )
                .Append("-- A mod over any of these is switched off until its files change.\n")
                .Append(
                    "-- To run a callback without waiting for its event, type /try <mod> in chat: it lists the ways,\n"
                )
                .Append("-- like /try <mod> town or /try <mod> whisper Friend hello. Only that mod hears it.\n");
            var areas = functions
                .Select(f => f.Area)
                .Concat(callbacks.Select(c => c.Area))
                .Distinct()
                .OrderBy(a => Array.IndexOf(Reading, a) is int i && i >= 0 ? i : Reading.Length)
                .ToList();
            foreach (var area in areas)
            {
                sb.Append("\n-- ").Append(area).Append('\n');
                foreach (var f in functions.Where(f => f.Area == area))
                    sb.Append(f.Signature).Append("  -- ").Append(f.Description).Append('\n');
                foreach (var c in callbacks.Where(c => c.Area == area))
                    sb.Append(c.Signature).Append("  -- ").Append(c.Description).Append('\n');
            }
            return sb.ToString();
        }
    }
}
