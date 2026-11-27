using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ReworkedCopium;
using ReworkedCopium.Inventory;
using ReworkedCopium.Player;
using ReworkedCopium.Shop;
using ReworkedCopium.UI;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;
using ShorelandsMods.Game.Assets;
using ShorelandsMods.Game.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Tools
{
    // Inspect mode, an author tool: a banner says it is on and how to leave; a panel beside the pointer names what
    // is drawn under it (screen art, world sprites, ground tiles, the cursor; an item or koi icon by its id) with the
    // file that replaces it; a click saves the marked one's original pixels. While on, a clear plate over the game's
    // UI takes the pointer and the game's input is off, so nothing in the game reacts; off, it costs one key check.
    static class Inspector
    {
        const int Order = 300,
            PadX = 6,
            PadY = 4,
            Gap = 3;
        const float LookEvery = 0.25f;
        static readonly Color Header = new Color32(0xFF, 0xD9, 0x8C, 0xFF);
        static readonly Color MarkBack = new Color32(0, 0, 0, 0x59);

        static ToolsArea tools;
        static ArtStage art;
        static Func<Key> key;
        static Canvas root;
        static RectTransform layer,
            banner,
            panel,
            mark;
        static InspectInput input;
        static readonly List<TextMeshProUGUI> lines = new List<TextMeshProUGUI>();
        static TextMeshProUGUI[] bannerLines;
        static List<InspectEntry> entries = new List<InspectEntry>();
        static int marked;
        static Vector2 lastPointer;
        static float nextLook,
            savedUntil,
            drawnAt;
        static string saved,
            drawn;
        static Dictionary<Sprite, int> itemOf;

        public static bool On { get; private set; }

        public static void Init(ToolsArea toolsArea, ArtStage artStage, Func<Key> toggleKey)
        {
            tools = toolsArea;
            art = artStage;
            key = toggleKey;
        }

        public static string KeyName => key != null && key() != Key.None ? key().ToString() : null;

        // The bottom of the banner while it shows (notices stack under it), else 0.
        public static int BannerBottom =>
            On && banner != null ? Mathf.RoundToInt(-banner.anchoredPosition.y + banner.sizeDelta.y) : 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool InWorld() => PlayerModel.LocalPlayer != null && GameManager.Instance?.hotbarView != null;

        public static void Set(bool on)
        {
            if (on == On || (on && !Build()))
                return;
            On = on;
            InputMute.Hold(on);
            layer.gameObject.SetActive(on);
            if (on)
            {
                EscapeMenus.Opened(input);
                TooltipSystem.Hide();
                entries = new List<InspectEntry>();
                marked = 0;
                drawn = null;
                lastPointer = new Vector2(float.NaN, float.NaN);
                Look();
            }
            else
            {
                EscapeMenus.Closed(input);
                Alphas.Clear();
            }
        }

        // Every frame (LateUpdate).
        public static void Tick()
        {
            var k = Keyboard.current;
            if (k != null && KeyName != null && k[key()].wasPressedThisFrame && (On || !KeyInput.Typing()) && InWorld())
                Set(!On);
            if (!On)
                return;
            if (!InWorld() || layer == null || root == null || !root.isActiveAndEnabled)
            {
                On = false;
                InputMute.Hold(false);
                EscapeMenus.Closed(input);
                return;
            }
            if (root.scaleFactor != drawnAt)
            {
                drawnAt = root.scaleFactor;
                UiKit.Redraw(layer);
                drawn = null;
            }
            if (Pointer() != lastPointer || Time.unscaledTime >= nextLook)
                Look();
            else if (saved != null && Time.unscaledTime >= savedUntil)
            {
                saved = null;
                Draw();
            }
        }

        static Vector2 Pointer() => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        static void Look()
        {
            nextLook = Time.unscaledTime + LookEvery;
            lastPointer = Pointer();
            var now = InspectModel.Entries(HitsAt(lastPointer));
            marked = InspectModel.Keep(entries, marked, now);
            entries = now;
            Draw();
        }

        internal static void Choose(int step)
        {
            if (entries.Count == 0)
                return;
            marked = Mathf.Clamp(marked + step, 0, entries.Count - 1);
            Draw();
        }

        internal static void SaveMarked()
        {
            if (marked >= entries.Count)
                return;
            var e = entries[marked];
            saved = e.Exportable ? tools.ExportEntry(e) : "This one cannot be saved: nothing can replace it.";
            savedUntil = Time.unscaledTime + 4;
            Draw();
        }

        // ---- what is under the pointer ----

        static List<InspectHit> HitsAt(Vector2 screen)
        {
            var hits = new List<InspectHit>();
            Ui(screen, hits);
            World(screen, hits);
            var cursor =
                CursorUI.Instance != null
                    ? CursorUI.Instance.GetComponentsInChildren<Image>().FirstOrDefault(i => i.sprite != null)
                    : null;
            if (cursor != null)
                hits.Add(TextureHit(cursor.sprite.texture, HitKind.Cursor));
            return hits;
        }

        // Image and RawImage graphics drawn at the point, topmost first: by canvas order, then drawing order.
        static void Ui(Vector2 screen, List<InspectHit> hits)
        {
            var found = new List<(int order, int depth, InspectHit hit)>();
            foreach (var g in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None))
            {
                Texture tex =
                    g is Image im ? (im.overrideSprite != null ? im.overrideSprite.texture : null)
                    : g is RawImage ri ? ri.texture
                    : null;
                if (tex == null || !g.isActiveAndEnabled || g.canvas == null || g.depth < 0)
                    continue;
                if (g.transform.IsChildOf(layer) || g.GetComponentInParent<CursorUI>() != null)
                    continue;
                if (g.canvasRenderer.cull || g.canvasRenderer.GetInheritedAlpha() * g.color.a < 0.02f)
                    continue;
                var rootCanvas = g.canvas.rootCanvas;
                var cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
                if (
                    !RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, screen, cam)
                    || Clipped(g, screen, cam)
                )
                    continue;
                var hit = IconHit(g as Image) ?? TextureHit(tex, HitKind.Screen);
                hit.Alpha = UiAlpha(g, screen, cam) * g.color.a * g.canvasRenderer.GetInheritedAlpha();
                found.Add((SortingOf(g.canvas), g.depth, hit));
            }
            hits.AddRange(found.OrderByDescending(f => f.order).ThenByDescending(f => f.depth).Select(f => f.hit));
        }

        static int SortingOf(Canvas c)
        {
            while (c != null && !c.isRootCanvas && !c.overrideSorting)
                c = c.transform.parent != null ? c.transform.parent.GetComponentInParent<Canvas>() : null;
            return c != null ? c.sortingOrder : 0;
        }

        // Outside a scroll view's mask the graphic is not drawn there.
        static bool Clipped(Graphic g, Vector2 screen, Camera cam)
        {
            foreach (var m in g.GetComponentsInParent<RectMask2D>())
                if (
                    m.isActiveAndEnabled
                    && !RectTransformUtility.RectangleContainsScreenPoint(m.rectTransform, screen, cam)
                )
                    return true;
            foreach (var m in g.GetComponentsInParent<Mask>())
                if (
                    m != null
                    && m.isActiveAndEnabled
                    && m.graphic != g
                    && !RectTransformUtility.RectangleContainsScreenPoint(m.rectTransform, screen, cam)
                )
                    return true;
            return false;
        }

        // Sprites and tiles at the point, front first.
        static void World(Vector2 screen, List<InspectHit> hits)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector2 p = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0));
            var found = new List<(int layer, int order, float y, InspectHit hit)>();
            foreach (var sr in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (!sr.enabled || !sr.isVisible || sr.sprite == null || sr.color.a < 0.02f)
                    continue;
                var b = sr.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y)
                    continue;
                var hit = ItemHit(sr.sprite, -1, -1) ?? TextureHit(sr.sprite.texture, HitKind.World);
                hit.Kind = HitKind.World;
                hit.Player = PlayerOf(sr.transform);
                hit.Alpha =
                    sr.color.a
                    * (
                        sr.drawMode == SpriteDrawMode.Simple
                            ? SpriteAlpha(sr.sprite, sr.transform.InverseTransformPoint(p), sr.flipX, sr.flipY)
                            : 1
                    );
                found.Add(
                    (SortingLayer.GetLayerValueFromID(sr.sortingLayerID), sr.sortingOrder, sr.transform.position.y, hit)
                );
            }
            foreach (var tm in UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            {
                var r = tm.GetComponent<TilemapRenderer>();
                if (r == null || !r.enabled || !r.isVisible)
                    continue;
                var cell = tm.WorldToCell(p);
                var s = tm.GetSprite(cell);
                if (s == null || s.texture == null)
                    continue;
                var tile = TextureHit(s.texture, HitKind.Ground);
                // The tile's sprite has its pivot at the cell's anchor, moved by the tile's own matrix.
                var at = tm.GetTransformMatrix(cell)
                    .inverse.MultiplyPoint(
                        tm.transform.InverseTransformPoint(p) - tm.CellToLocalInterpolated(cell + tm.tileAnchor)
                    );
                tile.Alpha = tm.GetColor(cell).a * tm.color.a * SpriteAlpha(s, at, false, false);
                found.Add((SortingLayer.GetLayerValueFromID(r.sortingLayerID), r.sortingOrder, float.MaxValue, tile));
            }
            hits.AddRange(
                found.OrderByDescending(f => f.layer).ThenByDescending(f => f.order).ThenBy(f => f.y).Select(f => f.hit)
            );
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static PlayerPart PlayerOf(Transform t)
        {
            var model = t.GetComponentInParent<PlayerModel>();
            return model == null ? PlayerPart.None
                : model == PlayerModel.LocalPlayer ? PlayerPart.Yours
                : PlayerPart.Another;
        }

        // ---- what is drawn at a pixel ----

        // Alpha of each texture read once from the GPU while inspect mode is on (the game's textures are not
        // readable on the CPU); null for one that cannot be read, which counts as opaque.
        static readonly Dictionary<Texture2D, byte[]> Alphas = new Dictionary<Texture2D, byte[]>();
        const int MaxAlphaBytes = 32 << 20;
        static int alphaBytes;

        static float TextureAlpha(Texture tex, int x, int y)
        {
            if (!(tex is Texture2D t))
                return 1;
            if (x < 0 || y < 0 || x >= t.width || y >= t.height)
                return 0;
            if (!Alphas.TryGetValue(t, out var a))
            {
                if (alphaBytes > MaxAlphaBytes)
                {
                    Alphas.Clear();
                    alphaBytes = 0;
                }
                Alphas[t] = a = ReadAlpha(t);
                alphaBytes += a?.Length ?? 0;
            }
            return a == null ? 1 : a[y * t.width + x] / 255f;
        }

        static byte[] ReadAlpha(Texture2D t)
        {
            try
            {
                var req = UnityEngine.Rendering.AsyncGPUReadback.Request(t, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (req.hasError)
                    return null;
                var px = req.GetData<byte>();
                var a = new byte[t.width * t.height];
                for (int i = 0; i < a.Length; i++)
                    a[i] = px[i * 4 + 3];
                return a;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // A sprite's alpha at a point in its own units (origin at its pivot).
        static float SpriteAlpha(Sprite s, Vector2 local, bool flipX, bool flipY)
        {
            if (flipX)
                local.x = -local.x;
            if (flipY)
                local.y = -local.y;
            var px = local * s.pixelsPerUnit + s.pivot;
            if (px.x < 0 || px.y < 0 || px.x >= s.rect.width || px.y >= s.rect.height)
                return 0;
            return SpritePixel(s, px);
        }

        // px: pixels from the bottom-left of the sprite's rect.
        static float SpritePixel(Sprite s, Vector2 px)
        {
            Rect tr;
            try
            {
                tr = s.textureRect;
            }
            catch (Exception)
            {
                return 1;
            }
            var at = tr.position + px - s.textureRectOffset;
            if (at.x < tr.xMin || at.y < tr.yMin || at.x >= tr.xMax || at.y >= tr.yMax)
                return 0;
            return TextureAlpha(s.texture, (int)at.x, (int)at.y);
        }

        // A UI graphic's texture alpha at a screen point: simple images and raw images by their pixel, others (sliced
        // plates, filled bars) as drawn all over their rect.
        static float UiAlpha(Graphic g, Vector2 screen, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(g.rectTransform, screen, cam, out var local))
                return 1;
            var r = g.rectTransform.rect;
            var uv = new Vector2((local.x - r.x) / r.width, (local.y - r.y) / r.height);
            if (g is RawImage raw)
            {
                var u = raw.uvRect;
                return TextureAlpha(
                    raw.texture,
                    Mathf.FloorToInt((u.x + uv.x * u.width) * raw.texture.width),
                    Mathf.FloorToInt((u.y + uv.y * u.height) * raw.texture.height)
                );
            }
            if (g is Image im && im.type == Image.Type.Simple && !im.preserveAspect && im.overrideSprite is Sprite s)
                return SpritePixel(s, Vector2.Scale(uv, s.rect.size));
            return 1;
        }

        static InspectHit TextureHit(Texture tex, HitKind kind) =>
            new InspectHit
            {
                Kind = kind,
                Texture = tex.name,
                W = tex.width,
                H = tex.height,
                NotReplaceable = WhyNot(tex),
            };

        static string WhyNot(Texture tex)
        {
            if (!(tex is Texture2D t) || t.GetInstanceID() < 0)
                return "the game makes it while it runs";
            if (!ArtStage.Replaceable(t))
                return "its pixel format (" + t.format + ") is not one a file can replace";
            return Originals.NameProblem(t.name);
        }

        // An item or koi icon in a slot (which says the id and the koi), in a shop slot, or by its sprite.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static InspectHit IconHit(Image im)
        {
            if (im == null || im.sprite == null || !art.IconsReady)
                return null;
            var slot = im.GetComponentInParent<Slot>();
            if (slot != null && slot.Icon == im && slot.ItemId > 0)
                return ItemHit(im.sprite, slot.ItemId, slot.ItemMeta);
            var shop = im.GetComponentInParent<ShopSlot>();
            if (
                shop != null
                && shop.itemData != null
                && shop.IsShown
                && im.sprite == shop.itemData.GetIcon(shop.shopSlot.item.meta)
            )
                return ItemHit(im.sprite, shop.itemData.id, shop.shopSlot.item.meta);
            return ItemHit(im.sprite, -1, -1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static InspectHit ItemHit(Sprite sprite, int id, int meta)
        {
            if (!art.IconsReady)
                return null;
            if (id < 0 && !ItemOf().TryGetValue(sprite, out id))
                return null;
            var names = tools.Host.Area<ArtArea>().Names;
            if (id == ItemTooltip.COSMIKOI_ID)
            {
                if (meta < 0 || !names.Koi.ContainsKey(meta))
                    return null;
                return new InspectHit
                {
                    Kind = HitKind.Screen,
                    KoiId = meta,
                    Name = names.Koi[meta],
                    IconW = names.KoiW,
                    IconH = names.KoiH,
                    KoiCovered = art.Covers(meta),
                };
            }
            if (!names.Items.TryGetValue(id, out var item))
                return null;
            return new InspectHit
            {
                Kind = HitKind.Screen,
                ItemId = id,
                Name = item.Name,
                IconW = item.W,
                IconH = item.H,
            };
        }

        // The game's item icons by sprite (koi icons are shared by colour, so only a slot can tell a koi's id).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Dictionary<Sprite, int> ItemOf()
        {
            if (itemOf != null)
                return itemOf;
            itemOf = new Dictionary<Sprite, int>();
            foreach (var id in tools.Host.Area<ArtArea>().Names.Items.Keys)
                if (ArtStage.ItemIcon(id, out _) is Sprite s && !itemOf.ContainsKey(s))
                    itemOf[s] = id;
            return itemOf;
        }

        // ---- drawing ----

        static bool Build()
        {
            if (layer != null && root != null)
                return true;
            root = HudRoot();
            if (root == null || !UiKit.Resolve(_ => { }))
                return false;
            layer = UiKit.Layer("ShorelandsMods inspect", root, Order, true);
            var blocker = UiKit.Child("Blocker", layer);
            blocker.anchorMin = Vector2.zero;
            blocker.anchorMax = Vector2.one;
            blocker.offsetMin = blocker.offsetMax = Vector2.zero;
            UiKit.Plate(blocker, null, Color.clear, true);
            input = blocker.gameObject.AddComponent<InspectInput>();
            banner = UiKit.Child("Banner", layer);
            UiKit.Plate(banner, UiKit.Bg2, Color.white, false);
            panel = UiKit.Child("Panel", layer);
            UiKit.Plate(panel, UiKit.Bg1, Color.white, false);
            mark = UiKit.Child("Mark", panel);
            UiKit.Plate(mark, UiKit.ChatBg, MarkBack, false);
            bannerLines = new[]
            {
                UiKit.Label("Line", banner, Header, true),
                UiKit.Label("Line", banner, Color.white, true),
            };
            lines.Clear();
            drawnAt = root.scaleFactor;
            layer.gameObject.SetActive(false);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Canvas HudRoot()
        {
            var hotbar = InWorld() ? GameManager.Instance.hotbarView : null;
            return hotbar != null ? hotbar.GetComponentInParent<Canvas>()?.rootCanvas : null;
        }

        static int CanvasW => Mathf.RoundToInt(((RectTransform)root.transform).rect.width);
        static int CanvasH => Mathf.RoundToInt(((RectTransform)root.transform).rect.height);

        static TextMeshProUGUI Line(int i)
        {
            while (lines.Count <= i)
                lines.Add(UiKit.Label("Line", panel, Color.white, true));
            lines[i].gameObject.SetActive(true);
            return lines[i];
        }

        static void Draw()
        {
            int lh = UiKit.LineH;
            int cw = CanvasW,
                ch = CanvasH;
            var text = new List<(string s, Color c)>();
            string k = KeyName;
            text.Add(("Inspect mode: point at anything to see the name of its art", Header));
            text.Add(
                (
                    "Click saves the marked one to mods/_originals.   Wheel: mark another.   Esc"
                        + (k != null ? " or " + k : "")
                        + ": leave",
                    Color.white
                )
            );
            if (entries.Count == 0)
                text.Add(("Nothing with art is under the pointer.", UiKit.Hint));
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                text.Add(
                    (
                        e.Title,
                        i == marked ? Header
                        : e.Exportable ? Color.white
                        : UiKit.Hint
                    )
                );
                text.Add((e.Detail, UiKit.Hint));
            }
            if (entries.Count > 0)
                text.Add(
                    saved != null
                        ? (saved, saved.StartsWith("Saved") ? UiKit.Good : UiKit.Warn)
                        : ("Click: save the marked one", UiKit.Hint)
                );
            string stamp = string.Join("\n", text.Select(t => t.s)) + marked + cw + "x" + ch + lastPointer;
            if (stamp == drawn)
                return;
            drawn = stamp;
            // Banner: two lines at the top centre.
            int bw = Math.Max(UiKit.Measure(text[0].s), UiKit.Measure(text[1].s)) + 2 * PadX;
            int bh = 2 * lh + 2 * PadY;
            var bannerRect = new URect((cw - bw) / 2, 4, bw, bh);
            UiKit.Put(banner, bannerRect);
            for (int i = 0; i < 2; i++)
                Place(bannerLines[i], text[i].s, text[i].c, PadX, PadY + i * lh, bw - 2 * PadX);
            // Panel: entries, then the footer, beside the pointer and inside the screen, clear of the banner.
            var body = text.Skip(2).ToList();
            int pw = body.Max(t => UiKit.Measure(t.s)) + 2 * PadX;
            int ph = body.Count * lh + 2 * PadY + (entries.Count > 0 ? Gap : 0);
            float scale = root.scaleFactor;
            int px = Mathf.RoundToInt(lastPointer.x / scale) + 12,
                py = ch - Mathf.RoundToInt(lastPointer.y / scale) + 14;
            if (px + pw > cw - 2)
                px = px - 12 - 8 - pw;
            if (py + ph > ch - 2)
                py = py - 14 - 6 - ph;
            px = Mathf.Clamp(px, 2, Math.Max(2, cw - pw - 2));
            py = Mathf.Clamp(py, 2, Math.Max(2, ch - ph - 2));
            var panelRect = new URect(px, py, pw, ph);
            if (panelRect.Overlaps(bannerRect) && bannerRect.Bottom + Gap + ph <= ch - 2)
                panelRect = new URect(px, bannerRect.Bottom + Gap, pw, ph);
            UiKit.Put(panel, panelRect);
            int y = PadY;
            for (int i = 0; i < body.Count; i++)
            {
                bool footer = i == body.Count - 1 && entries.Count > 0;
                if (footer)
                    y += Gap;
                Place(Line(i), body[i].s, body[i].c, PadX, y, pw - 2 * PadX);
                y += lh;
            }
            for (int i = body.Count; i < lines.Count; i++)
                lines[i].gameObject.SetActive(false);
            mark.gameObject.SetActive(marked < entries.Count);
            if (marked < entries.Count)
                UiKit.Put(mark, PadX - 2, PadY + 2 * marked * lh, pw - 2 * PadX + 4, 2 * lh);
            mark.SetAsFirstSibling();
        }

        static void Place(TextMeshProUGUI t, string s, Color c, int x, int y, int w)
        {
            UiKit.SetText(t, TextEscape.Plain(s));
            t.color = c;
            UiKit.Put(t.rectTransform, x, y, w + 1, UiKit.LineH);
        }
    }

    // The clear plate over the game while inspect mode is on: a left click saves the marked entry, the wheel marks
    // another, a right click or the game's Escape (IMenu) leaves.
    sealed class InspectInput : MonoBehaviour, IPointerClickHandler, IScrollHandler, IMenu
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                Inspector.SaveMarked();
            else if (e.button == PointerEventData.InputButton.Right)
                Inspector.Set(false);
        }

        public void OnScroll(PointerEventData e)
        {
            if (e.scrollDelta.y != 0)
                Inspector.Choose(e.scrollDelta.y > 0 ? -1 : 1);
        }

        public void Close() => Inspector.Set(false);
    }
}
