using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ShorelandsMods.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Ui
{
    // Draws HUD widgets on a layer of the game's HUD canvas (the hotbar's), under every game canvas: the game's own UI
    // always stays on top. Text is the game's labels; plates are its wallet plate; bars use its slider look.
    sealed class HudStage : IHudStage
    {
        const int PlatePadX = 4,
            PlatePadY = 3,
            IconPad = 2;
        static readonly Color BarBack = new Color32(0, 0, 0, 0x73);

        sealed class View
        {
            public RectTransform Root;
            public Image Plate,
                Icon,
                BarBg,
                BarFill;
            public readonly List<TextMeshProUGUI> Lines = new List<TextMeshProUGUI>();
            public HudDrag Drag;
        }

        readonly Action<string> log;
        readonly Dictionary<HudWidget, View> views = new Dictionary<HudWidget, View>();
        RectTransform layer;
        Canvas root;
        float drawnAt;
        public HudArea Area;
        readonly List<HudWidget> waitingIcons = new List<HudWidget>();

        // Icons.Pump brought icons: widgets drawn without theirs are drawn again in the next Flush.
        public void IconsArrived()
        {
            foreach (var w in waitingIcons)
                w.Dirty = true;
            waitingIcons.Clear();
        }

        public HudStage(Action<string> log)
        {
            this.log = log;
        }

        public bool Ready
        {
            get
            {
                if (layer != null && root != null && root.isActiveAndEnabled)
                    return CheckScale();
                var r = HudRoot();
                if (r == null || !UiKit.Resolve(log))
                    return false;
                root = r;
                layer = UiKit.Layer("ShorelandsMods HUD", root, -1, true);
                views.Clear();
                drawnAt = 0;
                return CheckScale();
            }
        }

        bool CheckScale()
        {
            if (root.scaleFactor != drawnAt)
            {
                drawnAt = root.scaleFactor;
                UiKit.Redraw(layer);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Canvas HudRoot()
        {
            if (ReworkedCopium.Player.PlayerModel.LocalPlayer == null)
                return null;
            var hotbar = ReworkedCopium.GameManager.Instance?.hotbarView;
            return hotbar != null ? hotbar.GetComponentInParent<Canvas>()?.rootCanvas : null;
        }

        public RectTransform Layer => layer;

        public int CanvasW => Mathf.RoundToInt(((RectTransform)root.transform).rect.width);
        public int CanvasH => Mathf.RoundToInt(((RectTransform)root.transform).rect.height);

        public int MeasureText(string line) => UiKit.Measure(line);

        // The game's HUD in canvas units: everything directly on its HUD canvas that is not a full-screen panel or an
        // open inventory, plus the chat window, the offering timer and the zone header.
        public List<URect> GameHud()
        {
            var list = new List<URect>();
            try
            {
                foreach (Transform t in root.transform)
                    if (t != layer && t.gameObject.activeSelf && t is RectTransform r && Small(r) && !Skip(t.name))
                        list.Add(Units(r));
                Collect(list);
            }
            catch (Exception) { }
            list.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            return list;
        }

        static bool Skip(string name) =>
            name == "InventoryPanel" || name == "GhostSlot" || name.StartsWith("ShorelandsMods");

        bool Small(RectTransform r) => r.rect.width < CanvasW / 2f && r.rect.height < CanvasH / 2f && r.rect.width > 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        void Collect(List<URect> list)
        {
            // The chat window's place is kept free also while it is closed, so widgets do not jump when it opens;
            // its tab bar and the open-chat button too.
            var chat = ReworkedCopium.Chat.ChatView.Instance;
            if (chat != null)
            {
                var t = HarmonyLib.Traverse.Create(chat);
                foreach (
                    var go in new[]
                    {
                        t.Field("chatWindow").GetValue<GameObject>(),
                        t.Field("openChatButton").GetValue<UnityEngine.UI.Button>()?.gameObject,
                        chat.transform.Find("TabHolder")?.gameObject,
                    }
                )
                    if (
                        go != null
                        && go.transform is RectTransform r
                        && (go.activeInHierarchy || go == t.Field("chatWindow").GetValue<GameObject>())
                    )
                        list.Add(Units(r));
            }
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c == root)
                    continue;
                var timer = c.name == "GlobalTimer" ? c.transform.Find("ClientTimer") as RectTransform : null;
                if (timer != null && timer.gameObject.activeInHierarchy)
                    list.Add(Units(timer));
                var header =
                    c.GetComponent<ReworkedCopium.UI.ChannelPanel>() != null
                        ? c.transform.Find("Header") as RectTransform
                        : null;
                if (header != null && header.gameObject.activeInHierarchy)
                    list.Add(Units(header));
            }
        }

        // A rect of any overlay canvas with the same scale, in this layer's units (top-left origin), grown to whole units.
        URect Units(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            float s = root.scaleFactor;
            float h = CanvasH;
            int x0 = Mathf.FloorToInt(c[0].x / s + 0.01f),
                x1 = Mathf.CeilToInt(c[2].x / s - 0.01f);
            int y0 = Mathf.FloorToInt(h - c[2].y / s + 0.01f),
                y1 = Mathf.CeilToInt(h - c[0].y / s - 0.01f);
            return new URect(x0, y0, x1 - x0, y1 - y0);
        }

        public void Show(HudWidget w)
        {
            if (!views.TryGetValue(w, out var v) || v.Root == null)
                views[w] = v = Create(w);
            v.Root.gameObject.SetActive(w.Visible);
            int pad = 0;
            switch (w.Kind)
            {
                case HudKind.Text:
                    pad = w.Plate ? PlatePadX : 0;
                    int padY = w.Plate ? PlatePadY : 0;
                    int lh = UiKit.LineH * w.Scale;
                    while (v.Lines.Count < w.Lines.Count)
                        v.Lines.Add(UiKit.Label("Line" + v.Lines.Count, v.Root, Color.white, true, w.Scale));
                    int widest = 0;
                    for (int i = 0; i < v.Lines.Count; i++)
                    {
                        var label = v.Lines[i];
                        bool on = i < w.Lines.Count;
                        label.gameObject.SetActive(on);
                        if (!on)
                            continue;
                        if (label.fontSize != UiKit.TextSize * w.Scale)
                            label.fontSize = UiKit.TextSize * w.Scale;
                        UiKit.SetText(label, w.Lines[i]);
                        label.color = UiKit.ToColor(w.Color);
                        int lw = UiKit.Measure(w.Lines[i], UiKit.TextSize * w.Scale);
                        widest = Math.Max(widest, lw);
                        UiKit.Put(label.rectTransform, pad, padY + i * lh, Math.Max(1, lw), lh);
                    }
                    w.W = widest + 2 * pad + w.Scale;
                    w.H = w.Lines.Count * lh + 2 * padY + (w.Plate ? 0 : w.Scale);
                    break;
                case HudKind.Icon:
                    if (!Icons.Get(w.Icon, out var sprite))
                        waitingIcons.Add(w);
                    v.Icon.sprite = sprite;
                    v.Icon.enabled = sprite != null;
                    v.Icon.color = UiKit.ToColor(w.Color);
                    int iw = sprite != null ? Mathf.RoundToInt(sprite.rect.width) : 24,
                        ih = sprite != null ? Mathf.RoundToInt(sprite.rect.height) : 24;
                    pad = w.Plate ? IconPad : 0;
                    UiKit.Put(v.Icon.rectTransform, pad, pad, iw * w.Scale, ih * w.Scale);
                    w.W = iw * w.Scale + 2 * pad;
                    w.H = ih * w.Scale + 2 * pad;
                    break;
                default:
                    pad = w.Plate ? PlatePadY : 0;
                    UiKit.Put(v.BarBg.rectTransform, pad, pad, w.BarW, w.BarH);
                    int inset = w.BarH >= 4 ? 1 : 0;
                    int fill = (int)Math.Round((w.BarW - 2 * inset) * w.Fraction);
                    UiKit.Put(v.BarFill.rectTransform, pad + inset, pad + inset, fill, w.BarH - 2 * inset);
                    v.BarFill.enabled = fill > 0;
                    v.BarFill.color = UiKit.ToColor(w.Color);
                    w.W = w.BarW + 2 * pad;
                    w.H = w.BarH + 2 * pad;
                    break;
            }
            v.Plate.enabled = w.Plate;
            v.Plate.raycastTarget = w.Draggable;
            v.Drag.enabled = w.Draggable;
            UiKit.Put(v.Plate.rectTransform, 0, 0, w.W, w.H);
        }

        View Create(HudWidget w)
        {
            var v = new View { Root = UiKit.Child("ShorelandsMods " + w.Key, layer) };
            var plate = UiKit.Child("Plate", v.Root);
            v.Plate = UiKit.Plate(plate, UiKit.Bg2, Color.white, false);
            if (w.Kind == HudKind.Icon)
            {
                v.Icon = UiKit.Child("Icon", v.Root).gameObject.AddComponent<Image>();
                v.Icon.raycastTarget = false;
                v.Icon.preserveAspect = false;
            }
            else if (w.Kind == HudKind.Bar)
            {
                v.BarBg = UiKit.Plate(UiKit.Child("Back", v.Root), UiKit.ChatBg, BarBack, false);
                v.BarFill = UiKit.Plate(UiKit.Child("Fill", v.Root), UiKit.ChatBg, Color.white, false);
            }
            v.Drag = v.Root.gameObject.AddComponent<HudDrag>();
            v.Drag.Stage = this;
            v.Drag.Widget = w;
            return v;
        }

        public void Place(HudWidget w)
        {
            if (views.TryGetValue(w, out var v) && v.Root != null)
                UiKit.Put(v.Root, w.Rect);
        }

        public void Remove(HudWidget w)
        {
            if (views.TryGetValue(w, out var v))
            {
                views.Remove(w);
                if (v.Root != null)
                    UnityEngine.Object.Destroy(v.Root.gameObject);
            }
        }

        // Dragging, in whole units, kept on the canvas; the host saves where it was dropped.
        internal void DragTo(HudWidget w, Vector2 topLeft)
        {
            if (!views.TryGetValue(w, out var v) || v.Root == null)
                return;
            var r = HudLayout.Clamp(
                new URect(Mathf.RoundToInt(topLeft.x), Mathf.RoundToInt(topLeft.y), w.W, w.H),
                CanvasW,
                CanvasH
            );
            UiKit.Put(v.Root, r);
            w.Rect = r;
        }

        internal void Dropped(HudWidget w) => Area?.Dragged(w, w.Rect);

        internal void Reset(HudWidget w) => Area?.ResetPosition(w);

        internal float Scale => root != null ? root.scaleFactor : 1;
    }

    // Left drag moves a draggable widget; right click puts it back (like the game's own draggable windows).
    sealed class HudDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        internal HudStage Stage;
        internal HudWidget Widget;
        Vector2 start,
            moved;

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left)
                return;
            start = new Vector2(Widget.Rect.X, Widget.Rect.Y);
            moved = Vector2.zero;
            ReworkedCopium.TooltipSystem.Hide();
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left)
                return;
            moved += new Vector2(e.delta.x, -e.delta.y) / Stage.Scale;
            Stage.DragTo(Widget, start + moved);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                Stage.Dropped(Widget);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right)
                Stage.Reset(Widget);
        }
    }
}
