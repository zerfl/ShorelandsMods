using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    // Link ids of GamePrintLink. The game's chat (ChatView.OnPointerClick) opens its player menu, whose Whisper sends,
    // for every link id that is not "item:..."; so the game must never handle one of these: ChatLinkClicks (Game)
    // takes every click on an id with Prefix before the game's handler runs. Ids are random, never made from mod text,
    // so no mod, player line or server line can name a live one.
    internal static class ChatLinkId
    {
        public const string Prefix = "smods:";
        const int HexDigits = 16;
        static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        public static bool IsHost(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);

        public static string New()
        {
            var bytes = new byte[HexDigits / 2];
            Rng.GetBytes(bytes);
            var c = new char[HexDigits];
            for (int i = 0; i < bytes.Length; i++)
            {
                c[2 * i] = "0123456789abcdef"[bytes[i] >> 4];
                c[2 * i + 1] = "0123456789abcdef"[bytes[i] & 15];
            }
            return Prefix + new string(c);
        }

        // Exactly what New makes.
        public static bool WellFormed(string id)
        {
            if (!IsHost(id) || id.Length != Prefix.Length + HexDigits)
                return false;
            for (int i = Prefix.Length; i < id.Length; i++)
                if (!((id[i] >= '0' && id[i] <= '9') || (id[i] >= 'a' && id[i] <= 'f')))
                    return false;
            return true;
        }
    }

    // The live links of all mods: each mod keeps its newest Limits.ChatLinksPerMod; a mod's go when it stops.
    internal sealed class ChatLinks
    {
        sealed class Link
        {
            public Mod Mod;
            public LuaFunction Fn;
            public string What;
        }

        readonly Dictionary<string, Link> live = new Dictionary<string, Link>();
        readonly Dictionary<Mod, Queue<string>> byMod = new Dictionary<Mod, Queue<string>>();

        public string Add(Mod mod, LuaFunction fn, string label)
        {
            if (!byMod.TryGetValue(mod, out var ids))
                byMod[mod] = ids = new Queue<string>();
            while (ids.Count >= Limits.ChatLinksPerMod)
                live.Remove(ids.Dequeue());
            string id;
            do id = ChatLinkId.New();
            while (live.ContainsKey(id));
            live[id] = new Link
            {
                Mod = mod,
                Fn = fn,
                What = "the link \"" + label + "\"",
            };
            ids.Enqueue(id);
            return id;
        }

        public void Drop(Mod mod)
        {
            if (!byMod.TryGetValue(mod, out var ids))
                return;
            foreach (var id in ids)
                live.Remove(id);
            byMod.Remove(mod);
        }

        public bool Find(string id, out Mod mod, out LuaFunction fn, out string what)
        {
            mod = null;
            fn = null;
            what = null;
            if (id == null || !live.TryGetValue(id, out var l))
                return false;
            mod = l.Mod;
            fn = l.Fn;
            what = l.What;
            return true;
        }

        public int Count(Mod mod) => byMod.TryGetValue(mod, out var ids) ? ids.Count : 0;

        public IEnumerable<LuaFunction> Held(Mod mod)
        {
            if (byMod.TryGetValue(mod, out var ids))
                foreach (var id in ids)
                    yield return live[id].Fn;
        }
    }
}
