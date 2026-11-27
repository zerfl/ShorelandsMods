using System;
using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core
{
    // Which mod's file is used for each name when several mods have one: the last in load order wins, and
    // withdrawing the winner (unload, switch-off) hands the name to the next one. Touched names are collected in
    // Dirty; TakeDirty returns those whose winner really changed since it last returned them, so the game applies
    // each change once per frame, whatever happened in between.
    internal sealed class Overrides<T>
        where T : class
    {
        public sealed class Claim
        {
            public string ModId;
            public T Value;
        }

        readonly Func<string, int> orderOf;
        readonly Dictionary<string, List<Claim>> byKey = new Dictionary<string, List<Claim>>();
        readonly Dictionary<string, Claim> applied = new Dictionary<string, Claim>();
        public readonly HashSet<string> Dirty = new HashSet<string>();

        // orderOf: the mod's position in the load order.
        public Overrides(Func<string, int> orderOf)
        {
            this.orderOf = orderOf;
        }

        public void Add(string key, string modId, T value)
        {
            if (!byKey.TryGetValue(key, out var list))
                byKey[key] = list = new List<Claim>();
            list.RemoveAll(c => c.ModId == modId);
            list.Add(new Claim { ModId = modId, Value = value });
            Dirty.Add(key);
        }

        public bool Remove(string key, string modId)
        {
            if (!byKey.TryGetValue(key, out var list) || list.RemoveAll(c => c.ModId == modId) == 0)
                return false;
            if (list.Count == 0)
                byKey.Remove(key);
            Dirty.Add(key);
            return true;
        }

        // Every claim of a mod; returns the keys.
        public List<string> RemoveMod(string modId)
        {
            var keys = byKey.Where(kv => kv.Value.Any(c => c.ModId == modId)).Select(kv => kv.Key).ToList();
            foreach (var k in keys)
                Remove(k, modId);
            return keys;
        }

        public Claim Winner(string key)
        {
            if (!byKey.TryGetValue(key, out var list))
                return null;
            Claim best = null;
            int bestAt = int.MinValue;
            foreach (var c in list)
            {
                int at = orderOf(c.ModId);
                if (at >= bestAt)
                {
                    best = c;
                    bestAt = at;
                }
            }
            return best;
        }

        public T WinnerValue(string key) => Winner(key)?.Value;

        public bool Has(string key) => byKey.ContainsKey(key);

        public IEnumerable<string> Keys => byKey.Keys;

        public IEnumerable<string> KeysOf(string modId) =>
            byKey.Where(kv => kv.Value.Any(c => c.ModId == modId)).Select(kv => kv.Key);

        public T ValueOf(string key, string modId) =>
            byKey.TryGetValue(key, out var list) ? list.FirstOrDefault(c => c.ModId == modId)?.Value : null;

        public List<string> TakeDirty()
        {
            var changed = new List<string>();
            foreach (var key in Dirty)
            {
                var w = Winner(key);
                applied.TryGetValue(key, out var was);
                if (w == was)
                    continue;
                if (w == null)
                    applied.Remove(key);
                else
                    applied[key] = w;
                changed.Add(key);
            }
            Dirty.Clear();
            return changed;
        }

        // The game could not take the winner yet (still loading): TakeDirty returns the key again.
        public void Retry(string key)
        {
            applied.Remove(key);
            Dirty.Add(key);
        }
    }
}
