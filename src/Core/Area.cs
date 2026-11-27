using System;
using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core
{
    // A feature area ("wallet", "timers", "hud"): registers its functions, callbacks and game hooks in
    // Register, keeps per-mod state in Mod.Get<T>() and per-mod things in Mod.Resources, and is told about each mod's
    // lifecycle.
    internal abstract class Area
    {
        public abstract string Name { get; }

        public ModHost Host { get; internal set; }

        bool eventFailed;

        // Host work run inside one of the game's own C# events: an error is logged once and never reaches the game.
        protected void InGameEvent(Action work)
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                if (eventFailed)
                    return;
                eventFailed = true;
                Host?.Env.Log(LogLevel.Error, "area " + Name + " failed in a game event (logged once): " + e);
            }
        }

        protected internal abstract void Register(AreaSetup setup);

        // Hooks are resolved; mods are not loaded yet.
        protected internal virtual void HostStarted() { }

        // Before init.lua runs. Throw ModProblem to stop the mod with a sentence for the player.
        protected internal virtual void ModStarting(Mod mod) { }

        // init.lua ran; the mod is loaded.
        protected internal virtual void ModStarted(Mod mod) { }

        // Unload, reload, switch-off or failure. The mod's resources are disposed right after.
        protected internal virtual void ModStopping(Mod mod) { }

        // Every frame on the main thread, before the host fires OnTick and OnFrame.
        protected internal virtual void Update(double now) { }

        protected internal virtual void Shutdown() { }

        // The mod's asset files this area handles, for /mods <id> and the Mods page (ModHost.AssetReport).
        protected internal virtual IEnumerable<AssetLine> AssetLines(Mod mod) => Enumerable.Empty<AssetLine>();

        // The mod's Lua functions this area keeps (timers, buttons), so LuaData counts what they hold.
        protected internal virtual IEnumerable<Lua.LuaFunction> Held(Mod mod) => Enumerable.Empty<Lua.LuaFunction>();
    }
}
