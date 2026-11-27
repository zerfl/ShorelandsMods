using System.Collections.Generic;

namespace ShorelandsMods.Core
{
    internal enum LogLevel
    {
        Info,
        Warning,
        Error,
    }

    // What the host needs from its surroundings: the game plugin in the game, a fake in the offline tests.
    internal interface IHostEnv
    {
        // <game>/mods and <game>/mods_data.
        string ModsDir { get; }
        string DataRoot { get; }

        // The host's version, shown by /mods and the Mods page.
        string HostVersion { get; }

        // Real seconds, monotonic.
        double Now { get; }

        void Log(LogLevel level, string text);

        // One local chat line of rich text; the host escapes everything a mod provided.
        void Chat(string richText);

        // The player's load order and switched-off mods (edited by the Mods page).
        IReadOnlyList<string> SavedOrder { get; }
        bool IsSwitchedOff(string id);
        void SaveOrder(IReadOnlyList<string> ids);
        void SetSwitchedOff(string id, bool switchedOff);

        ISettingsStore OpenSettings(string modId, IReadOnlyList<SettingDef> defs);
        bool IsKeyName(string name);
    }
}
