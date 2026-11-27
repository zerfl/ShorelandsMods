using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using ShorelandsMods.Core;
using LogLevel = ShorelandsMods.Core.LogLevel;

namespace ShorelandsMods.Game
{
    sealed class GameEnv : IHostEnv
    {
        const int ChatBacklog = 30;
        readonly ManualLogSource log;
        readonly ConfigEntry<string> order,
            off;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Queue<string> chatWaiting = new Queue<string>();
        readonly string configDir;
        GameHook chat;

        public GameEnv(ManualLogSource log, ConfigFile config, string gameDir, string configDir)
        {
            this.log = log;
            this.configDir = configDir;
            ModsDir = Path.Combine(gameDir, "mods");
            DataRoot = Path.Combine(gameDir, "mods_data");
            order = config.Bind(
                "Mods",
                "Order",
                "",
                "Load order: mod ids separated by commas. Mods not listed load after these, alphabetically."
            );
            off = config.Bind("Mods", "Off", "", "Mods switched off: ids separated by commas.");
        }

        public string ModsDir { get; }
        public string DataRoot { get; }
        public string HostVersion => BuildInfo.Version;
        public double Now => clock.Elapsed.TotalSeconds;

        // The chat hook is registered by ChatArea; lines wait until chat exists (main menu) or the hook is known.
        public void UseChat(GameHook hook) => chat = hook;

        public void Log(LogLevel level, string text)
        {
            switch (level)
            {
                case LogLevel.Error:
                    log.LogError(text);
                    break;
                case LogLevel.Warning:
                    log.LogWarning(text);
                    break;
                default:
                    log.LogInfo(text);
                    break;
            }
        }

        public void Chat(string richText)
        {
            if (chatWaiting.Count >= ChatBacklog)
                chatWaiting.Dequeue();
            chatWaiting.Enqueue(richText);
            FlushChat();
        }

        // Every frame: prints waiting lines once the chat exists.
        public void FlushChat()
        {
            if (chatWaiting.Count == 0 || chat == null || !chat.Available || !ChatReady())
                return;
            while (chatWaiting.Count > 0)
                Print(chatWaiting.Dequeue());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool ChatReady() => ReworkedCopium.Chat.ChatView.Instance != null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Print(string line) => ReworkedCopium.Chat.ChatManager.PrintInfoMessage(line);

        public IReadOnlyList<string> SavedOrder => LoadOrder.Split(order.Value);

        public bool IsSwitchedOff(string id) => LoadOrder.Split(off.Value).Contains(id);

        public void SaveOrder(IReadOnlyList<string> ids) => order.Value = string.Join(",", ids);

        // The change is saved at once; the host reloads the mod to apply it.
        public void SetSwitchedOff(string id, bool switchedOff)
        {
            var ids = LoadOrder.Split(off.Value).Where(x => x != id).ToList();
            if (switchedOff)
                ids.Add(id);
            off.Value = string.Join(",", ids);
        }

        public ISettingsStore OpenSettings(string modId, IReadOnlyList<SettingDef> defs) =>
            new CfgSettings(Path.Combine(configDir, "mods." + modId + ".cfg"), defs);

        public bool IsKeyName(string name) =>
            name.Length <= 32
            && name != "None"
            && Enum.TryParse<UnityEngine.InputSystem.Key>(name, false, out _)
            && !name.All(char.IsDigit);
    }

    // A text setting's length limit, so the cfg and ConfigurationManager hold the text the mod gets.
    sealed class AcceptableText : AcceptableValueBase
    {
        readonly SettingDef def;

        public AcceptableText(SettingDef def)
            : base(typeof(string))
        {
            this.def = def;
        }

        public override object Clamp(object value) => def.Fit(value);

        public override bool IsValid(object value) => def.Check(value) == null;

        public override string ToDescriptionString() => "# At most " + def.MaxLength + " characters";
    }

