using System;
using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core
{
    // Copies of the game's own art go to <game>/mods/_originals with the layout of a mod's art/ folder, so a file can
    // be copied straight across: textures/<name>.png, items/<id>.png, koi/<id>.png.
    internal static class Originals
    {
        public const string Folder = "_originals";
        const int MaxNameLength = 120;

        public static string Item(int id) => "items/" + id + ".png";

        public static string Koi(int id) => "koi/" + id + ".png";

        // The file for one size of a texture, or null with the reason no file can carry the name. A name the game
        // has in two sizes keeps its first (smallest) size in textures/<name>.png and puts the others in a folder per
        // size, textures/<W>x<H>/<name>.png, since one folder cannot hold two files of the same name.
        public static string Texture(string name, int w, int h, IEnumerable<(int W, int H)> sizes, out string why)
        {
            why = NameProblem(name);
            if (why != null)
                return null;
            var first = sizes.DefaultIfEmpty((w, h)).First();
            return first == (w, h) ? "textures/" + name + ".png" : "textures/" + w + "x" + h + "/" + name + ".png";
        }

        // Why a texture name cannot be a file name in art/textures/, or null.
        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "it has no name";
            if (name.Length > MaxNameLength)
                return "its name is longer than " + MaxNameLength + " characters";
            foreach (char c in name)
                if (char.IsControl(c) || "\\/:*?\"<>|".IndexOf(c) >= 0)
                    return "its name has the character "
                        + (char.IsControl(c) ? "(a control character)" : "" + c)
                        + ", which a file name cannot have";
            if (name.EndsWith(".") || name.EndsWith(" "))
                return "its name ends with a dot or a space, which Windows drops from file names";
            if (ModPaths.IsReserved(name.Split('.')[0]))
                return "its name is one Windows reserves";
            return null;
        }

        // Every loaded texture size as files: textures whose names differ only in case would be the same file on
        // Windows, so only the first (ordinal order) is kept; a mod cannot replace both either.
        public static List<(string Name, int W, int H, string File)> Plan(
            IEnumerable<(string Name, int W, int H)> loaded,
            NamesCatalog names,
            List<(string Name, string Why)> skipped
        )
        {
            var plan = new List<(string, int, int, string)>();
            var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in loaded.Distinct().OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.W))
            {
                if (taken.TryGetValue(t.Name, out string other) && other != t.Name)
                {
                    skipped.Add(
                        (t.Name, "it differs from " + other + " only in case; Windows keeps one file for both")
                    );
                    continue;
                }
                taken[t.Name] = t.Name;
                string file = Texture(t.Name, t.W, t.H, names.SizesOf(t.Name), out string why);
                if (file == null)
                    skipped.Add((t.Name, why));
                else
                    plan.Add((t.Name, t.W, t.H, file));
            }
            return plan;
        }
    }
}
