using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ShorelandsMods.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TextFit = ShorelandsMods.Core.Ui.TextFit;

namespace ShorelandsMods.Game.Ui
{
    // Mod windows on a layer of the game's HUD canvas at order 170: above its trade and channel windows (150-160),
    // under its pop-ups (200) and the settings menu (500). The window plate is the settings window's, the close
    // cross the profile's; Escape closes the top window like the game's own (EscapeMenus).
    sealed class WindowStage : IWindowStage
    {
        public const int Order = 170;
        static readonly Color Stripe = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color ListBack = new Color32(0, 0, 0, 0x33);

        // A selected button looks held down (the game's pressed tint) with a gold label.
        static Color Pressed => UiKit.ButtonColors.pressedColor;

        sealed class Row
        {
            public Image Stripe,
                Icon;
            public TextMeshProUGUI Label;
        }

        internal sealed class View
        {
            public RectTransform Root;
            public ModWindowView Window;
            public TextMeshProUGUI Title;
            public HostField Search;
            public readonly List<(Button b, TextMeshProUGUI label)> Buttons = new List<(Button, TextMeshProUGUI)>();
            public readonly List<TextMeshProUGUI> TextLines = new List<TextMeshProUGUI>();
            public VirtualList List;
            public int ShownRowsVersion = -1,
                ShownCount = -1;
            public bool MissingIcons;
        }

        readonly Action<string> log;
        readonly Dictionary<ModWindow, View> views = new Dictionary<ModWindow, View>();
        RectTransform layer;
        Canvas root;
        float drawnAt;
        int iconUnitsAt;
        float iconUnits;
        public WindowArea Area;

        public WindowStage(Action<string> log)
        {
            this.log = log;
        }

