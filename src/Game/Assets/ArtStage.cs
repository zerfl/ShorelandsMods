using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ReworkedCopium;
using ReworkedCopium.Inventory;
using ReworkedCopium.Items;
using ReworkedCopium.Messages;
using ReworkedCopium.UI;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Assets
{
    // What art/ does in the game:
    // - textures by name and item icons by id: the PNG is copied on the GPU into the game's own texture (every user
    //   follows), over a GPU backup taken before the first write, so undo is exact. A texture's look is recomposed
    //   from the backup whenever one of its overrides changes: the full image by name, then item icon areas on top.
    // - koi icons by id: the game's koi icon is one sprite per colour, shared by many koi, so a postfix on
    //   OtherStone.GetIcon returns the mod's own sprite instead; chat links get a one-glyph TMP sprite asset.
    internal sealed class ArtStage : Area, IArtStage
    {
        public const double ScanSeconds = 3;
        const string ChatPrefix = "smods_koi_";

        static ArtStage instance;
        readonly Harmony harmony;
        ArtArea art;
        GameHook icons,
            koiHook,
            chatHook;

        sealed class Img
        {
            public ArtImage Source;
            public Color32[] Pixels;

            // Koi only: the mod's texture and sprite, and the game's sprite it replaces.
            public Texture2D Own;
            public Sprite Sprite,
                Original;
        }

        sealed class Target
        {
            public Texture2D Tex,
                Backup;
        }

        sealed class Region
        {
            public Texture2D Tex;
            public RectInt Rect;
            public int Scale;
        }

        readonly Dictionary<string, Img> images = new Dictionary<string, Img>();
        readonly Dictionary<int, Texture2D> seen = new Dictionary<int, Texture2D>();
        readonly Dictionary<string, List<Texture2D>> byName = new Dictionary<string, List<Texture2D>>(
            StringComparer.Ordinal
        );
        readonly Dictionary<int, Target> targets = new Dictionary<int, Target>();
        readonly Dictionary<string, List<Region>> regions = new Dictionary<string, List<Region>>();
        readonly Dictionary<int, TMP_SpriteAsset> chatAssets = new Dictionary<int, TMP_SpriteAsset>();
        readonly Dictionary<string, string> itemNotes = new Dictionary<string, string>();

        // Koi whose icon another plugin replaces, as KoiIconPostfix last saw them.
        readonly HashSet<int> foreignKoi = new HashSet<int>();
        bool iconsKnown;
        double nextScan;
        bool scanSoon = true;

        // Measurements.
        public double LastScanMs,
            MaxScanMs,
            ApplyMs,
            RecomposeMs;
        public int Scans,
            Recomposes;

        public ArtStage(Harmony harmony)
        {
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Art (game)";

        protected internal override void Register(AreaSetup s)
        {
            icons = s.Hook(
                "Database item table, ItemDataSO.icon, OtherStone icons",
                "item and koi icons by id",
                TouchIcons
            );
            koiHook = s.Hook("OtherStone.GetIcon", "koi icons by id", PatchKoi);
            chatHook = s.Hook("ItemTooltip.IconTag, itemicons sprite asset", "koi icons in chat links", PatchChat);
        }

        protected internal override void HostStarted()
        {
            art = Host.Area<ArtArea>();
            art.Names.GameVersion = GameVersion();
            SceneManager.sceneLoaded += (scene, mode) => scanSoon = true;
        }

        protected internal override void Update(double now)
        {
            if (!iconsKnown && icons.Available && DatabaseReady())
                LearnIcons();
            if (scanSoon || now >= nextScan)
            {
                scanSoon = false;
                nextScan = now + ScanSeconds;
                Scan();
            }
        }

        // ---- IArtStage ----

        public void Set(string key, ArtImage image)
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            images.TryGetValue(key, out var old);
            if (old?.Source == image)
                return;
            Img img = null;
            if (image != null)
            {
                var px = Decode(image.Full, image.W, image.H, out string problem);
                if (px == null)
                {
                    art.Reject(key, image.ModId, problem);
                    return;
                }
                img = new Img { Source = image, Pixels = px };
                images[key] = img;
            }
            else
                images.Remove(key);
            string name = key.Substring(key.IndexOf('/') + 1);
            if (key.StartsWith("texture/"))
            {
                if (byName.TryGetValue(name, out var list))
                    foreach (var tex in list)
                        Recompose(tex);
            }
            else if (key.StartsWith("item/"))
                SetItem(key, int.Parse(name));
            else
                SetKoi(int.Parse(name), old, img);
            ApplyMs += t0.Elapsed.TotalMilliseconds;
        }

        public bool IconSize(AssetKind kind, int id, out int w, out int h, out string problem)
        {
            w = h = 0;
            problem = null;
            if (!icons.Available || !DatabaseReady())
                return false;
            var sprite = kind == AssetKind.Item ? ItemIcon(id, out problem) : KoiIcon(id, out problem);
            if (sprite == null)
                return false;
            var r = sprite.rect;
            w = (int)r.width;
            h = (int)r.height;
            return true;
        }

        public AssetLine Describe(string key, ArtImage image)
        {
            string name = key.Substring(key.IndexOf('/') + 1);
            if (key.StartsWith("texture/"))
            {
                int n = byName.TryGetValue(name, out var list)
                    ? list.Count(t => t != null && t.width == image.W && t.height == image.H)
                    : 0;
                if (n > 0)
                    return new AssetLine(image.Rel, AssetState.Applied, n > 1 ? n + " textures" : null);
                return new AssetLine(
                    image.Rel,
                    AssetState.Waiting,
                    art.Names.KnowsTexture(name)
                        ? "the texture is not loaded right now; it applies when the game loads it"
                        : "no texture of that name has been seen yet (see _names.txt); it applies if one loads"
                );
            }
            if (key.StartsWith("koi/") && Covers(int.Parse(name)))
                return new AssetLine(
                    image.Rel,
                    AssetState.Covered,
                    "another plugin draws its own icon for this koi and wins"
                );
            if (!iconsKnown)
                return new AssetLine(image.Rel, AssetState.Waiting, "icons apply once you are in the world");
            itemNotes.TryGetValue(key, out string note);
            return new AssetLine(image.Rel, AssetState.Applied, note);
        }

        // ---- textures ----

        // Finds asset textures that loaded since the last scan; also forgets destroyed ones.
        internal void Scan()
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                int id = tex.GetInstanceID();
                if (id <= 0 || seen.ContainsKey(id))
                    continue;
                seen[id] = tex;
                string name = tex.name;
                if (name.Length == 0 || !Replaceable(tex))
                    continue;
                if (!byName.TryGetValue(name, out var list))
                    byName[name] = list = new List<Texture2D>();
                list.Add(tex);
                art.TextureSeen(name, tex.width, tex.height);
                if (images.ContainsKey("texture/" + name))
                    Recompose(tex);
            }
            foreach (var key in regions.Keys.ToList())
                if (regions[key].Any(r => r.Tex == null))
                    SetItem(key, int.Parse(key.Substring(5)));
            foreach (var id in targets.Where(kv => kv.Value.Tex == null).Select(kv => kv.Key).ToList())
            {
                UnityEngine.Object.Destroy(targets[id].Backup);
                targets.Remove(id);
            }
            foreach (var id in seen.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
                seen.Remove(id);
            foreach (var list in byName.Values)
                list.RemoveAll(t => t == null);
            Scans++;
            LastScanMs = t0.Elapsed.TotalMilliseconds;
            MaxScanMs = Math.Max(MaxScanMs, LastScanMs);
        }

        // Plain 8-bit colour formats; the game's art is R8G8B8A8_SRGB.
        internal static bool Replaceable(Texture2D t) =>
            t.format == TextureFormat.RGBA32
            || t.format == TextureFormat.ARGB32
            || t.format == TextureFormat.BGRA32
            || t.format == TextureFormat.RGB24;

        // The texture's look from its backup: the full image by name if one is set for its size, then item icons.
        void Recompose(Texture2D tex)
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RecomposeNow(tex);
            }
            finally
            {
                Recomposes++;
                RecomposeMs += t0.Elapsed.TotalMilliseconds;
            }
        }

        void RecomposeNow(Texture2D tex)
        {
            int id = tex.GetInstanceID();
            images.TryGetValue("texture/" + tex.name, out var full);
            if (full != null && (full.Source.W != tex.width || full.Source.H != tex.height))
                full = null;
            var mine = regions
                .Where(kv => kv.Value.Any(r => r.Tex == tex))
                .SelectMany(kv => kv.Value.Where(r => r.Tex == tex).Select(r => (Key: kv.Key, Region: r)))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToList();
            targets.TryGetValue(id, out var target);
            if (full == null && mine.Count == 0)
            {
                if (target != null)
                {
                    Graphics.CopyTexture(target.Backup, tex);
                    UnityEngine.Object.Destroy(target.Backup);
                    targets.Remove(id);
                }
                return;
            }
            try
            {
                if (target == null)
                {
                    var backup = Blank(tex.width, tex.height, tex, tex.mipmapCount);
                    backup.name = "mods-backup:" + tex.name;
                    Graphics.CopyTexture(tex, backup);
                    targets[id] = target = new Target { Tex = tex, Backup = backup };
                }
                if (full != null)
                    Upload(full.Pixels, tex.width, tex.height, tex, 0, 0, tex.mipmapCount);
                else
                    Graphics.CopyTexture(target.Backup, tex);
            }
            catch (Exception e)
            {
                Host.Env.Log(LogLevel.Warning, "art: " + tex.name + " (" + tex.graphicsFormat + ") not replaced: " + e);
                if (full != null)
                    art.Reject(
                        "texture/" + tex.name,
                        full.Source.ModId,
                        "the game's texture cannot take it (" + e.Message + ")"
                    );
                return;
            }
            foreach (var (key, r) in mine)
                if (images.TryGetValue(key, out var img))
                {
                    var px = r.Scale == 1 ? img.Pixels : ScaleUp(img.Pixels, img.Source.W, img.Source.H, r.Scale);
                    try
                    {
                        Upload(px, r.Rect.width, r.Rect.height, tex, r.Rect.x, r.Rect.y, 1);
                    }
                    catch (Exception e)
                    {
                        Host.Env.Log(LogLevel.Error, "art: " + key + " into " + tex.name + " failed: " + e.Message);
                    }
                }
        }

        // Same TextureFormat and colour space as the game's texture, so CopyTexture accepts it (RGB24 included, which
        // has no GraphicsFormat of its own on this GPU).
        static Texture2D Blank(int w, int h, Texture2D like, int mips) =>
            new Texture2D(w, h, like.format, mips, !GraphicsFormatUtility.IsSRGBFormat(like.graphicsFormat));

        // Pixels (bottom row first, as GetPixels32 gives them) into an area of a texture.
        static void Upload(Color32[] px, int w, int h, Texture2D dst, int x, int y, int mips)
        {
            var src = Blank(w, h, dst, mips);
            try
            {
                src.SetPixels32(px);
                src.Apply(mips > 1, false);
                if (mips > 1 || (x == 0 && y == 0 && w == dst.width && h == dst.height))
                    Graphics.CopyTexture(src, dst);
                else
                    Graphics.CopyTexture(src, 0, 0, 0, 0, w, h, dst, 0, 0, x, y);
            }
            finally
            {
                UnityEngine.Object.Destroy(src);
            }
        }

        static Color32[] ScaleUp(Color32[] px, int w, int h, int k)
        {
            var o = new Color32[w * k * h * k];
            for (int y = 0; y < h * k; y++)
            for (int x = 0; x < w * k; x++)
                o[y * w * k + x] = px[(y / k) * w + x / k];
            return o;
        }

        // A PNG's pixels once its header, size and chunks passed (FileHeaders.PngData); null with a sentence if it is
        // refused or does not decode to w x h.
        internal static Color32[] Decode(string path, int w, int h, out string problem)
        {
            byte[] data = FileHeaders.PngData(path, FileHeaders.MaxImageSide, out int dw, out int dh, out problem);
            if (data == null)
                return null;
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (dw != w || dh != h || !tmp.LoadImage(data, false) || tmp.width != w || tmp.height != h)
                {
                    problem = "it is not a readable PNG file";
                    return null;
                }
                return tmp.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.Destroy(tmp);
            }
        }

        // ---- item icons ----

        // The icon's area in its texture, and the item's glyph in the chat link sprite sheet when it has the same
        // size or a whole multiple of it.
        void SetItem(string key, int id)
        {
            var touched = new HashSet<Texture2D>();
            if (regions.TryGetValue(key, out var old))
                foreach (var r in old)
                    if (r.Tex != null)
                        touched.Add(r.Tex);
            regions.Remove(key);
            itemNotes.Remove(key);
            if (images.TryGetValue(key, out var img) && iconsKnown)
            {
                var list = ItemRegions(id, img.Source, out string note);
                if (list.Count > 0)
                    regions[key] = list;
                if (note != null)
                    itemNotes[key] = note;
                foreach (var r in list)
                    touched.Add(r.Tex);
            }
            foreach (var tex in touched)
                Recompose(tex);
        }

        // ---- koi icons ----

        void SetKoi(int id, Img old, Img now)
        {
            if (now != null)
            {
                var original = icons.Available && DatabaseReady() ? KoiIcon(id, out _) : null;
                now.Own = new Texture2D(
                    now.Source.W,
                    now.Source.H,
                    GraphicsFormat.R8G8B8A8_SRGB,
                    TextureCreationFlags.None
                )
                {
                    name = "mods-koi:" + id,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                now.Own.SetPixels32(now.Pixels);
                now.Own.Apply(false, true);
                var pivot = original != null ? original.pivot / original.rect.size : new Vector2(0.5f, 0.5f);
                now.Sprite = Sprite.Create(
                    now.Own,
                    new Rect(0, 0, now.Source.W, now.Source.H),
                    pivot,
                    original != null ? original.pixelsPerUnit : 32f,
                    0,
                    SpriteMeshType.FullRect
                );
                now.Sprite.name = now.Own.name;
                now.Original = original;
                if (chatAssets.TryGetValue(id, out var asset))
                    PointChat(asset, now.Own, new Rect(0, 0, now.Source.W, now.Source.H));
            }
            else if (chatAssets.TryGetValue(id, out var asset))
                PointChatBack(asset, id);
            if (old?.Sprite != null)
                Swap(old.Sprite, now?.Sprite ?? old.Original);
            if (now != null && !Covers(id))
                RefreshSlots(id);
            if (old != null)
            {
                UnityEngine.Object.Destroy(old.Sprite);
                UnityEngine.Object.Destroy(old.Own);
            }
            RedrawChat();
        }

        // Where the game shows a sprite of ours that goes away, it shows the replacement instead.
        static void Swap(Sprite from, Sprite to)
        {
            foreach (
                var img in UnityEngine.Object.FindObjectsByType<Image>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
                if (img.sprite == from)
                    img.sprite = to;
            foreach (
                var sr in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
                if (sr.sprite == from)
                    sr.sprite = to;
            foreach (
                var slot in UnityEngine.Object.FindObjectsByType<Slot>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
                if (slot.BaseSprite == from)
                    slot.BaseSprite = to;
        }

        // Inventory slots that already show this koi take the new icon now.
        void RefreshSlots(int koiId)
        {
            if (!DatabaseReady() || !(Database.GetItem(ItemTooltip.COSMIKOI_ID) is OtherStone stone))
                return;
            foreach (
                var slot in UnityEngine.Object.FindObjectsByType<Slot>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
                if (slot.ItemId == ItemTooltip.COSMIKOI_ID && slot.ItemMeta == koiId && slot.Icon != null)
                    slot.BaseSprite = slot.Icon.sprite = stone.GetIcon(koiId);
        }

        // Postfix on OtherStone.GetIcon, run after every other plugin's (Priority.Last): only a sprite that is still
        // the game's own is replaced, so a plugin that draws its own koi icons keeps them.
        static void KoiIconPostfix(OtherStone __instance, int id, ref Sprite __result)
        {
            try
            {
                var p = instance;
                if (p == null)
                    return;
                bool gameOwn = __result == __instance.icon || __instance.icons.Contains(__result);
                if (__result == null || gameOwn)
                    p.foreignKoi.Remove(id);
                else
                    p.foreignKoi.Add(id);
                if (gameOwn && p.images.TryGetValue("koi/" + id, out var img) && img.Sprite != null)
                    __result = img.Sprite;
            }
            catch (Exception) { }
        }

        // Whether another plugin replaces this koi's icon: asks the game for it, which runs that plugin's patch and then
        // KoiIconPostfix.
        internal bool Covers(int koiId) =>
            koiHook.Available && DatabaseReady() && AskKoiIcon(koiId) && foreignKoi.Contains(koiId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool AskKoiIcon(int koiId)
        {
            if (!(Database.GetItem(ItemTooltip.COSMIKOI_ID) is OtherStone stone))
                return false;
            stone.GetIcon(koiId);
            return true;
        }

        // _names.txt says which koi a mod can still show (the koi table must be known).
        void NoteShownKoi()
        {
            var names = art.Names;
            if (names.Koi.Count == 0)
                return;
            var shown = names.Koi.Keys.Where(k => !Covers(k)).ToList();
            if (shown.Count == names.Koi.Count)
                return;
            names.KoiShown = shown;
            names.Changed = true;
        }

        // ---- chat links ----

        // Postfix on ItemTooltip.IconTag (Priority.Last): a koi this host replaces gets its own one-glyph sprite asset;
        // a tag another plugin already changed is left alone. Must never throw (it would blank the chat).
        static void IconTagPostfix(int itemId, int meta, ref string __result)
        {
            try
            {
                var p = instance;
                if (
                    p == null
                    || itemId != ItemTooltip.COSMIKOI_ID
                    || __result == null
                    || !__result.StartsWith("<sprite=\"itemicons\"")
                    || !p.images.TryGetValue("koi/" + meta, out var img)
                    || img.Own == null
                )
                    return;
                var asset = p.ChatAsset(meta, img);
                if (asset != null)
                    __result = "<sprite=\"" + asset.name + "\" index=0>";
            }
            catch (Exception e)
            {
                instance?.Host.Env.Log(LogLevel.Error, "art: chat icon for koi " + meta + " failed: " + e.Message);
            }
        }

        // One sprite asset per koi id for the session, registered with TMP by name; it points at the mod's texture,
        // or back at the game's glyph once the override is gone, so chat lines already shown stay valid.
        TMP_SpriteAsset ChatAsset(int koiId, Img img)
        {
            if (chatAssets.TryGetValue(koiId, out var a))
                return a;
            var sheet = ItemIconsAsset();
            var glyph = sheet != null ? KoiGlyph(sheet, koiId) : null;
            if (glyph == null)
                return null;
            a = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            a.name = ChatPrefix + koiId;
            Traverse.Create(a).Field("m_Version").SetValue("1.1.0");
            a.material = new Material(sheet.material);
            var g = new TMP_SpriteGlyph(
                0,
                glyph.glyph.metrics,
                new GlyphRect(0, 0, img.Source.W, img.Source.H),
                glyph.glyph.scale,
                0
            );
            a.spriteGlyphTable.Add(g);
            a.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, a, g) { name = a.name, scale = glyph.scale });
            PointChat(a, img.Own, new Rect(0, 0, img.Source.W, img.Source.H));
            MaterialReferenceManager.AddSpriteAsset((int)TMP_TextUtilities.GetSimpleHashCode(a.name), a);
            MaterialReferenceManager.AddSpriteAsset((int)TMP_TextUtilities.GetHashCodeCaseInSensitive(a.name), a);
            chatAssets[koiId] = a;
            return a;
        }

        static void PointChat(TMP_SpriteAsset a, Texture tex, Rect r)
        {
            a.spriteSheet = tex;
            a.material.mainTexture = tex;
            a.spriteGlyphTable[0].glyphRect = new GlyphRect((int)r.x, (int)r.y, (int)r.width, (int)r.height);
            a.UpdateLookupTables();
        }

        void PointChatBack(TMP_SpriteAsset a, int koiId)
        {
            var sheet = ItemIconsAsset();
            var glyph = sheet != null ? KoiGlyph(sheet, koiId) : null;
            if (glyph == null)
                return;
            var gr = glyph.glyph.glyphRect;
            PointChat(a, sheet.spriteSheet, new Rect(gr.x, gr.y, gr.width, gr.height));
            a.spriteGlyphTable[0].metrics = glyph.glyph.metrics;
        }

        // Chat lines with one of our glyphs are laid out again (their UVs change when the glyph does).
        void RedrawChat()
        {
            if (chatAssets.Count == 0)
                return;
            foreach (var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
                if (t.text != null && t.text.Contains(ChatPrefix))
                    t.ForceMeshUpdate(false, true);
        }

        // ---- game access ----

        static class G
        {
            public static AccessTools.FieldRef<Database, ItemDatabase> ItemDb;
            public static AccessTools.FieldRef<object, Dictionary<int, ItemDataSO>> Lookup;
            public static AccessTools.FieldRef<Database, Dictionary<int, CosmiKoiData>> KoiDb;
            public static TMP_SpriteAsset ItemIcons;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchIcons()
        {
            G.ItemDb = AccessTools.FieldRefAccess<Database, ItemDatabase>("itemDb");
            G.Lookup = AccessTools.FieldRefAccess<Dictionary<int, ItemDataSO>>(
                typeof(BaseDatabase<ItemDataSO>),
                "LookupDictionnary"
            );
            G.KoiDb = AccessTools.FieldRefAccess<Database, Dictionary<int, CosmiKoiData>>("KoiDB");
            return typeof(OtherStone).GetField("icons") != null && typeof(ItemDataSO).GetField("icon") != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool PatchKoi()
        {
            var m = AccessTools.Method(typeof(OtherStone), nameof(OtherStone.GetIcon));
            if (m == null)
                return false;
            harmony.Patch(
                m,
                postfix: new HarmonyMethod(typeof(ArtStage), nameof(KoiIconPostfix)) { priority = Priority.Last }
            );
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool PatchChat()
        {
            var m = AccessTools.Method(typeof(ItemTooltip), nameof(ItemTooltip.IconTag));
            if (m == null)
                return false;
            harmony.Patch(
                m,
                postfix: new HarmonyMethod(typeof(ArtStage), nameof(IconTagPostfix)) { priority = Priority.Last }
            );
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string GameVersion() => NetworkStrings.GAME_VER;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool DatabaseReady() => Database.Instance != null && G.ItemDb(Database.Instance) != null;

        // The game's tables arrived (first time in the world): names for _names.txt, then the waiting icons.
        [MethodImpl(MethodImplOptions.NoInlining)]
        void LearnIcons()
        {
            iconsKnown = true;
            var names = art.Names;
            foreach (var kv in G.Lookup(G.ItemDb(Database.Instance)))
            {
                var d = kv.Value;
                if (d == null || d.icon == null || d is OtherStone)
                    continue;
                names.Items[kv.Key] = (d.displayName ?? "", (int)d.icon.rect.width, (int)d.icon.rect.height);
                names.IconFrom(d.icon.texture != null ? d.icon.texture.name : null, kv.Key);
            }
            foreach (var kv in G.KoiDb(Database.Instance))
                names.Koi[kv.Key] = kv.Value.name ?? "";
            NoteShownKoi();
            var any = names.Koi.Keys.Select(k => KoiIcon(k, out _)).FirstOrDefault(s => s != null);
            if (any != null)
            {
                names.KoiW = (int)any.rect.width;
                names.KoiH = (int)any.rect.height;
            }
            names.Changed = true;
            art.IconsKnown();
            foreach (var key in images.Keys.Where(k => k.StartsWith("item/")).ToList())
                SetItem(key, int.Parse(key.Substring(5)));
            foreach (var key in images.Keys.Where(k => k.StartsWith("koi/")).ToList())
            {
                var img = images[key];
                images.Remove(key);
                SetKoi(int.Parse(key.Substring(4)), img, ReplaceImg(key, img));
            }
        }

        // A fresh Img for the same source (its sprite is rebuilt with the game's pivot, now known).
        Img ReplaceImg(string key, Img old)
        {
            var img = new Img { Source = old.Source, Pixels = old.Pixels };
            images[key] = img;
            return img;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Sprite ItemIcon(int id, out string problem)
        {
            problem = null;
            var d = Database.GetItem(id);
            if (d == null)
                problem = "there is no item with id " + id + " (see _names.txt)";
            else if (d is OtherStone)
                problem = "item " + id + " is the koi; koi icons go in art/koi/<koi id>.png";
            else if (d.icon == null)
                problem = "item " + id + " has no icon";
            return problem == null ? d.icon : null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Sprite KoiIcon(int id, out string problem)
        {
            problem = null;
            if (!G.KoiDb(Database.Instance).ContainsKey(id))
            {
                problem = "there is no koi with id " + id + " (see _names.txt)";
                return null;
            }
            if (!(Database.GetItem(ItemTooltip.COSMIKOI_ID) is OtherStone stone))
                return null;
            return stone.GetVariantIcon(stone.GetIconVariant(id));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        List<Region> ItemRegions(int id, ArtImage image, out string note)
        {
            note = null;
            var list = new List<Region>();
            var icon = ItemIcon(id, out _);
            if (icon == null)
                return list;
            var tex = icon.texture;
            var r = icon.textureRect;
            if (tex.mipmapCount > 1 || !Replaceable(tex))
            {
                art.Reject("item/" + id, image.ModId, "the game's icon texture cannot be replaced");
                return list;
            }
            list.Add(
                new Region
                {
                    Tex = tex,
                    Rect = new RectInt((int)r.x, (int)r.y, image.W, image.H),
                    Scale = 1,
                }
            );
            var sheet = chatHook.Available ? ItemIconsAsset() : null;
            int gi = sheet != null ? sheet.GetSpriteIndexFromName("item_" + id) : -1;
            if (
                gi < 0
                || !(sheet.spriteSheet is Texture2D sheetTex)
                || !Replaceable(sheetTex)
                || sheetTex.mipmapCount > 1
            )
            {
                note = "chat links keep the game's icon";
                return list;
            }
            var g = sheet.spriteCharacterTable[gi].glyph.glyphRect;
            if (g.width % image.W != 0 || g.width / image.W != g.height / image.H || g.height % image.H != 0)
            {
                note = "chat links keep the game's icon (theirs is " + g.width + "x" + g.height + ")";
                return list;
            }
            list.Add(
                new Region
                {
                    Tex = sheetTex,
                    Rect = new RectInt(g.x, g.y, g.width, g.height),
                    Scale = g.width / image.W,
                }
            );
            return list;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static TMP_SpriteAsset ItemIconsAsset()
        {
            if (G.ItemIcons == null)
                G.ItemIcons = Resources.Load<TMP_SpriteAsset>(TMP_Settings.defaultSpriteAssetPath + "itemicons");
            return G.ItemIcons;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static TMP_SpriteCharacter KoiGlyph(TMP_SpriteAsset sheet, int koiId)
        {
            if (!(Database.GetItem(ItemTooltip.COSMIKOI_ID) is OtherStone stone))
                return null;
            int i = sheet.GetSpriteIndexFromName(ItemTooltip.IconSpriteName(stone, koiId));
            return i < 0 ? null : sheet.spriteCharacterTable[i];
        }

        // ---- for the author tools ----

        // The game's own pixels of a loaded texture: its backup while a mod replaces any of it, else itself.
        internal Texture2D Original(Texture2D tex) =>
            targets.TryGetValue(tex.GetInstanceID(), out var t) && t.Backup != null ? t.Backup : tex;

        internal IEnumerable<Texture2D> LoadedTextures => byName.Values.SelectMany(l => l).Where(t => t != null);

        internal bool IconsReady => iconsKnown && icons.Available && DatabaseReady();

        // ---- measurements ----

        public IEnumerable<Texture2D> Loaded(string name) =>
            byName.TryGetValue(name, out var l) ? l.Where(t => t != null) : Enumerable.Empty<Texture2D>();

        public int Targets => targets.Count;

        public long BackupBytes =>
            targets.Values.Sum(t => (long)t.Tex.width * t.Tex.height * 4 * (t.Tex.mipmapCount > 1 ? 4 : 3) / 3);

        public int SeenCount => seen.Count;

        public Sprite KoiSprite(int id) => images.TryGetValue("koi/" + id, out var i) ? i.Sprite : null;
    }
}
