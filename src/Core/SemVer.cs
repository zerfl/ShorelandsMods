using System;
using System.Globalization;

namespace ShorelandsMods.Core
{
    // A host version (the csproj's VersionPrefix, IHostEnv.HostVersion) or a mod's needs_host: MAJOR.MINOR.PATCH,
    // compared by number. "1.2" and "1" read as 1.2.0 and 1.0.0.
    internal readonly struct SemVer : IComparable<SemVer>
    {
        public readonly int Major,
            Minor,
            Patch;

        public SemVer(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public static bool TryParse(string s, out SemVer v)
        {
            v = default;
            if (string.IsNullOrEmpty(s) || s.Length > 32)
                return false;
            var parts = s.Split('.');
            if (parts.Length > 3)
                return false;
            var n = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0 || p.Length > 9)
                    return false;
                foreach (char c in p)
                    if (c < '0' || c > '9')
                        return false;
                n[i] = int.Parse(p, CultureInfo.InvariantCulture);
            }
            v = new SemVer(n[0], n[1], n[2]);
            return true;
        }

        public int CompareTo(SemVer o) =>
            Major != o.Major ? Major.CompareTo(o.Major)
            : Minor != o.Minor ? Minor.CompareTo(o.Minor)
            : Patch.CompareTo(o.Patch);

        public override string ToString() => Major + "." + Minor + "." + Patch;

        // Null when a mod needing `needs` runs on host `have`; else the sentence for the player.
        public static string Unmet(SemVer needs, SemVer have) =>
            needs.CompareTo(have) <= 0
                ? null
                : "it needs host " + needs + ", you have " + have + "; update ShorelandsMods to use it";
    }
}