        public bool Ready
        {
            get
            {
                if (layer != null && root != null && root.isActiveAndEnabled)
                {
                    if (root.scaleFactor != drawnAt)
                    {
                        drawnAt = root.scaleFactor;
                        UiKit.Redraw(layer);
                    }
                    return true;
                }
                var r = HudRoot();
                if (r == null || !UiKit.Resolve(log))
                    return false;
                root = r;
                layer = UiKit.Layer("ShorelandsMods windows", root, Order, true);
                views.Clear();
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Canvas HudRoot()
        {
            if (ReworkedCopium.Player.PlayerModel.LocalPlayer == null)
                return null;
            var hotbar = ReworkedCopium.GameManager.Instance?.hotbarView;
            return hotbar != null ? hotbar.GetComponentInParent<Canvas>()?.rootCanvas : null;
        }

        public int CanvasW => Mathf.RoundToInt(((RectTransform)root.transform).rect.width);
        public int CanvasH => Mathf.RoundToInt(((RectTransform)root.transform).rect.height);
        public int LineH => UiKit.LineH;

        // Icons draw at whole screen pixels per art pixel; a row is as tall as its icon or its text.
        float IconUnits
        {
            get
            {
                int s = Mathf.RoundToInt(root.scaleFactor);
                if (s != iconUnitsAt)
                {
                    iconUnitsAt = s;
                    iconUnits = UiKit.IconUnits(24, s, 16);
                }
                return iconUnits;
            }
        }

        public int RowH => Math.Max(LineH + 2, Mathf.CeilToInt(IconUnits) + 2);
        public (int w, int h) CloseSize => UiKit.CloseSize;

        public int MeasureText(string line) => UiKit.Measure(line);

        public void Build(ModWindow w)
        {
            // The search field moves to the new window: a text set while the player types (OnWindowSearchChanged) can
            // change the layout, and a new field would lose the focus, the caret and the keys in flight.
            var field = views.TryGetValue(w, out var old) && old.Root != null ? old.Search : null;
            if (field != null)
                field.transform.SetParent(layer, false);
            Destroy(w);
            var L = w.Layout;
            var v = new View { Root = UiKit.Child("ShorelandsMods window " + w.Key, layer) };
            UiKit.Plate(v.Root, UiKit.Bg2, Color.white, true);
            v.Window = v.Root.gameObject.AddComponent<ModWindowView>();
            v.Window.Stage = this;
            v.Window.Model = w;
            v.Title = UiKit.Label("Title", v.Root, Color.white, true);
            UiKit.Put(v.Title.rectTransform, L.Title);
            var close = UiKit.CloseButton(v.Root);
            UiKit.Put((RectTransform)close.transform, L.Close);
            close.onClick.AddListener(() => Area?.ClosedByPlayer(w));
            if (w.HasSearch)
            {
                if (field != null)
                    field.transform.SetParent(v.Root, false);
                else
                {
                    field = HostField.Create(v.Root, w.SearchHint, WindowArea.MaxSearchChars);
                    field.SetTextWithoutNotify(w.SearchText);
                    field.onValueChanged.AddListener(text => Area?.SearchChanged(w, text));
                }
                v.Search = field;
                UiKit.Put((RectTransform)field.transform, L.Search);
            }
            for (int i = 0; i < w.Buttons.Count && i < L.Buttons.Count; i++)
            {
                int index = i;
                var b = UiKit.TextButton("Button" + i, v.Root, w.Buttons[i].Label, out var label);
                UiKit.PutButton(b, label, L.Buttons[i]);
                b.onClick.AddListener(() => Area?.ButtonPressed(w, index));
                v.Buttons.Add((b, label));
            }
            for (int i = 0; i < L.TextLines; i++)
            {
                var t = UiKit.Label("Text" + i, v.Root, Color.white, true);
                UiKit.Put(t.rectTransform, L.Text.X, L.Text.Y + i * LineH, L.Text.W, LineH);
                v.TextLines.Add(t);
            }
            if (w.Rows != null && L.ListRows > 0)
            {
                var back = UiKit.Child("ListBack", v.Root);
                UiKit.Put(back, L.List);
                UiKit.Plate(back, UiKit.ChatBg, ListBack, false);
                int iconU = Mathf.CeilToInt(IconUnits);
                float iconExact = IconUnits;
                v.List = VirtualList.Create(
                    v.Root,
                    L.List,
                    L.Track,
                    L.RowH,
                    (slot, width) => MakeRow(slot, width, L.RowH, iconExact),
                    (o, index) => BindRow((Row)o, w, index, v)
                );
            }
            views[w] = v;
        }

        Row MakeRow(RectTransform slot, int width, int rowH, float icon)
        {
            var row = new Row();
            var stripe = UiKit.Child("Stripe", slot);
            UiKit.Put(stripe, 0, 0, width, rowH);
            row.Stripe = UiKit.Plate(stripe, null, Stripe, false);
            var ic = UiKit.Child("Icon", slot);
            ic.anchorMin = ic.anchorMax = ic.pivot = new Vector2(0f, 1f);
            ic.anchoredPosition = new Vector2(2, -Mathf.Floor((rowH - icon) / 2));
            ic.sizeDelta = new Vector2(icon, icon);
            row.Icon = ic.gameObject.AddComponent<Image>();
            row.Icon.raycastTarget = false;
            row.Label = UiKit.Label("Text", slot, Color.white, true);
            row.Label.rectTransform.sizeDelta = new Vector2(width, LineH);
            row.Label.rectTransform.anchoredPosition = new Vector2(0, -((rowH - LineH) / 2 + 1));
            return row;
        }

        void BindRow(Row row, ModWindow w, int index, View v)
        {
            bool on = index >= 0 && w.Shown != null && index < w.Shown.Count;
            row.Stripe.enabled = on && index % 2 == 0;
            if (!on)
            {
                UiKit.SetText(row.Label, "");
                row.Icon.enabled = false;
                return;
            }
            var r = w.Shown[index];
            if (!Icons.TryGet(r.Icon, out var sprite))
                v.MissingIcons = true;
            row.Icon.sprite = sprite;
            row.Icon.enabled = sprite != null;
            int x = 3 + (r.Icon != null ? Mathf.CeilToInt(row.Icon.rectTransform.sizeDelta.x) + 3 : 0);
            var rt = row.Label.rectTransform;
            float width = ((RectTransform)rt.parent).rect.width;
            if (rt.anchoredPosition.x != x)
            {
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
                rt.sizeDelta = new Vector2(width - x - 2, rt.sizeDelta.y);
            }
            int lw = Mathf.RoundToInt(rt.sizeDelta.x);
            if (r.Shown == null || r.ShownW != lw)
            {
                r.Shown = TextFit.Clip(r.Text, lw - 1, UiKit.Width);
                r.ShownW = lw;
            }
            UiKit.SetText(row.Label, r.Shown);
            row.Label.color = r.Color.HasValue ? UiKit.ToColor(r.Color.Value) : Color.white;
        }

        // Every frame, after Icons.Pump: icons that arrived are shown in the rows waiting for them.
        public void Tick(bool iconsArrived)
        {
            if (!iconsArrived)
                return;
            foreach (var v in views.Values)
                if (v.MissingIcons && v.List != null && v.Root != null && v.Root.gameObject.activeInHierarchy)
                {
                    v.MissingIcons = false;
                    v.List.Rebind();
                }
        }

        public void Refresh(ModWindow w)
        {
            if (!views.TryGetValue(w, out var v) || v.Root == null)
                return;
            UiKit.SetText(v.Title, w.Title);
            for (int i = 0; i < v.TextLines.Count; i++)
                UiKit.SetText(v.TextLines[i], i < w.TextLines.Count ? w.TextLines[i] : "");
            for (int i = 0; i < v.Buttons.Count && i < w.Buttons.Count; i++)
            {
                var (b, label) = v.Buttons[i];
                bool on = w.Buttons[i].Selected;
                UiKit.SetText(label, w.Buttons[i].Label);
                label.color = on ? UiKit.Gold : Color.white;
                b.targetGraphic.color = on ? Pressed : Color.white;
            }
            if (v.Search != null && v.Search.placeholder is TMP_Text hint)
                UiKit.SetText(hint, w.SearchHint);
            // WindowSetSearch; the player's own typing is already in SearchText.
            if (v.Search != null && v.Search.text != w.SearchText)
            {
                v.Search.SetTextWithoutNotify(w.SearchText);
                v.Search.MoveTextEnd(false);
            }
            if (v.List != null && (v.ShownRowsVersion != w.RowsVersion))
            {
                int count = w.Shown?.Count ?? 0;
                v.List.SetCount(count, count != v.ShownCount);
                v.ShownRowsVersion = w.RowsVersion;
                v.ShownCount = count;
            }
        }

        public void SetOpen(ModWindow w, bool open)
        {
            if (!views.TryGetValue(w, out var v) || v.Root == null)
                return;
            if (v.Root.gameObject.activeSelf != open)
                v.Root.gameObject.SetActive(open);
            if (open)
            {
                v.Root.SetAsLastSibling();
                ReworkedCopium.UI.EscapeMenus.Opened(v.Window);
            }
            else
                ReworkedCopium.UI.EscapeMenus.Closed(v.Window);
        }

        public void Place(ModWindow w)
        {
            if (views.TryGetValue(w, out var v) && v.Root != null)
                UiKit.Put(v.Root, w.Rect);
        }

        public void Destroy(ModWindow w)
        {
            if (!views.TryGetValue(w, out var v))
                return;
            views.Remove(w);
            if (v.Root == null)
                return;
            ReworkedCopium.UI.EscapeMenus.Closed(v.Window);
            UnityEngine.Object.Destroy(v.Root.gameObject);
        }

        internal View ViewOf(ModWindow w) => views.TryGetValue(w, out var v) ? v : null;

        internal float Scale => root != null ? root.scaleFactor : 1;

        internal void DragTo(ModWindow w, Vector2 topLeft)
        {
            if (!views.TryGetValue(w, out var v) || v.Root == null)
                return;
            var r = HudLayout.Clamp(
                new URect(Mathf.RoundToInt(topLeft.x), Mathf.RoundToInt(topLeft.y), w.Rect.W, w.Rect.H),
                CanvasW,
                CanvasH
            );
            UiKit.Put(v.Root, r);
            w.Rect = r;
        }
    }

    // The window object: the game's Escape closes it (IMenu); a left drag on its plate moves it, a right click puts
    // it back where it opens by default.
    sealed class ModWindowView
        : MonoBehaviour,
            ReworkedCopium.IMenu,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler,
            IPointerClickHandler,
            IPointerDownHandler
    {
        internal WindowStage Stage;
        internal ModWindow Model;
        Vector2 start,
            moved;
        bool dragging;

        public void Close() => Stage.Area?.ClosedByPlayer(Model);

        public void OnPointerDown(PointerEventData e) => transform.SetAsLastSibling();

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left)
                return;
            dragging = true;
            start = new Vector2(Model.Rect.X, Model.Rect.Y);
            moved = Vector2.zero;
            ReworkedCopium.TooltipSystem.Hide();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!dragging)
                return;
            moved += new Vector2(e.delta.x, -e.delta.y) / Stage.Scale;
            Stage.DragTo(Model, start + moved);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!dragging)
                return;
            dragging = false;
            Stage.Area?.Dragged(Model, Model.Rect);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right && e.pointerPress == gameObject)
                Stage.Area?.ResetPosition(Model);
        }
    }
}
