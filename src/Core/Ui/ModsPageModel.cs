using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core.Ui
{
    internal enum ModState
    {
        On,
        Off,
        Failed,
    }

    // What the Mods page says about a mod and the list as a whole, in plain words.
    internal static class ModsPageModel
    {
        public static ModState State(Mod mod) =>
            mod.Status == ModStatus.Loaded ? ModState.On
            : mod.Status == ModStatus.Off ? ModState.Off
            : ModState.Failed;

        public static string StateWord(Mod mod) =>
            mod.Status == ModStatus.Loaded ? "On"
            : mod.Status == ModStatus.Off ? "Off"
            : mod.Status == ModStatus.Skipped ? "Not loaded"
            : "Failed";

        // The sentence under the name: why it failed or is not loaded; nothing for a mod that is on or off.
        public static string Problem(Mod mod)
        {
            if (mod.Status != ModStatus.Failed && mod.Status != ModStatus.Skipped)
                return null;
            string r = (mod.Reason ?? "unknown reason").Trim();
            if (!r.EndsWith(".") && !r.EndsWith("?") && !r.EndsWith("!"))
                r += ".";
            return mod.Status == ModStatus.Failed ? r + " It stays off until its files change or you reload." : r;
        }

        public static string Summary(IEnumerable<Mod> mods)
        {
            var list = mods.ToList();
            int on = list.Count(m => State(m) == ModState.On),
                off = list.Count(m => State(m) == ModState.Off),
                failed = list.Count(m => State(m) == ModState.Failed);
            if (list.Count == 0)
                return "No mods installed";
            var parts = new List<string> { on + " on" };
            if (off > 0)
                parts.Add(off + " off");
            if (failed > 0)
                parts.Add(failed + " failed");
            return list.Count + (list.Count == 1 ? " mod: " : " mods: ") + string.Join(", ", parts);
        }

        public static string Byline(Mod mod)
        {
            var m = mod.Manifest;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(m?.Version))
                parts.Add("version " + m.Version);
            if (!string.IsNullOrEmpty(m?.Author))
                parts.Add("by " + m.Author);
            if (!string.IsNullOrEmpty(m?.MadeForGame))
                parts.Add("made for " + m.MadeForGame);
            parts.Add("folder " + mod.Id);
            return string.Join(", ", parts);
        }

        public static string AssetSummary(IList<AssetLine> lines)
        {
            if (lines.Count == 0)
                return null;
            var parts = new List<string>();
            foreach (
                var (state, word) in new[]
                {
                    (AssetState.Applied, "applied"),
                    (AssetState.Waiting, "waiting"),
                    (AssetState.Ignored, "not used"),
                    (AssetState.Overridden, "overridden"),
                    (AssetState.Covered, "not shown"),
                }
            )
            {
                int n = lines.Count(l => l.State == state);
                if (n > 0)
                    parts.Add(n + " " + word);
            }
            return lines.Count + (lines.Count == 1 ? " file: " : " files: ") + string.Join(", ", parts);
        }

        // One report line as the page shows it: file, then what became of it.
        public static (string file, string what) AssetText(AssetLine l)
        {
            switch (l.State)
            {
                case AssetState.Applied:
                    return (l.File, l.Detail == null ? "applied" : "applied (" + l.Detail + ")");
                case AssetState.Overridden:
                    return (l.File, "overridden by " + l.Detail);
                case AssetState.Waiting:
                    return (l.File, "waiting: " + l.Detail);
                case AssetState.Covered:
                    return (l.File, "not shown: " + l.Detail);
                default:
                    return (l.File, "not used: " + l.Detail);
            }
        }
    }

    // The mods as the Mods page last drew them. A reload makes a new Mod, so a mod that reloaded counts as changed,
    // as does one that moved, failed or was switched on or off.
    internal sealed class ModsSeen
    {
        readonly List<(Mod mod, ModStatus status, string reason)> drawn =
            new List<(Mod mod, ModStatus status, string reason)>();

        public void Take(IReadOnlyList<Mod> mods)
        {
            drawn.Clear();
            foreach (var m in mods)
                drawn.Add((m, m.Status, m.Reason));
        }

        public bool Changed(IReadOnlyList<Mod> mods)
        {
            if (mods.Count != drawn.Count)
                return true;
            for (int i = 0; i < mods.Count; i++)
            {
                var (mod, status, reason) = drawn[i];
                if (!ReferenceEquals(mods[i], mod) || mods[i].Status != status || mods[i].Reason != reason)
                    return true;
            }
            return false;
        }
    }
}
