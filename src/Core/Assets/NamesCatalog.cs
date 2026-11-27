using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ShorelandsMods.Core
{
    // Everything an asset file can be named after, written to <game>/mods/_names.txt. Textures load over time, so
    // the file keeps the union of every session of the same game version.
    internal sealed class NamesCatalog
    {
        public string GameVersion = "";
        readonly Dictionary<string, SortedSet<long>> textures = new Dictionary<string, SortedSet<long>>(
            StringComparer.Ordinal
        );
        public readonly SortedDictionary<int, (string Name, int W, int H)> Items =
            new SortedDictionary<int, (string, int, int)>();
        public readonly SortedDictionary<int, string> Koi = new SortedDictionary<int, string>();

        // Textures item icons are cut from, with those items: a file by the texture's name changes the same picture,
        // but not in chat links, so _names.txt points to art/items/ there.
        public readonly Dictionary<string, SortedSet<int>> IconTextures = new Dictionary<string, SortedSet<int>>(
            StringComparer.Ordinal
        );
        static readonly Regex IconNote = new Regex(@"^the icons? of items? (?<ids>[\d, ]+)");
        public int KoiW,
            KoiH;

        // The koi whose icon a mod can still change while another plugin draws its own icons for the others; null
        // while no other plugin does.
        public List<int> KoiShown;

        // Set when something new was learned since the last ToText.
        public bool Changed;

        static long Size(int w, int h) => ((long)w << 32) | (uint)h;

        // Adds a texture size; true if it is new.
        public bool Seen(string name, int w, int h)
        {
            if (!textures.TryGetValue(name, out var sizes))
                textures[name] = sizes = new SortedSet<long>();
            if (!sizes.Add(Size(w, h)))
                return false;
            Changed = true;
            return true;
        }

        public bool KnowsTexture(string name) => textures.ContainsKey(name);

        public IEnumerable<(int W, int H)> SizesOf(string name) =>
            textures.TryGetValue(name, out var s)
                ? s.Select(v => ((int)(v >> 32), (int)(v & 0xFFFFFFFF)))
                : Enumerable.Empty<(int, int)>();

        public IEnumerable<string> TextureNames => textures.Keys;

        public void IconFrom(string texture, int item)
        {
            if (string.IsNullOrEmpty(texture))
                return;
            if (!IconTextures.TryGetValue(texture, out var ids))
                IconTextures[texture] = ids = new SortedSet<int>();
            if (ids.Add(item))
                Changed = true;
        }

        // "the icon of item 100 (Hoe): use art/items/100.png"
        string IconNoteOf(string texture)
        {
            if (!IconTextures.TryGetValue(texture, out var ids) || ids.Count == 0)
                return null;
            if (ids.Count > 1)
                return "the icons of items " + string.Join(", ", ids) + ": use art/items/<id>.png";
            int id = ids.Min;
            string name =
                Items.TryGetValue(id, out var item) && item.Name.Length > 0 ? " (" + OneLine(item.Name) + ")" : "";
            return "the icon of item " + id + name + ": use art/items/" + id + ".png";
        }

        public string ToText()
        {
            Changed = false;
            var sb = new StringBuilder();
            sb.Append("-- ShorelandsMods: the names asset files use. Written by the host; do not edit.\n");
            sb.Append("-- Game version ").Append(GameVersion).Append(".\n\n");
            sb.Append(
                "[textures] art/textures/<name>.png, the same size as the texture. Every game texture seen so far\n"
            );
            sb.Append("-- (more appear as you play; a name listed twice has two sizes). name<TAB>width x height\n");
            sb.Append(
                "-- A note after the size names the items whose icons are cut from that texture. Replace an icon in\n"
            );
            sb.Append(
                "-- art/items/<id>.png: that also reaches item links in chat (a texture file does not) and wins.\n"
            );
            foreach (var kv in textures.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                string note = IconNoteOf(kv.Key);
                foreach (var (w, h) in SizesOf(kv.Key))
                {
                    sb.Append(kv.Key).Append('\t').Append(w).Append('x').Append(h);
                    if (note != null)
                        sb.Append('\t').Append(note);
                    sb.Append('\n');
                }
            }
            sb.Append("\n[items] art/items/<id>.png, the same size as the icon. id<TAB>width x height<TAB>name\n");
            if (Items.Count == 0)
                sb.Append("-- (listed once you have been in the world)\n");
            foreach (var kv in Items)
                sb.Append(kv.Key)
                    .Append('\t')
                    .Append(kv.Value.W)
                    .Append('x')
                    .Append(kv.Value.H)
                    .Append('\t')
                    .Append(OneLine(kv.Value.Name))
                    .Append('\n');
            sb.Append("\n[koi] art/koi/<id>.png, ")
                .Append(KoiW > 0 ? KoiW + "x" + KoiH + " pixels" : "the size of the game's koi icon")
                .Append(". id<TAB>name\n");
            if (Koi.Count == 0)
                sb.Append("-- (listed once you have been in the world)\n");
            if (KoiShown != null)
                sb.Append("-- another plugin draws its own icons for the other koi; a file in art/koi/ ")
                    .Append("shows only for: ")
                    .Append(KoiShown.Count == 0 ? "none" : string.Join(", ", KoiShown))
                    .Append('\n');
            foreach (var kv in Koi)
                sb.Append(kv.Key).Append('\t').Append(OneLine(kv.Value)).Append('\n');
            sb.Append("\n[sounds] sounds/<name>.ogg or .wav\n");
            foreach (var s in AssetNames.SoundEvents)
                sb.Append(s).Append('\n');
            sb.Append("\n[cues] cues/<slot>.png (a strip of frames) and/or cues/<slot>.ogg or .wav. ")
                .Append("slot<TAB>what the game draws then\n");
            foreach (var s in AssetNames.CueSlots)
                sb.Append(s).Append('\t').Append(CueRules.GameDraws(s)).Append('\n');
            return sb.ToString();
        }

        static string OneLine(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

        // Reads an earlier file of the same game version; anything else is ignored.
        public void Merge(string text)
        {
            string section = null;
            bool sameVersion = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("-- Game version "))
                    sameVersion = line.Substring(16).TrimEnd('.') == GameVersion;
                if (line.StartsWith("["))
                {
                    section = line.Substring(1, Math.Max(0, line.IndexOf(']') - 1));
                    continue;
                }
                if (!sameVersion || line.Length == 0 || line.StartsWith("--"))
                    continue;
                var f = line.Split('\t');
                if (section == "textures" && (f.Length == 2 || f.Length == 3) && ParseSize(f[1], out int w, out int h))
                {
                    Seen(f[0], w, h);
                    var note = f.Length == 3 ? IconNote.Match(f[2]) : Match.Empty;
                    if (note.Success)
                        foreach (
                            var id in note.Groups["ids"]
                                .Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        )
                            if (int.TryParse(id, out int item))
                                IconFrom(f[0], item);
                }
                else if (
                    section == "items"
                    && f.Length == 3
                    && int.TryParse(f[0], out int id)
                    && ParseSize(f[1], out int iw, out int ih)
                    && !Items.ContainsKey(id)
                )
                    Items[id] = (f[2], iw, ih);
                else if (section == "koi" && f.Length == 2 && int.TryParse(f[0], out int kid) && !Koi.ContainsKey(kid))
                    Koi[kid] = f[1];
            }
            Changed = false;
        }

        static bool ParseSize(string s, out int w, out int h)
        {
            w = h = 0;
            int x = s.IndexOf('x');
            return x > 0
                && int.TryParse(s.Substring(0, x), NumberStyles.None, CultureInfo.InvariantCulture, out w)
                && int.TryParse(s.Substring(x + 1), NumberStyles.None, CultureInfo.InvariantCulture, out h);
        }
    }
}
