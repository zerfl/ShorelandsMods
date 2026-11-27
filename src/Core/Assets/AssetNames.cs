using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core
{
    // The names a sound or cue file may have.
    internal static class AssetNames
    {
        public static readonly string[] Zones = { "town", "playerland", "holyland" };

        public static readonly string[] SoundEvents = new[]
        {
            "wallet_gain",
            "wallet_loss",
            "item_gained",
            "whisper",
            "chat",
            "rain_start",
            "rain_stop",
            "rain_loop",
            "ui_open",
            "ui_close",
        }
            .Concat(Zones.Select(z => "zone_" + z))
            .Concat(Zones.Select(z => "music_" + z))
            .Concat(Zones.Select(z => "music_" + z + "_night"))
            .ToArray();

        public static bool IsLoop(string sound) => sound == "rain_loop" || sound.StartsWith("music_");

        public static readonly string[] CueSlots =
        {
            "fishing_nibble",
            "fishing_bite",
            "fishing_tug",
            "fishing_spook",
            "fishing_leave",
        };

        public static readonly HashSet<string> SoundSet = new HashSet<string>(SoundEvents);
        public static readonly HashSet<string> CueSet = new HashSet<string>(CueSlots);

        // The music a zone plays: the night variant at night when the pack has it, else the day one.
        public static string Music(string zone, bool night, System.Func<string, bool> has)
        {
            if (zone == null || System.Array.IndexOf(Zones, zone) < 0)
                return null;
            if (night && has("music_" + zone + "_night"))
                return "music_" + zone + "_night";
            return has("music_" + zone) ? "music_" + zone : null;
        }
    }

    internal enum AssetState
    {
        Applied,
        Waiting,
        Ignored,
        Overridden,
        Covered,
    }

    // One asset file of a mod in its report (/mods <id>, the Mods page).
    internal sealed class AssetLine
    {
        public string File;
        public AssetState State;

        // Why, for everything but Applied (for Overridden: the winning mod's id).
        public string Detail;

        public AssetLine(string file, AssetState state, string detail = null)
        {
            File = file;
            State = state;
            Detail = detail;
        }

        public override string ToString()
        {
            switch (State)
            {
                case AssetState.Applied:
                    return File + ": applied" + (Detail != null ? " (" + Detail + ")" : "");
                case AssetState.Overridden:
                    return File + ": overridden by " + Detail;
                case AssetState.Waiting:
                    return File + ": waiting, " + Detail;
                case AssetState.Covered:
                    return File + ": not shown, " + Detail;
                default:
                    return File + ": ignored, " + Detail;
            }
        }
    }
}
