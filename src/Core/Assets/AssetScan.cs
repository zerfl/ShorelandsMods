using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShorelandsMods.Core
{
    internal enum AssetKind
    {
        Texture,
        Item,
        Koi,
        Sound,
        CueImage,
        CueSound,
    }

    internal sealed class AssetFile
    {
        public AssetKind Kind;

        // Texture name, item or koi id, sound event or cue slot.
        public string Key;

        // "art/textures/hoe.png", as the author sees it.
        public string Rel;
        public string Full;
    }

    // The asset files of one mod folder by kind and name, and the files that are not used with the reason. Per mod
    // state (Of): scanned once per start.
    internal sealed class AssetScan
    {
        public const int MaxFiles = 2000;

        public readonly List<AssetFile> Files = new List<AssetFile>();
        public readonly List<AssetLine> Ignored = new List<AssetLine>();
        public string SoundsXml,
            CuesXml;
        bool done;

        public static AssetScan Of(Mod mod)
        {
            var s = mod.Get<AssetScan>();
            if (!s.done)
                s.Load(mod.Dir);
            return s;
        }

        public IEnumerable<AssetFile> OfKind(AssetKind kind) => Files.Where(f => f.Kind == kind);

        public void Load(string modDir)
        {
            done = true;
            var found = new List<(string rel, string full)>();
            foreach (var top in new[] { "art", "sounds", "cues" })
            {
                string dir = Path.Combine(modDir, top);
                try
                {
                    if (!Directory.Exists(dir))
                        continue;
                    if (ModFiles.IsLink(dir))
                    {
                        Ignore(top + "/", "it is " + ModFiles.LinkSentence);
                        continue;
                    }
                    int entries = 0;
                    foreach (var e in ModFiles.Walk(dir))
                    {
                        if (++entries == ModFiles.MaxEntries)
                            Ignore(top + "/", ModFiles.MaxEntries + " files and folders or more; the rest is not read");
                        bool link = ModFiles.IsLink(e);
                        if (link)
                            Ignore(
                                Rel(modDir, e.FullName) + (e is DirectoryInfo ? "/" : ""),
                                "it is " + ModFiles.LinkSentence
                            );
                        if (link || e is DirectoryInfo || NotAnAsset(e.Name))
                            continue;
                        if (found.Count >= MaxFiles)
                        {
                            Ignore(top + "/", "more than " + MaxFiles + " files; the rest is not read");
                            break;
                        }
                        found.Add((Rel(modDir, e.FullName), e.FullName));
                    }
                }
                catch (Exception e) when (ModFiles.IsFileError(e))
                {
                    Ignore(top + "/", "it cannot be read (" + e.Message + ")");
                }
            }
            foreach (var (rel, full) in found.OrderBy(f => f.rel, StringComparer.Ordinal))
                Classify(rel, full);
            // Two files for the same name (x.ogg and x.wav): the first in name order is used.
            foreach (var g in Files.GroupBy(f => f.Kind + "/" + f.Key).Where(g => g.Count() > 1).ToList())
            foreach (var extra in g.Skip(1))
            {
                Files.Remove(extra);
                Ignored.Add(
                    new AssetLine(extra.Rel, AssetState.Ignored, "the mod has " + g.First().Rel + " for that too")
                );
            }
        }

        // Notes for people (a README.txt per folder) and files Windows and macOS put into folders by themselves.
        static bool NotAnAsset(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".txt"
                || ext == ".md"
                || file.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
                || file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                || file == ".DS_Store";
        }

        static string Rel(string modDir, string full) => ModFiles.Rel(Path.GetFullPath(modDir), full);

        void Classify(string rel, string full)
        {
            var parts = rel.Split('/');
            string file = parts[parts.Length - 1];
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string stem = Path.GetFileNameWithoutExtension(file);
            switch (parts[0])
            {
                case "art":
                    if (parts.Length != 3 || (parts[1] != "textures" && parts[1] != "items" && parts[1] != "koi"))
                    {
                        Ignore(rel, "art/ holds only the folders textures/, items/ and koi/ (no deeper folders)");
                        return;
                    }
                    if (ext != ".png")
                    {
                        Ignore(rel, "only .png files are used in art/");
                        return;
                    }
                    if (parts[1] == "textures")
                    {
                        Add(AssetKind.Texture, stem, rel, full);
                        return;
                    }
                    if (!int.TryParse(stem, out int id) || id < 0 || id.ToString() != stem)
                    {
                        Ignore(
                            rel,
                            "the file name must be " + (parts[1] == "items" ? "an item" : "a koi") + " id, like 5.png"
                        );
                        return;
                    }
                    Add(parts[1] == "items" ? AssetKind.Item : AssetKind.Koi, stem, rel, full);
                    return;
                case "sounds":
                    if (parts.Length == 2 && file.Equals("sounds.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        SoundsXml = full;
                        return;
                    }
                    if (!Flat(rel, parts, "sounds/") || !AudioExt(rel, ext))
                        return;
                    if (!AssetNames.SoundSet.Contains(stem))
                    {
                        Ignore(rel, Unknown("sound event", stem, AssetNames.SoundEvents));
                        return;
                    }
                    Add(AssetKind.Sound, stem, rel, full);
                    return;
                default:
                    if (parts.Length == 2 && file.Equals("cues.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        CuesXml = full;
                        return;
                    }
                    if (!Flat(rel, parts, "cues/"))
                        return;
                    if (ext != ".png" && ext != ".ogg" && ext != ".wav")
                    {
                        Ignore(rel, "only .png, .ogg and .wav files are used in cues/");
                        return;
                    }
                    if (!AssetNames.CueSet.Contains(stem))
                    {
                        Ignore(rel, Unknown("cue slot", stem, AssetNames.CueSlots));
                        return;
                    }
                    Add(ext == ".png" ? AssetKind.CueImage : AssetKind.CueSound, stem, rel, full);
                    return;
            }
        }

        bool Flat(string rel, string[] parts, string folder)
        {
            if (parts.Length == 2)
                return true;
            Ignore(rel, folder + " has no subfolders");
            return false;
        }

        bool AudioExt(string rel, string ext)
        {
            if (ext == ".ogg" || ext == ".wav")
                return true;
            Ignore(rel, "only .ogg and .wav files play");
            return false;
        }

        public static string Unknown(string what, string name, IEnumerable<string> known)
        {
            string near = ErrorText.Suggest(name, known);
            return "there is no "
                + what
                + " called \""
                + TextEscape.Clip(name, 40)
                + "\""
                + (near != null ? " (did you mean " + near + "?)" : "");
        }

        void Add(AssetKind kind, string key, string rel, string full) =>
            Files.Add(
                new AssetFile
                {
                    Kind = kind,
                    Key = key,
                    Rel = rel,
                    Full = full,
                }
            );

        void Ignore(string rel, string why) => Ignored.Add(new AssetLine(rel, AssetState.Ignored, why));
    }
}
