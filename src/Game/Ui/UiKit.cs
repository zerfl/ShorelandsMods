using System;
using System.Collections.Generic;
using ReworkedCopium.UI;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Ui
{
    // The game's look, taken from its own objects: the font and material of its labels, its plates and button sprites
    // by name, its button tints, its text shadow. Host UI is laid out in whole canvas units from the top-left corner.
    static class UiKit
    {
        public const int TextSize = 9;
        public static readonly Color Gold = new Color32(0xF2, 0xC9, 0x4C, 0xFF);
        public static readonly Color Hint = new Color32(0xB4, 0xB4, 0xB4, 0xFF);
        public static readonly Color Warn = new Color32(0xFF, 0xD2, 0x7F, 0xFF);
        public static readonly Color Bad = new Color32(0xFF, 0x9C, 0x9C, 0xFF);
        public static readonly Color Good = new Color32(0xA8, 0xE0, 0x8C, 0xFF);
        public static readonly Color ModTint = ColorUtility.TryParseHtmlString(ModHost.Color.Mod, out var c)
            ? c
            : Color.white;

        public static TMP_FontAsset Font;
        public static Material FontMaterial;

        // inventory_bg_1 (tooltip, profile), _2 (settings window, wallet), _3 (buttons, fields, toggles), chat_bg
        // (dark lists, slider tracks, check marks), ui_exit (close cross).
        public static Sprite Bg1,
            Bg2,
            Bg3,
            ChatBg,
            Exit;
        public static ColorBlock ButtonColors;
        static int lineH;
        static TextMeshProUGUI measurer;
        static string missing;
        static float nextSpriteLook;

        public static bool Ready => Font != null && Bg3 != null;

        // Looks the game's style up; false (and logged once) while some of it cannot be found.
        public static bool Resolve(Action<string> log)
        {
            if (
                Ready
                && measurer != null
                && (Exit != null && ChatBg != null && Bg1 != null && Bg2 != null || Time.unscaledTime < nextSpriteLook)
            )
                return true;
            nextSpriteLook = Time.unscaledTime + 1f;
            if (Font == null)
                foreach (var t in Resources.FindObjectsOfTypeAll<TextShadow>())
                {
                    var label = t.GetComponent<TextMeshProUGUI>();
                    if (label != null && label.font != null && t.gameObject.scene.IsValid())
                    {
                        Font = label.font;
                        FontMaterial = label.fontSharedMaterial;
                        break;
                    }
                }
            var byName = new Dictionary<string, Sprite>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && !byName.ContainsKey(s.name))
                    byName[s.name] = s;
            Bg1 = Bg1 ?? Get(byName, "inventory_bg_1");
            Bg2 = Bg2 ?? Get(byName, "inventory_bg_2");
            Bg3 = Bg3 ?? Get(byName, "inventory_bg_3");
            ChatBg = ChatBg ?? Get(byName, "chat_bg");
            Exit = Exit ?? Get(byName, "ui_exit");
            ButtonColors = ColorBlock.defaultColorBlock;
            ButtonColors.normalColor = new Color32(0xEB, 0xEB, 0xEB, 0xFF);
            ButtonColors.highlightedColor = Color.white;
            ButtonColors.pressedColor = new Color32(0xB8, 0xB8, 0xB8, 0xFF);
            ButtonColors.selectedColor = ButtonColors.normalColor;
            ButtonColors.disabledColor = new Color32(0x8C, 0x8C, 0x8C, 0x99);
            ButtonColors.fadeDuration = 0.05f;
            foreach (var b in Resources.FindObjectsOfTypeAll<Button>())
                if (b.gameObject.scene.IsValid() && b.targetGraphic is Image img && img.sprite == Bg3)
                {
                    ButtonColors = b.colors;
                    break;
                }
            string now =
                Font == null ? "the game's font"
                : Bg3 == null ? "the game's button sprite"
                : null;
            if (now != null)
            {
                if (missing != now)
                    log("host UI waits for " + now);
                missing = now;
                return false;
            }
            if (measurer == null)
            {
                // Active (an inactive label measures nothing) but under no canvas, so it is never drawn.
                var go = new GameObject("ShorelandsMods measure", typeof(RectTransform));
                UnityEngine.Object.DontDestroyOnLoad(go);
                measurer = go.AddComponent<TextMeshProUGUI>();
                Style(measurer, TextSize);
                lineH = 0;
            }
            return true;
        }

        static Sprite Get(Dictionary<string, Sprite> byName, string name) =>
            byName.TryGetValue(name, out var s) ? s : null;

        // ---- measuring, from the font in use ----

        public static int Measure(string line, int size = TextSize)
        {
            if (string.IsNullOrEmpty(line) || measurer == null)
                return 0;
            if (measurer.fontSize != size)
                measurer.fontSize = size;
            return Mathf.CeilToInt(measurer.GetPreferredValues(line).x - 0.01f);
        }

        public static readonly ShorelandsMods.Core.Ui.Measure Width = s => Measure(s);

        // One line of text at TextSize: at least the game's 10 units.
        public static int LineH
        {
            get
            {
                if (lineH == 0 && measurer != null)
                {
                    measurer.fontSize = TextSize;
                    lineH = Math.Max(10, Mathf.CeilToInt(measurer.GetPreferredValues("Ag|").y - 0.01f));
                }
                return lineH == 0 ? 10 : lineH;
            }
        }

        // ---- objects ----

        public static RectTransform Child(string name, Transform parent)
        {
            var t = (RectTransform)
                new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer }.transform;
            t.SetParent(parent, false);
            t.anchorMin = t.anchorMax = t.pivot = new Vector2(0f, 1f);
            return t;
        }

        // A nested canvas over a root canvas: same scale, its own sorting order.
        public static RectTransform Layer(string name, Canvas root, int order, bool raycast)
        {
            var t = Child(name, root.transform);
            t.gameObject.layer = LayerMask.NameToLayer("UI");
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = t.offsetMax = Vector2.zero;
            var c = t.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = order;
            if (raycast)
                t.gameObject.AddComponent<GraphicRaycaster>();
            return t;
        }

        // From the parent's top-left corner, y down, whole units.
        public static void Put(RectTransform r, int x, int y, int w, int h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        public static void Put(RectTransform r, URect u) => Put(r, u.X, u.Y, u.W, u.H);

        static void Style(TextMeshProUGUI t, int size)
        {
            t.font = Font;
            if (FontMaterial != null)
                t.fontSharedMaterial = FontMaterial;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.BottomLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.parseCtrlCharacters = false;
            t.raycastTarget = false;
        }

        // The game's label: bottom-aligned on its line, with its one-unit shadow (offset by the text scale).
        public static TextMeshProUGUI Label(string name, Transform parent, Color color, bool shadow, int scale = 1)
        {
            var r = Child(name, parent);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            Style(t, TextSize * scale);
            t.color = color;
            if (shadow)
                AddShadow(t, scale);
            return t;
        }

        // The game's shadow copies the label as a sibling with rich text on; host text is plain, so the copy is too.
        public static void AddShadow(TextMeshProUGUI t, int scale = 1)
        {
            t.gameObject.AddComponent<TextShadow>().offsetScale = scale;
            var parent = t.transform.parent;
            for (int i = 0; i < parent.childCount; i++)
                if (
                    parent.GetChild(i).name == t.name + "Shadow"
                    && parent.GetChild(i).TryGetComponent<TextMeshProUGUI>(out var copy)
                )
                {
                    copy.richText = false;
                    copy.parseCtrlCharacters = false;
                }
        }

        public static void SetText(TMP_Text t, string s)
        {
            if (t.text != s)
                t.text = s;
        }

        public static Image Plate(RectTransform r, Sprite sprite, Color color, bool raycast)
        {
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        // A plate button with a centred label, the game's tints.
        public static Button TextButton(string name, Transform parent, string text, out TextMeshProUGUI label)
        {
            var r = Child(name, parent);
            var img = Plate(r, Bg3, Color.white, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = ButtonColors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            label = Label("Label", r, Color.white, true);
            label.alignment = TextAlignmentOptions.Bottom;
            label.text = text;
            return b;
        }

        // Puts a button and centres its label (one line) inside it.
        public static void PutButton(Button b, TextMeshProUGUI label, URect u)
        {
            Put((RectTransform)b.transform, u);
            int h = LineH;
            Put(label.rectTransform, 0, (u.H - h) / 2 + 1, u.W, h);
        }

        public static Button CloseButton(Transform parent)
        {
            var r = Child("Close", parent);
            var img = Plate(r, Exit, Color.white, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = ButtonColors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        public static (int w, int h) CloseSize =>
            Exit != null ? (Mathf.RoundToInt(Exit.rect.width), Mathf.RoundToInt(Exit.rect.height)) : (11, 11);

        public static Color ToColor(Rgba c) => new Color((float)c.R, (float)c.G, (float)c.B, (float)c.A);

        // Icon size in canvas units for a list row: whole screen pixels per art pixel, at most maxUnits.
        public static float IconUnits(int artPx, float canvasScale, int maxUnits)
        {
            int s = Mathf.Max(1, Mathf.RoundToInt(canvasScale));
            int k = 1;
            while (artPx * (k + 1) / (float)s <= maxUnits)
                k++;
            return artPx * k / (float)s;
        }

        // A canvas-scale change leaves TMP labels drawn for the old scale (soft); this redraws them.
        public static void Redraw(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                t.SetAllDirty();
        }
    }
}
