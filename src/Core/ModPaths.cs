using System;
using System.IO;

namespace ShorelandsMods.Core
{
    internal static class ModPaths
    {
        public static readonly string[] LuaExtensions = { ".lua" };
        public static readonly string[] SoundExtensions = { ".ogg", ".wav" };

        // Longer than any path a mod needs, and checked before the path is looked at.
        public const int MaxPathChars = 200;

        // Names in the mods folder starting with _ are the host's (_api.txt, _names.txt, _originals/), never a mod.
        public static bool IsHostName(string name) => name.StartsWith("_", StringComparison.Ordinal);

        // A path a mod gave to dofile: relative to its folder ("files/util.lua"), or Noita's "mods/<id>/files/util.lua".
        // Returns the full path, or null with a sentence in error.
        public static string ResolveLua(
            string modDir,
            string modId,
            string path,
            out string relative,
            out string error
        ) => ResolveFile(modDir, modId, path, LuaExtensions, out relative, out error);

        // The same rules for any file a mod names (dofile, SoundPlay, icons), with the extensions it may have; no part of
        // the path below the mod folder may be a link.
        public static string ResolveFile(
            string modDir,
            string modId,
            string path,
            string[] extensions,
            out string relative,
            out string error
        )
        {
            relative = null;
            error = null;
            if (string.IsNullOrEmpty(path))
                return Fail("the path is empty", out error);
            if (path.Length > MaxPathChars)
                return Fail(
                    "\""
                        + TextEscape.Clip(path, 40)
                        + "\" is over "
                        + MaxPathChars
                        + " characters, too long for a path",
                    out error
                );
            string p = path.Replace('\\', '/');
            string noitaPrefix = "mods/" + modId + "/";
            if (p.StartsWith(noitaPrefix, StringComparison.OrdinalIgnoreCase))
                p = p.Substring(noitaPrefix.Length);
            foreach (char c in p)
                if (
                    char.IsControl(c)
                    || c == ':'
                    || c == '*'
                    || c == '?'
                    || c == '"'
                    || c == '<'
                    || c == '>'
                    || c == '|'
                )
                    return Fail("\"" + path + "\" has a character a file path cannot use here", out error);
            if (p.StartsWith("/"))
                return Fail("\"" + path + "\" must be relative to the mod folder (no leading /)", out error);
            foreach (var part in p.Split('/'))
                if (part == ".." || part == ".")
                    return Fail("\"" + path + "\" must stay inside the mod folder (no . or ..)", out error);
            bool known = false;
            foreach (var ext in extensions)
                known |= p.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
            if (!known)
                return Fail("\"" + path + "\" is not a " + string.Join(" or ", extensions) + " file", out error);
            string root = Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Fail("\"" + path + "\" must stay inside the mod folder", out error);
            string link = ModFiles.Problem(root, full);
            if (link != null)
                return Fail(link, out error);
            relative = p;
            return full;
        }

        static readonly string[] Reserved =
        {
            "con",
            "prn",
            "aux",
            "nul",
            "conin$",
            "conout$",
            "com0",
            "com1",
            "com2",
            "com3",
            "com4",
            "com5",
            "com6",
            "com7",
            "com8",
            "com9",
            "lpt0",
            "lpt1",
            "lpt2",
            "lpt3",
            "lpt4",
            "lpt5",
            "lpt6",
            "lpt7",
            "lpt8",
            "lpt9",
        };

        // con, nul, com1 and the like: Windows refuses files with these names, whatever their extension.
        public static bool IsReserved(string stem) => Array.IndexOf(Reserved, stem.ToLowerInvariant()) >= 0;

        // "currency" or "currency.txt" -> "currency.txt". Letters, digits, space, _ - and . only, at most 64 characters.
        public static string ExportFileName(string name, out string error)
        {
            error = null;
            string n = (name ?? "").Trim();
            if (n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            if (n.Length == 0 || n.Length > 64)
                return Fail("the file name must be 1 to 64 characters", out error);
            foreach (char c in n)
                if (
                    !(
                        (c >= 'a' && c <= 'z')
                        || (c >= 'A' && c <= 'Z')
                        || (c >= '0' && c <= '9')
                        || c == '_'
                        || c == '-'
                        || c == ' '
                        || c == '.'
                    )
                )
                    return Fail("\"" + name + "\" may only use letters, digits, space, _ - and .", out error);
            if (n[0] == '.' || n.EndsWith(".") || n.EndsWith(" "))
                return Fail("\"" + name + "\" may not start or end with . or a space", out error);
            string stem = n.Split('.')[0].ToLowerInvariant();
            if (IsReserved(stem))
                return Fail("\"" + name + "\" is a name Windows reserves", out error);
            return n + ".txt";
        }

        static string Fail(string sentence, out string error)
        {
            error = sentence;
            return null;
        }
    }
}
