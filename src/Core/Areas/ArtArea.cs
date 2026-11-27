using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShorelandsMods.Core.Areas
{
    internal sealed class ArtImage
    {
        public string ModId,
            Rel,
            Full;
        public int W,
            H;
    }

    // What the game does with art (Game/Assets/ArtStage.cs; a fake offline).
    internal interface IArtStage
    {
        // The image now used for a key ("texture/<name>", "item/<id>", "koi/<id>"), or null to show the game's own.
        // Called in Update, once per changed key, and for all keys at Shutdown.
        void Set(string key, ArtImage image);

        // Size of the game's icon for an item or koi id. False with problem null while the game's tables are not
        // loaded (main menu); false with a sentence for an unknown id.
        bool IconSize(AssetKind kind, int id, out int w, out int h, out string problem);

        // The report line of a key this image wins (applied, waiting for the texture, covered by another plugin).
        AssetLine Describe(string key, ArtImage image);
    }

    // art/: textures by name, item and koi icons by id. Claims go through Overrides (last mod wins); the stage applies
    // the winners. Also keeps the names catalog behind _names.txt.
    internal sealed class ArtArea : Area
    {
        sealed class State
        {
            public readonly List<AssetLine> Ignored = new List<AssetLine>();
        }

        readonly IArtStage stage;
        Overrides<ArtImage> claims;
        public readonly NamesCatalog Names = new NamesCatalog();
        double namesDue;
        string namesFile;

        public ArtArea(IArtStage stage)
        {
            this.stage = stage;
        }

        public override string Name => "Art";

        protected internal override void Register(AreaSetup s) { }

        protected internal override void HostStarted()
        {
            claims = new Overrides<ArtImage>(Host.OrderOf);
            namesFile = Path.Combine(Host.Env.ModsDir, "_names.txt");
            if (File.Exists(namesFile))
                Names.Merge(ModFiles.ReadText(Host.Env.ModsDir, namesFile, NamesBytes, out _) ?? "");
            WriteNames();
        }

        public static string KeyOf(AssetKind kind, string name) =>
            (
                kind == AssetKind.Texture ? "texture/"
                : kind == AssetKind.Item ? "item/"
                : "koi/"
            ) + name;

        protected internal override void ModStarted(Mod mod)
        {
            var st = mod.Get<State>();
            long pixels = 0;
            foreach (var f in AssetScan.Of(mod).Files)
            {
                if (f.Kind != AssetKind.Texture && f.Kind != AssetKind.Item && f.Kind != AssetKind.Koi)
                    continue;
                if (!FileHeaders.Png(f.Full, out int w, out int h, out string problem))
                {
                    st.Ignored.Add(new AssetLine(f.Rel, AssetState.Ignored, problem));
                    continue;
                }
                if (pixels + (long)w * h > Limits.ArtPixelsPerMod)
                {
                    st.Ignored.Add(
                        new AssetLine(
                            f.Rel,
                            AssetState.Ignored,
                            "the mod's art would be over "
                                + Limits.ArtPixelsPerMod / 1_000_000
                                + " million pixels together"
                        )
                    );
                    continue;
                }
                var image = new ArtImage
                {
                    ModId = mod.Id,
                    Rel = f.Rel,
                    Full = f.Full,
                    W = w,
                    H = h,
                };
                string why = Check(f.Kind, f.Key, image);
                if (why != null)
                    st.Ignored.Add(new AssetLine(f.Rel, AssetState.Ignored, why));
                else
                {
                    claims.Add(KeyOf(f.Kind, f.Key), mod.Id, image);
                    pixels += (long)w * h;
                }
            }
        }

        // Why an image cannot be used for its key with what is known now, or null.
        string Check(AssetKind kind, string name, ArtImage image)
        {
            if (kind == AssetKind.Texture)
            {
                if (!Names.KnowsTexture(name) || Names.SizesOf(name).Contains((image.W, image.H)))
                    return null;
                return "it is "
                    + image.W
                    + "x"
                    + image.H
                    + " pixels, the game's "
                    + name
                    + " is "
                    + string.Join(" or ", Names.SizesOf(name).Select(s => s.W + "x" + s.H));
            }
            if (!stage.IconSize(kind, int.Parse(name), out int w, out int h, out string problem))
                return problem;
            if (w == image.W && h == image.H)
                return null;
            return "it is "
                + image.W
                + "x"
                + image.H
                + " pixels, the game's icon for "
                + (kind == AssetKind.Item ? "item " : "koi ")
                + name
                + " is "
                + w
                + "x"
                + h;
        }

        // The stage saw a texture (name and size); new names may make waiting claims wrong.
        public void TextureSeen(string name, int w, int h)
        {
            if (Names.Seen(name, w, h))
                Revalidate(k => k == "texture/" + name);
        }

        // The game's item and koi tables became available.
        public void IconsKnown() => Revalidate(k => k.StartsWith("item/") || k.StartsWith("koi/"));

        void Revalidate(Func<string, bool> which)
        {
            foreach (var key in claims.Keys.Where(which).ToList())
            foreach (var mod in Host.Mods)
            {
                var image = claims.ValueOf(key, mod.Id);
                if (image == null)
                    continue;
                int slash = key.IndexOf('/');
                var kind =
                    key[0] == 't' ? AssetKind.Texture
                    : key[0] == 'i' ? AssetKind.Item
                    : AssetKind.Koi;
                string why = Check(kind, key.Substring(slash + 1), image);
                if (why != null)
                    Reject(key, mod.Id, why);
            }
        }

        // The stage found an image unusable after all (it did not decode, it does not fit the texture).
        public void Reject(string key, string modId, string why)
        {
            var mod = Host.Find(modId);
            var image = claims.ValueOf(key, modId);
            if (mod == null || image == null)
                return;
            claims.Remove(key, modId);
            mod.Get<State>().Ignored.Add(new AssetLine(image.Rel, AssetState.Ignored, why));
            Host.Notice(mod, image.Rel + " is not used: " + why + ".");
        }

        protected internal override void ModStopping(Mod mod) => claims.RemoveMod(mod.Id);

        protected internal override void Update(double now)
        {
            if (claims.Dirty.Count > 0)
                foreach (var key in claims.TakeDirty())
                    stage.Set(key, claims.WinnerValue(key));
            if (Names.Changed && now >= namesDue)
            {
                namesDue = now + 5;
                WriteNames();
            }
        }

        protected internal override void Shutdown()
        {
            foreach (var key in claims.TakeDirty())
                stage.Set(key, claims.WinnerValue(key));
            if (Names.Changed)
                WriteNames();
        }

        const long NamesBytes = 4 << 20;
        bool namesFailed;

        void WriteNames()
        {
            string text = Names.ToText();
            try
            {
                if (File.Exists(namesFile) && ModFiles.ReadText(Host.Env.ModsDir, namesFile, NamesBytes, out _) == text)
                    return;
                ModFiles.CheckWrite(Host.Env.ModsDir, namesFile);
                File.WriteAllText(namesFile, text);
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                if (!namesFailed)
                    Host.Env.Log(LogLevel.Warning, "_names.txt is not written: " + e.Message);
                namesFailed = true;
            }
        }

        protected internal override IEnumerable<AssetLine> AssetLines(Mod mod)
        {
            var lines = new List<AssetLine>();
            foreach (var key in claims.KeysOf(mod.Id))
            {
                var mine = claims.ValueOf(key, mod.Id);
                var win = claims.Winner(key);
                lines.Add(
                    win.ModId == mod.Id
                        ? stage.Describe(key, mine)
                        : new AssetLine(mine.Rel, AssetState.Overridden, win.ModId)
                );
            }
            var st = mod.Find<State>();
            if (st != null)
                lines.AddRange(st.Ignored);
            return lines;
        }

        // The winning image of a key (for the stage's own bookkeeping and tests).
        public ArtImage Winner(string key) => claims.WinnerValue(key);
    }
}
