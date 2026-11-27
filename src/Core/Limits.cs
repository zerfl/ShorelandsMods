namespace ShorelandsMods.Core
{
    // Host-wide numbers. The guide quotes several of them; change both together.
    internal static class Limits
    {
        // The step limits stop a pure-Lua loop (about 71 million steps a second on this game's Mono, so 3 ms per
        // callback); the time limits catch loops that spend their time inside library functions. 5 ms switched off
        // a healthy mod once while the game started; 20 ms leaves room for such stalls.
        public static readonly Budget Callback = new Budget(20, 200_000);
        public static readonly Budget Init = new Budget(250, 10_000_000);
        public const int HookInterval = 1000;

        // Host function time not charged to a call's time budget, at most this much per call.
        public const double HostCreditMs = 60;
        public const long MemoryGrowthBytes = 64L << 20;

        // A mod's time over time: every call's real time (Lua, host functions, collections in it) plus the host's
        // work for it (drawing its HUD and windows, writing its files, measuring its data). 300 ms a second for 5 s
        // switches it off: a runaway goes in seconds, while the heaviest example (koi_checklist: 18-22 ms after
        // login, 8 ms per refresh) and a Lua search over 2,000 rows typed at 10 keys a second (850 ms) fit. Seconds
        // count real time, so a slow start-up frame adds no calls. Over half of it gives one notice.
        public const double WindowSeconds = 5;
        public const double WindowMs = 1500;

        // A mod's timers stop for this frame once it used this much of it; the rest run in the next frame.
        public const double FrameShareMs = 5;

        // Lua data a mod keeps between calls (LuaData): over this it is switched off, over half of it one notice.
        // The heaviest example keeps about 1.5 MB. Measured every KeptCheckSeconds, and in the next frame once its
        // calls grew the heap by KeptGrowthBytes; a collection in a call hides growth, so then within KeptMinSeconds.
        public const long KeptBytes = 32L << 20;
        public const double KeptCheckSeconds = 5;
        public const double KeptMinSeconds = 1;
        public const long KeptGrowthBytes = 4L << 20;

        // A call that saw a garbage collection gets its time budget again, at most this often.
        public const int GcGraceRounds = 2;

        public const double TickSeconds = 0.1;
        public const double ReloadDebounceSeconds = 0.5;
        public const int ScanIntervalMs = 500;

        // A Lua file (init.lua, settings.lua, dofile) over this is refused before it is read.
        public const long LuaFileBytes = 1L << 20;

        // Pixels of a mod's art/ images together (decoded and kept while the mod runs); all the game's textures are
        // about 7 million.
        public const long ArtPixelsPerMod = 16_000_000;

        // HudIcon and list row icons: PNG files of at most IconSide pixels a side, IconFilesPerMod different files.
        public const int IconSide = 256;
        public const int IconFilesPerMod = 256;

        public const int DataBytes = 256 * 1024;

        // data.json with indentation can be many times its 256 KB of JSON; past this it was not written by the host.
        public const long DataFileBytes = 16L << 20;

        // Writes a mod's calls cause are coalesced: data.json and a settings file at most this often, exports within
        // a per-mod allowance of writes and bytes a second.
        public const double DataWriteSeconds = 10;
        public const double SettingsWriteSeconds = 5;
        public const int ExportBytes = 64 * 1024;
        public const int ExportFiles = 32;
        public const double ExportMinInterval = 0.25;
        public const int ExportWritesPerSecond = 8;
        public const int ExportBytesPerSecond = 128 * 1024;
        public const int TimersPerMod = 64;
        public const int CommandsPerMod = 16;
        public const int KeySettingsPerMod = 8;
        public const int DataKeys = 1000;
        public const double TimerMinEvery = 0.05;
        public const int ChatLinesPerSecond = 10;
        public const int LogLinesPerSecond = 100;
        public const int ChatLineChars = 300;

        // GamePrintLink: links in one line, characters of a label, and the newest links of a mod that still work.
        public const int ChatLinksPerLine = 3;
        public const int ChatLinkLabelChars = 40;
        public const int ChatLinksPerMod = 64;
        public const int HostLineChars = 1000;
        public const int LogLineChars = 1000;
        public const int LogCharsPerSecond = 20_000;

        // Sounds: per file, per mod, and voices. Clips stay compressed in memory, so bytes are about file bytes.
        public const double SoundCooldownDefault = 0.05;
        public const double SoundSecondsOnce = 30;
        public const double SoundSecondsLoop = 600;
        public const double SoundSecondsCue = 5;
        public const long SoundFileBytes = 8L << 20;
        public const long SoundBytesPerMod = 32L << 20;
        public const int SoundClipsPerMod = 64;
        public const int SoundLoopsPerMod = 4;
        public const int SoundVoicesPerMod = 6;
        public const int SoundVoicesPack = 8;

        // A SoundPlay whose file is still loading after this long is dropped instead of playing late.
        public const double SoundLateSeconds = 1;
    }

    internal readonly struct Budget
    {
        public readonly double Ms;
        public readonly long Instructions;

        public Budget(double ms, long instructions)
        {
            Ms = ms;
            Instructions = instructions;
        }
    }
}
