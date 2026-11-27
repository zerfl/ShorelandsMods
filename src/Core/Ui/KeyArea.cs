using System;
using System.Collections.Generic;
using System.Linq;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Core.Ui
{
    internal interface IKeyInput
    {
        // Keys must not reach mods now: a text field or the chat has the focus, or a key is being rebound.
        bool Blocked { get; }

        // The key (an Input System key name) went down this frame.
        bool Pressed(string keyName);

        // Keys the host and the game act on outside text fields, with what they do ("the game's key for Interact").
        IReadOnlyList<(string key, string what)> Taken();
    }

    // OnKey: a mod is told when a key it declared as a "key" setting in settings.lua went down, by the setting's id.
    // It never learns about any other key.
    internal sealed class KeyArea : Area
    {
        public const double CheckSeconds = 0.5;

        readonly IKeyInput input;
        List<KeyClash.Group> clashes = new List<KeyClash.Group>();
        string clashStamp = "";

        // Clashes said in chat, with the mods in them: forgotten once the clash is gone while those mods are on.
        readonly Dictionary<string, string[]> told = new Dictionary<string, string[]>();
        double nextCheck;

        public KeyArea(IKeyInput input)
        {
            this.input = input;
        }

        public override string Name => "Keys";

        // Goes up whenever the set of clashes changes.
        public int ClashVersion { get; private set; }

        protected internal override void Register(AreaSetup s)
        {
            s.Callback(
                "OnKey( id:string )",
                "the key of one of the mod's type = \"key\" settings went down; id is the setting's id (the player "
                    + "may rebind it on the Mods page). Not while a text field or the chat has the focus or a key is "
                    + "being rebound; a mod never hears any other key. The Mods page and chat say when the key is also "
                    + "another mod's, inspect mode's or one of the game's"
            );
        }

        protected internal override void Update(double now)
        {
            if (now >= nextCheck)
            {
                nextCheck = now + CheckSeconds;
                CheckClashes();
            }
            if (input.Blocked)
                return;
            var mods = Host.Mods;
            List<(Mod, string)> fired = null;
            for (int m = 0; m < mods.Count; m++)
            {
                var mod = mods[m];
                var st = mod.Find<SettingsArea.State>();
                if (mod.Status != ModStatus.Loaded || st?.Defs == null || mod.Lua == null)
                    continue;
                foreach (var d in st.Defs)
                    if (d.Type == SettingType.Key && SettingsArea.Value(st, d) is string key && input.Pressed(key))
                        (fired = fired ?? new List<(Mod, string)>()).Add((mod, d.Id));
            }
            if (fired == null)
                return;
            foreach (var (mod, id) in fired)
                if (mod.Status == ModStatus.Loaded && mod.Lua != null && mod.Lua.HasFunction("OnKey"))
                    Host.CallByPlayer(mod, "OnKey", mod.Lua.Global("OnKey"), 1, new Args(id));
        }

        // What else reacts to the key of this setting, as a sentence for the Mods page; null when nothing does.
        public string ClashOf(Mod mod, SettingDef def)
        {
            foreach (var g in clashes)
            foreach (var u in g.Uses)
                if (u.ModId == mod.Id && u.SettingId == def.Id)
                    return KeyClash.Sentence(g, u);
            return null;
        }

        public bool HasClash(Mod mod) => clashes.Any(g => g.Uses.Any(u => u.ModId == mod.Id));

        // Also called by the Mods page right after it changed a key.
        public void CheckClashes()
        {
            var uses = new List<KeyClash.Use>();
            foreach (var mod in Host.Mods)
            {
                var st = mod.Find<SettingsArea.State>();
                if (mod.Status != ModStatus.Loaded || st?.Defs == null)
                    continue;
                foreach (var d in st.Defs)
                    if (d.Type == SettingType.Key && SettingsArea.Value(st, d) is string key)
                        uses.Add(new KeyClash.Use(mod.Id, mod.Name, d.Id, d.Name, key));
            }
            clashes = KeyClash.Find(uses, input.Taken());
            string stamp = string.Join("\n", clashes.Select(KeyClash.Signature));
            if (stamp != clashStamp)
            {
                clashStamp = stamp;
                ClashVersion++;
            }
            var now = new HashSet<string>();
            foreach (var g in clashes)
            {
                string sig = KeyClash.Signature(g);
                now.Add(sig);
                if (told.ContainsKey(sig))
                    continue;
                told[sig] = g.Uses.Select(u => u.ModId).Distinct().ToArray();
                var last = g.Uses[g.Uses.Count - 1];
                Host.Notice(
                    Host.Find(last.ModId),
                    KeyClash.Sentence(g, last)
                        + (g.Uses.Count > 1 ? " Change one" : " Change it")
                        + " on the Mods page."
                );
            }
            foreach (var sig in told.Keys.ToList())
                if (!now.Contains(sig) && told[sig].All(id => Host.Find(id)?.Status == ModStatus.Loaded))
                    told.Remove(sig);
        }
    }

    // Two things that react to one key press: key settings of mods, the host's inspect key, the game's own keys.
    internal static class KeyClash
    {
        public sealed class Use
        {
            public readonly string ModId,
                ModName,
                SettingId,
                SettingName,
                Key;

            public Use(string modId, string modName, string settingId, string settingName, string key)
            {
                ModId = modId;
                ModName = modName;
                SettingId = settingId;
                SettingName = settingName;
                Key = key;
            }
        }

        public sealed class Group
        {
            public string Key;

            // In load order.
            public readonly List<Use> Uses = new List<Use>();
            public readonly List<string> Taken = new List<string>();
        }

        // Every key a mod's setting shares with another setting, the host or the game.
        public static List<Group> Find(IReadOnlyList<Use> uses, IReadOnlyList<(string key, string what)> taken)
        {
            var groups = new List<Group>();
            foreach (var key in uses.Select(u => u.Key).Distinct())
            {
                var g = new Group { Key = key };
                g.Uses.AddRange(uses.Where(u => u.Key == key));
                g.Taken.AddRange(taken.Where(t => t.key == key).Select(t => t.what).Distinct());
                if (g.Uses.Count + g.Taken.Count > 1)
                    groups.Add(g);
            }
            return groups;
        }

        public static string Signature(Group g) =>
            g.Key
            + ":"
            + string.Join(",", g.Uses.Select(u => u.ModId + "." + u.SettingId).OrderBy(s => s, StringComparer.Ordinal))
            + ":"
            + string.Join(",", g.Taken);

        // "K is also Koi checklist's key for "Open the checklist": one press does both."
        public static string Sentence(Group g, Use use)
        {
            var others = g
                .Uses.Where(u => u != use)
                .Select(u =>
                    (u.ModId == use.ModId ? "this mod's" : u.ModName + "'s") + " key for \"" + u.SettingName + "\""
                )
                .Concat(g.Taken)
                .ToList();
            string list =
                others.Count == 1
                    ? others[0]
                    : string.Join(", ", others.Take(others.Count - 1)) + " and " + others[others.Count - 1];
            return g.Key + " is also " + list + ": one press does " + (others.Count == 1 ? "both." : "all of them.");
        }
    }
}
