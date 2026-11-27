using System;
using System.IO;
using System.Xml;

namespace ShorelandsMods.Core
{
    internal sealed class ModManifest
    {
        // A mod.xml is one element; the Mods page shows these fields.
        public const int MaxBytes = 16 * 1024,
            MaxNameChars = 60,
            MaxShortChars = 40,
            MaxDescriptionChars = 1000;

        public string Name,
            Description = "",
            Version = "",
            Author = "",
            MadeForGame = "";

        // The lowest host version the mod works with; null when mod.xml does not say.
        public SemVer? NeedsHost;

        public static ModManifest Read(string path)
        {
            try
            {
                if (new FileInfo(path).Length > MaxBytes)
                    throw new ModProblem(
                        "mod.xml is over " + MaxBytes / 1024 + " KB; it needs only one <Mod ... /> element"
                    );
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new ModProblem("mod.xml cannot be read (" + e.Message + ")");
            }
        }

        // Throws ModProblem with a sentence for the player.
        public static ModManifest Parse(string xml)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxBytes,
            };
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                using (var reader = XmlReader.Create(new StringReader(xml), settings))
                    doc.Load(reader);
            }
            catch (XmlException e)
            {
                throw new ModProblem("mod.xml is not valid XML (line " + e.LineNumber + ": " + e.Message + ")");
            }
            var root = doc.DocumentElement;
            if (root == null || root.Name != "Mod")
                throw new ModProblem("mod.xml must have one <Mod ... /> element");
            var m = new ModManifest
            {
                Name = OneLine(root, "name", MaxNameChars),
                Description = Capped(root, "description", MaxDescriptionChars).Trim(),
                Version = OneLine(root, "version", MaxShortChars),
                Author = OneLine(root, "author", MaxNameChars),
                MadeForGame = OneLine(root, "made_for_game", MaxShortChars),
            };
            if (m.Name.Length == 0)
                throw new ModProblem("mod.xml has no name=\"...\"");
            if (root.HasAttribute("needs_host"))
            {
                string needs = root.GetAttribute("needs_host").Trim();
                if (!SemVer.TryParse(needs, out var v))
                    throw new ModProblem(
                        "mod.xml: needs_host must be a host version like 0.2.0 (numbers with dots between them), not \""
                            + TextEscape.Clip(TextEscape.Plain(needs), 40)
                            + "\""
                    );
                m.NeedsHost = v;
            }
            return m;
        }

        // A character reference like &#10; can put a line break into an attribute.
        static string OneLine(XmlElement e, string name, int max) => TextEscape.Plain(Capped(e, name, max)).Trim();

        static string Capped(XmlElement e, string name, int max)
        {
            string s = e.GetAttribute(name);
            if (s.Length > max)
                throw new ModProblem("mod.xml: " + name + " has " + s.Length + " characters; at most " + max);
            return s;
        }
    }

    internal static class ModId
    {
        public const int MaxLength = 40;

        // The folder name is the id: a lower case letter first, then lower case letters, digits and _.
        public static string Problem(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxLength)
                return "the folder name must be 1 to " + MaxLength + " characters";
            if (id[0] < 'a' || id[0] > 'z')
                return "the folder name must start with a lower case letter";
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                    return "the folder name may only use lower case letters, digits and _";
            if (ModPaths.IsReserved(id))
                return "the folder name " + id + " is a name Windows reserves for a device; rename the folder";
            return null;
        }
    }

    // A reason a mod cannot start, written for the player.
    internal sealed class ModProblem : Exception
    {
        public ModProblem(string sentence)
            : base(sentence) { }
    }
}
