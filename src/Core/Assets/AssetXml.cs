using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace ShorelandsMods.Core
{
    internal sealed class SoundOptions
    {
        public double Volume = 1,
            Cooldown = Limits.SoundCooldownDefault;
    }

    internal sealed class CueOptions
    {
        // Null: replace the game's own mark when the mod draws an image, accompany it otherwise.
        public bool? HideNative;
        public string Anchor = "mark";
        public int OffsetX,
            OffsetY,
            Scale = 1,
            Frames = 1;
        public double Fps = 8,
            Volume = 1;

        // Null: as long as the game's own cue lasts.
        public double? Seconds;
    }

    // sounds/sounds.xml and cues/cues.xml. A mistake costs only the attribute or entry it is in; every mistake is a
    // sentence for the mod's report. A file, its entries, their attributes and the report are capped before they are
    // read (a few dozen names exist to be set).
    internal static class AssetXml
    {
        public static readonly string[] Anchors = { "mark", "player", "bobber" };

        public const int MaxBytes = 64 * 1024,
            MaxEntries = 100,
            MaxAttributes = 16,
            MaxProblems = 20;

        // The file's text, or "" with a problem when it cannot be read or is over MaxBytes.
        public static string Read(string path, List<string> problems)
        {
            string file = Path.GetFileName(path);
            try
            {
                if (new FileInfo(path).Length > MaxBytes)
                {
                    problems.Add(file + " is over " + MaxBytes / 1024 + " KB, so it is not read");
                    return "";
                }
                return File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                problems.Add(file + " cannot be read (" + e.Message + ")");
                return "";
            }
        }

        // At most MaxProblems sentences, then one that says how many more there were.
        static void Cap(List<string> problems)
        {
            if (problems.Count <= MaxProblems)
                return;
            int more = problems.Count - MaxProblems;
            problems.RemoveRange(MaxProblems, more);
            problems.Add("and " + more + " more problems");
        }

        public static Dictionary<string, SoundOptions> Sounds(string text, List<string> problems)
        {
            var result = new Dictionary<string, SoundOptions>();
            foreach (var (e, line) in Entries(text, "Sounds", "Sound", "sounds/sounds.xml", problems))
            {
                string name = e.GetAttribute("name");
                if (!AssetNames.SoundSet.Contains(name))
                {
                    problems.Add(
                        At(line, "sounds.xml") + AssetScan.Unknown("sound event", name, AssetNames.SoundEvents)
                    );
                    continue;
                }
                var o = new SoundOptions();
                var r = new Reader(e, line, "sounds.xml", "Sound \"" + name + "\"", problems);
                o.Volume = r.Number("volume", o.Volume, 0, 1);
                o.Cooldown = r.Number("cooldown", o.Cooldown, 0, 60);
                r.Unknown("name", "volume", "cooldown");
                result[name] = o;
            }
            Cap(problems);
            return result;
        }

        public static Dictionary<string, CueOptions> Cues(string text, List<string> problems)
        {
            var result = new Dictionary<string, CueOptions>();
            foreach (var (e, line) in Entries(text, "Cues", "Cue", "cues/cues.xml", problems))
            {
                string slot = e.GetAttribute("slot");
                if (!AssetNames.CueSet.Contains(slot))
                {
                    problems.Add(At(line, "cues.xml") + AssetScan.Unknown("cue slot", slot, AssetNames.CueSlots));
                    continue;
                }
                var o = new CueOptions();
                var r = new Reader(e, line, "cues.xml", "Cue \"" + slot + "\"", problems);
                if (e.HasAttribute("hide_native"))
                    o.HideNative = r.Number("hide_native", 0, 0, 1) == 1;
                if (e.HasAttribute("anchor"))
                {
                    string a = e.GetAttribute("anchor");
                    if (Array.IndexOf(Anchors, a) >= 0)
                        o.Anchor = a;
                    else
                        r.Problem("anchor", "must be mark, player or bobber");
                }
                o.OffsetX = r.Integer("offset_x", 0, -64, 64);
                o.OffsetY = r.Integer("offset_y", 0, -64, 64);
                o.Scale = r.Integer("scale", 1, 1, 8);
                o.Frames = r.Integer("frames", 1, 1, 64);
                o.Fps = r.Number("fps", o.Fps, 0.5, 60);
                if (e.HasAttribute("seconds"))
                    o.Seconds = r.Number("seconds", 1, 0.05, 10);
                o.Volume = r.Number("volume", 1, 0, 1);
                r.Unknown(
                    "slot",
                    "hide_native",
                    "anchor",
                    "offset_x",
                    "offset_y",
                    "scale",
                    "frames",
                    "fps",
                    "seconds",
                    "volume"
                );
                result[slot] = o;
            }
            Cap(problems);
            return result;
        }

        static IEnumerable<(XmlElement, int)> Entries(
            string text,
            string root,
            string entry,
            string file,
            List<string> problems
        )
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxBytes,
            };
            var list = new List<(XmlElement, int)>();
            try
            {
                using (var reader = XmlReader.Create(new StringReader(text), settings))
                {
                    var info = (IXmlLineInfo)reader;
                    bool sawRoot = false;
                    var doc = new XmlDocument();
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                            continue;
                        if (reader.Depth == 0)
                        {
                            if (reader.Name != root)
                            {
                                problems.Add(
                                    file
                                        + " must start with <"
                                        + root
                                        + ">, not <"
                                        + TextEscape.Clip(reader.Name, 40)
                                        + ">"
                                );
                                return list;
                            }
                            sawRoot = true;
                            continue;
                        }
                        int line = info.LineNumber;
                        if (reader.Depth != 1 || reader.Name != entry)
                        {
                            problems.Add(
                                At(line, file)
                                    + "<"
                                    + TextEscape.Clip(reader.Name, 40)
                                    + "> is not used; entries are <"
                                    + entry
                                    + " ... />"
                            );
                            continue;
                        }
                        if (list.Count == MaxEntries)
                        {
                            problems.Add(
                                At(line, file) + "more than " + MaxEntries + " entries; the rest are not read"
                            );
                            return list;
                        }
                        if (reader.AttributeCount > MaxAttributes)
                        {
                            problems.Add(
                                At(line, file)
                                    + "an entry has more than "
                                    + MaxAttributes
                                    + " attributes; it is not read"
                            );
                            continue;
                        }
                        var e = doc.CreateElement(entry);
                        if (reader.MoveToFirstAttribute())
                        {
                            do e.SetAttribute(reader.Name, reader.Value);
                            while (reader.MoveToNextAttribute());
                            reader.MoveToElement();
                        }
                        list.Add((e, line));
                    }
                    if (!sawRoot)
                        problems.Add(file + " is empty");
                }
            }
            catch (XmlException ex)
            {
                problems.Add(file + " is not valid XML (line " + ex.LineNumber + ": " + ex.Message + ")");
            }
            return list;
        }

        static string At(int line, string file) => file + " line " + line + ": ";

        sealed class Reader
        {
            readonly XmlElement e;
            readonly string where;
            readonly List<string> problems;

            public Reader(XmlElement e, int line, string file, string what, List<string> problems)
            {
                this.e = e;
                where = At(line, file) + what;
                this.problems = problems;
            }

            public void Problem(string attr, string sentence) =>
                problems.Add(
                    where + ": " + attr + "=\"" + TextEscape.Clip(e.GetAttribute(attr), 40) + "\" " + sentence
                );

            public double Number(string attr, double fallback, double min, double max)
            {
                if (!e.HasAttribute(attr))
                    return fallback;
                if (
                    double.TryParse(e.GetAttribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    && d >= min
                    && d <= max
                )
                    return d;
                Problem(attr, "must be a number from " + LuaText.Num(min) + " to " + LuaText.Num(max));
                return fallback;
            }

            public int Integer(string attr, int fallback, int min, int max)
            {
                if (!e.HasAttribute(attr))
                    return fallback;
                if (int.TryParse(e.GetAttribute(attr), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    if (i >= min && i <= max)
                        return i;
                Problem(attr, "must be a whole number from " + min + " to " + max);
                return fallback;
            }

            public void Unknown(params string[] known)
            {
                foreach (XmlAttribute a in e.Attributes)
                    if (Array.IndexOf(known, a.Name) < 0)
                    {
                        string near = ErrorText.Suggest(a.Name, known);
                        problems.Add(
                            where
                                + ": there is no attribute "
                                + TextEscape.Clip(a.Name, 40)
                                + (near != null ? " (did you mean " + near + "?)" : "")
                        );
                    }
            }
        }
    }
}