    // One mod's settings in BepInEx/config/mods.<id>.cfg. Changes made to the file outside the game are picked up by
    // its write time; ConfigFile.Reload raises SettingChanged for each entry whose value differs. The file is written
    // by Flush only (once after binding, then when the host says), never per Bind or per set.
    sealed class CfgSettings : ISettingsStore
    {
        readonly string path;
        readonly ConfigFile file;
        readonly Dictionary<string, ConfigEntryBase> entries = new Dictionary<string, ConfigEntryBase>();
        readonly Dictionary<ConfigEntryBase, string> ids = new Dictionary<ConfigEntryBase, string>();
        readonly Dictionary<string, object> pending = new Dictionary<string, object>();
        readonly HashSet<string> changed = new HashSet<string>();
        DateTime lastWrite;
        bool ownWrite;

        public CfgSettings(string path, IReadOnlyList<SettingDef> defs)
        {
            this.path = path;
            file = new ConfigFile(path, false) { SaveOnConfigSet = false };
            foreach (var d in defs)
            {
                string desc = d.Name + (d.Description.Length > 0 ? ": " + d.Description : "");
                ConfigEntryBase e;
                switch (d.Type)
                {
                    case SettingType.Boolean:
                        e = file.Bind("Settings", d.Id, (bool)d.Default, desc);
                        break;
                    case SettingType.Number:
                        e = file.Bind(
                            "Settings",
                            d.Id,
                            (double)d.Default,
                            new ConfigDescription(desc, new AcceptableValueRange<double>(d.Min, d.Max))
                        );
                        break;
                    case SettingType.Choice:
                        // BepInEx replaces a value not in the list with the list's first entry: the default.
                        e = file.Bind(
                            "Settings",
                            d.Id,
                            (string)d.Default,
                            new ConfigDescription(
                                desc,
                                new AcceptableValueList<string>(
                                    d.Choices.Select(c => c.Key).OrderBy(k => k == (string)d.Default ? 0 : 1).ToArray()
                                )
                            )
                        );
                        break;
                    case SettingType.Text:
                        e = file.Bind(
                            "Settings",
                            d.Id,
                            (string)d.Default,
                            new ConfigDescription(desc, new AcceptableText(d))
                        );
                        break;
                    default:
                        e = file.Bind("Settings", d.Id, (string)d.Default, desc + " (a key)");
                        break;
                }
                entries[d.Id] = e;
                ids[e] = d.Id;
            }
            file.SettingChanged += (s, a) =>
            {
                if (!ownWrite && ids.TryGetValue(a.ChangedSetting, out var id))
                    changed.Add(id);
            };
            Write();
        }

        DateTime WriteTime() => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;

        void Write()
        {
            file.Save();
            pending.Clear();
            lastWrite = WriteTime();
        }

        public object Get(SettingDef def) => entries[def.Id].BoxedValue;

        public void Set(SettingDef def, object value)
        {
            SetOwn(def.Id, value);
            pending[def.Id] = entries[def.Id].BoxedValue;
        }

        void SetOwn(string id, object value)
        {
            ownWrite = true;
            try
            {
                entries[id].BoxedValue = value;
            }
            finally
            {
                ownWrite = false;
            }
        }

        public bool Dirty => pending.Count > 0;

        public void Flush()
        {
            if (Dirty)
                Write();
        }

        // An edit of the file wins for the settings it changed; the host's unwritten values for the others are put
        // back (Reload reads every entry) and written later.
        public IList<string> ReloadIfChanged()
        {
            var t = WriteTime();
            // A deleted file (the mod folder is going away) is not a change.
            if (t == lastWrite || t == default)
                return Array.Empty<string>();
            changed.Clear();
            try
            {
                file.Reload();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // An editor still holds the file: read it on the next poll.
                return Array.Empty<string>();
            }
            lastWrite = t;
            foreach (var id in changed)
                pending.Remove(id);
            foreach (var kv in pending)
                SetOwn(kv.Key, kv.Value);
            return changed.ToList();
        }
    }
}
