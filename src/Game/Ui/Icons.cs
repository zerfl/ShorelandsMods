using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;
using UnityEngine;

namespace ShorelandsMods.Game.Ui
{
    // Sprites for IconRefs: the game's item icon (a koi's for item 9999 with its meta), or a PNG of a mod. Nothing is
    // decoded on a drawing path: files and list item icons are fetched by Pump within a time budget a frame.
    static class Icons
    {
        // Mod PNGs by file version (IconRef.Stamp, from the host's check): the sprite, or null for a file that did not
        // decode, kept so it is not tried again until the file changes. At most Capacity; the oldest is freed first.
        const int Capacity = 128;

        sealed class FileIcon
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        static readonly Dictionary<string, FileIcon> files = new Dictionary<string, FileIcon>();
        static readonly LinkedList<string> age = new LinkedList<string>();

        // Item icons for long lists: another plugin may load art on the first request for a koi, several ms each;
        // kept for TtlSeconds so later changes to an icon still show.
        const float TtlSeconds = 10f;
        static readonly Dictionary<long, (Sprite sprite, float at)> items = new Dictionary<long, (Sprite, float)>();
        static readonly Queue<IconRef> pending = new Queue<IconRef>();
        static readonly HashSet<string> queued = new HashSet<string>();

        static long Key(IconRef i) => ((long)i.ItemId << 32) | (uint)i.Meta;

        static string QueueKey(IconRef i) => i.File != null ? FileKey(i) : Key(i).ToString();

        static string FileKey(IconRef i) => i.File + "|" + i.Stamp;

        // The sprite if it is at hand now; otherwise it is fetched by Pump and false is returned.
        public static bool TryGet(IconRef icon, out Sprite sprite)
        {
            sprite = null;
            if (icon == null)
                return true;
            if (icon.File != null)
                return Cached(icon, out sprite);
            long k = Key(icon);
            bool known = items.TryGetValue(k, out var e);
            if (known && Time.unscaledTime - e.at < TtlSeconds)
            {
                sprite = e.sprite;
                return true;
            }
            Enqueue(icon);
            // An old icon shows until its fresh copy arrives.
            sprite = known ? e.sprite : null;
            return known;
        }

        // For a widget: an item icon at once; a file from the cache, or false while Pump fetches it.
        public static bool Get(IconRef icon, out Sprite sprite)
        {
            sprite = null;
            if (icon == null)
                return true;
            if (icon.File != null)
                return Cached(icon, out sprite);
            sprite = ItemIcon(icon);
            return true;
        }

        static bool Cached(IconRef icon, out Sprite sprite)
        {
            string k = FileKey(icon);
            if (files.TryGetValue(k, out var f))
            {
                age.Remove(k);
                age.AddLast(k);
                sprite = f.Sprite;
                return true;
            }
            Enqueue(icon);
            sprite = null;
            return false;
        }

        static void Enqueue(IconRef icon)
        {
            if (queued.Add(QueueKey(icon)))
                pending.Enqueue(icon);
        }

        // Fetches waiting icons for up to budgetMs (at least one); true if any arrived.
        public static bool Pump(double budgetMs)
        {
            if (pending.Count == 0)
                return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                var icon = pending.Dequeue();
                queued.Remove(QueueKey(icon));
                if (icon.File != null)
                    Load(icon);
                else
                    items[Key(icon)] = (ItemIcon(icon), Time.unscaledTime);
            } while (pending.Count > 0 && sw.Elapsed.TotalMilliseconds < budgetMs);
            if (items.Count > 4096)
                items.Clear();
            return true;
        }

        static Sprite ItemIcon(IconRef icon)
        {
            try
            {
                return ItemSprite(icon.ItemId, icon.Meta);
            }
            catch (Exception)
            {
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Sprite ItemSprite(int id, int meta)
        {
            var def = ReworkedCopium.Database.Instance != null ? ReworkedCopium.Database.GetItem(id) : null;
            return def != null ? def.GetIcon(meta) : null;
        }

        // Decodes a mod's PNG once its header, size and chunks passed (FileHeaders.PngData), with point filtering.
        static void Load(IconRef icon)
        {
            string k = FileKey(icon);
            if (files.ContainsKey(k))
                return;
            var f = new FileIcon();
            byte[] data = FileHeaders.PngData(icon.File, Limits.IconSide, out int w, out int h, out _);
            if (data != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool decoded;
                try
                {
                    decoded = tex.LoadImage(data) && tex.width == w && tex.height == h;
                }
                catch (Exception)
                {
                    decoded = false;
                }
                if (decoded)
                {
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    f.Texture = tex;
                    f.Sprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, w, h),
                        new Vector2(0.5f, 0.5f),
                        32,
                        0,
                        SpriteMeshType.FullRect
                    );
                }
                else
                    UnityEngine.Object.Destroy(tex);
            }
            files[k] = f;
            age.AddLast(k);
            while (age.Count > Capacity)
                Free(age.First.Value);
        }

        static void Free(string key)
        {
            if (!files.TryGetValue(key, out var f))
                return;
            files.Remove(key);
            age.Remove(key);
            if (f.Sprite != null)
                UnityEngine.Object.Destroy(f.Sprite);
            if (f.Texture != null)
                UnityEngine.Object.Destroy(f.Texture);
        }
    }
}
