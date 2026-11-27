using System;
using System.Collections.Generic;

namespace ShorelandsMods.Core
{
    internal enum ModStatus
    {
        Loaded,
        Failed,
        Off,
        Skipped,
    }

    internal sealed class Mod
    {
        public readonly string Id,
            Dir,
            DataDir;
        public readonly ModHost Host;
        public ModManifest Manifest { get; internal set; }
        public ModStatus Status { get; internal set; }

        // Why it failed, is off or was skipped, in words for the player.
        public string Reason { get; internal set; }
        public ModRuntime Lua { get; internal set; }
        public double LoadedAt { get; internal set; }
        public readonly ResourceRegistry Resources = new ResourceRegistry();

        // Kept after a failure, so the Mods page still shows what the calls cost.
        public readonly CallCosts Costs = new CallCosts();
        public readonly ModLoad Load = new ModLoad();

        // Set by ApiCall.Refuse during the running API call.
        internal bool CallRefused;

        readonly Dictionary<Type, object> areaState = new Dictionary<Type, object>();

        internal Mod(ModHost host, string id, string dir, string dataDir)
        {
            Host = host;
            Id = id;
            Dir = dir;
            DataDir = dataDir;
        }

        public string Name => Manifest?.Name ?? Id;

        // Per-mod state of an area, created on first use and dropped when the mod stops.
        public T Get<T>()
            where T : class, new()
        {
            if (!areaState.TryGetValue(typeof(T), out var o))
                areaState[typeof(T)] = o = new T();
            return (T)o;
        }

        public T Find<T>()
            where T : class => areaState.TryGetValue(typeof(T), out var o) ? (T)o : null;

        internal void ClearState() => areaState.Clear();
    }
}
