using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Game.Assets;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShorelandsMods.Game.Tools
{
    // Export of the game's original art: the pixels are read back from the GPU (the game's textures are not readable
    // on the CPU), from the host's backup while a mod replaces the texture, and written as PNG files in the layout of
    // a mod's art/ folder (Core/Assets/Originals.cs).
    sealed class AuthorTools : IAuthorTools
    {
        const int Batch = 32;

        readonly ArtStage art;
        readonly Func<ArtArea> names;
        readonly string dir;

        public AuthorTools(ArtStage art, Func<ArtArea> names, string modsDir)
        {
            this.art = art;
            this.names = names;
            dir = Path.Combine(modsDir, Originals.Folder);
        }

        public bool InWorld => Inspector.InWorld();
        public bool Inspecting => Inspector.On;

        public void SetInspect(bool on) => Inspector.Set(on);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Lua.LuaTable ItemTable(int id, int count, int meta) =>
            ReworkedCopium.Database.Instance != null && ReworkedCopium.Database.GetItem(id) != null
                ? Areas.InventoryArea.ItemTable(id, count, meta)
                : null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public double[] Wallet()
        {
            var w = ReworkedCopium.Player.PlayerWallet.LocalWallet;
            return w != null ? new double[] { w.Currency, w.Citb } : null;
        }

        sealed class Job
        {
            public Texture2D Source;
            public RectInt Area;
            public string Rel;
        }

        public ExportResult ExportTexture(string name)
        {
            var r = new ExportResult();
            var jobs = new List<Job>();
            var catalog = names().Names;
            foreach (var t in art.Loaded(name).GroupBy(t => (t.width, t.height)).Select(g => g.First()))
            {
                string rel = Originals.Texture(name, t.width, t.height, catalog.SizesOf(name), out string why);
                if (rel == null)
                    r.Problems.Add(name + ": " + why);
                else if (!ArtStage.Replaceable(t))
                    r.Problems.Add(
                        name + ": its format " + t.format + " cannot be replaced by a mod, so it is not saved"
                    );
                else
                    jobs.Add(
                        new Job
                        {
                            Source = t,
                            Area = new RectInt(0, 0, t.width, t.height),
                            Rel = rel,
                        }
                    );
            }
            if (jobs.Count == 0 && r.Problems.Count == 0)
                r.Problems.Add(name + " is not loaded right now; go where the game shows it and try again");
            Run(jobs, r);
            return r;
        }

        public ExportResult ExportItem(int id) =>
            Icon(() => ArtStage.ItemIcon(id, out var p) ?? Fail(p), Originals.Item(id));

        public ExportResult ExportKoi(int id) =>
            Icon(() => ArtStage.KoiIcon(id, out var p) ?? Fail(p), Originals.Koi(id));

        static Sprite Fail(string problem) => throw new ArgumentException(problem ?? "the game has no icon for it");

        ExportResult Icon(Func<Sprite> icon, string rel)
        {
            var r = new ExportResult();
            if (!art.IconsReady)
            {
                r.Problems.Add("icons can be saved once you are in the world");
                return r;
            }
            try
            {
                var job = IconJob(icon(), rel, out string why);
                if (job == null)
                    r.Problems.Add(why);
                else
                    Run(new List<Job> { job }, r);
            }
            catch (ArgumentException e)
            {
                r.Problems.Add(e.Message);
            }
            return r;
        }

        static Job IconJob(Sprite s, string rel, out string why)
        {
            why = null;
            var tex = s.texture;
            if (tex == null || !ArtStage.Replaceable(tex) || tex.mipmapCount > 1)
            {
                why = "its icon cannot be replaced by a mod, so it is not saved";
                return null;
            }
            var tr = s.textureRect;
            int w = (int)s.rect.width,
                h = (int)s.rect.height,
                x = Mathf.Clamp((int)tr.x, 0, tex.width - w),
                y = Mathf.Clamp((int)tr.y, 0, tex.height - h);
            return new Job
            {
                Source = tex,
                Area = new RectInt(x, y, w, h),
                Rel = rel,
            };
        }

        public ExportResult ExportAll()
        {
            var r = new ExportResult();
            var catalog = names().Names;
            var skipped = new List<(string Name, string Why)>();
            var loaded = art.LoadedTextures.Where(ArtStage.Replaceable).ToList();
            var jobs = new List<Job>();
            foreach (var p in Originals.Plan(loaded.Select(t => (t.name, t.width, t.height)), catalog, skipped))
            {
                var t = loaded.First(x => x.name == p.Name && x.width == p.W && x.height == p.H);
                jobs.Add(
                    new Job
                    {
                        Source = t,
                        Area = new RectInt(0, 0, p.W, p.H),
                        Rel = p.File,
                    }
                );
            }
            r.Problems.AddRange(skipped.Select(s => s.Name + ": " + s.Why));
            if (art.IconsReady)
                IconJobs(catalog, jobs);
            Run(jobs, r);
            return r;
        }

        // Every item icon, and the koi icons a mod can show (all of them unless another plugin draws some).
        [MethodImpl(MethodImplOptions.NoInlining)]
        void IconJobs(NamesCatalog catalog, List<Job> jobs)
        {
            foreach (var id in catalog.Items.Keys)
                if (ArtStage.ItemIcon(id, out _) is Sprite s && IconJob(s, Originals.Item(id), out _) is Job j)
                    jobs.Add(j);
            foreach (var id in catalog.KoiShown ?? catalog.Koi.Keys.ToList())
                if (ArtStage.KoiIcon(id, out _) is Sprite s && IconJob(s, Originals.Koi(id), out _) is Job j)
                    jobs.Add(j);
        }

        // Reads back in batches (one GPU wait per batch) and writes each file.
        void Run(List<Job> jobs, ExportResult r)
        {
            for (int i = 0; i < jobs.Count; i += Batch)
            {
                var batch = jobs.Skip(i).Take(Batch).ToList();
                var requests = batch
                    .Select(j =>
                        AsyncGPUReadback.Request(
                            art.Original(j.Source),
                            0,
                            j.Area.x,
                            j.Area.width,
                            j.Area.y,
                            j.Area.height,
                            0,
                            1,
                            TextureFormat.RGBA32
                        )
                    )
                    .ToList();
                for (int k = 0; k < batch.Count; k++)
                {
                    var req = requests[k];
                    req.WaitForCompletion();
                    if (req.hasError)
                    {
                        r.Problems.Add(batch[k].Rel + ": the game's pixels could not be read");
                        continue;
                    }
                    string why = Write(batch[k], req.GetData<byte>().ToArray(), out string full);
                    if (why != null)
                        r.Problems.Add(batch[k].Rel + ": " + why);
                    else
                        r.Written.Add(full);
                }
            }
        }

        string Write(Job j, byte[] raw, out string full)
        {
            full = Path.Combine(dir, j.Rel.Replace('/', Path.DirectorySeparatorChar));
            var png = new Texture2D(j.Area.width, j.Area.height, TextureFormat.RGBA32, false);
            try
            {
                png.LoadRawTextureData(raw);
                ModFiles.CheckWrite(Path.GetDirectoryName(dir), full);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                string tmp = full + ".tmp";
                File.WriteAllBytes(tmp, png.EncodeToPNG());
                if (File.Exists(full))
                    File.Delete(full);
                File.Move(tmp, full);
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return "it could not be written (" + e.Message + ")";
            }
            finally
            {
                UnityEngine.Object.Destroy(png);
            }
        }
    }
}
