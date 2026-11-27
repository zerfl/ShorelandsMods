using System;
using System.Collections.Generic;
using ShorelandsMods.Core.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShorelandsMods.Game.Ui
{
    // A long list drawn with as many row objects as fit, re-bound when it scrolls (ListScroll does the arithmetic).
    // The wheel scrolls three rows; a click or drag on the track jumps there.
    sealed class VirtualList : MonoBehaviour, IScrollHandler
    {
        const int WheelRows = 3;
        static readonly Color TrackColor = new Color32(0xFF, 0xFF, 0xFF, 0x1F);
        static readonly Color MarkColor = new Color32(0xFF, 0xFF, 0xFF, 0x80);

        RectTransform rows,
            track,
            mark;
        Image trackImg;
        readonly List<RectTransform> slots = new List<RectTransform>();
        Func<RectTransform, int, object> make;
        Action<object, int> bind;
        readonly List<object> slotData = new List<object>();
        ListScroll view;
        int rowH,
            width;

        public ListScroll View => view;
        public int RowH => rowH;

        // rowsRect: where the rows go (a background is the caller's); trackRect: the scroll track beside it.
        public static VirtualList Create(
            Transform parent,
            URect rowsRect,
            URect trackRect,
            int rowH,
            Func<RectTransform, int, object> make,
            Action<object, int> bind
        )
        {
            var r = UiKit.Child("List", parent);
            UiKit.Put(r, rowsRect.X, rowsRect.Y, trackRect.Right - rowsRect.X, rowsRect.H);
            var bg = r.gameObject.AddComponent<Image>();
            bg.color = Color.clear;
            var list = r.gameObject.AddComponent<VirtualList>();
            list.rows = UiKit.Child("Rows", r);
            UiKit.Put(list.rows, 0, 0, rowsRect.W, rowsRect.H);
            list.rows.gameObject.AddComponent<RectMask2D>();
            list.track = UiKit.Child("Track", r);
            UiKit.Put(list.track, trackRect.X - rowsRect.X, 0, trackRect.W, trackRect.H);
            list.trackImg = UiKit.Plate(list.track, UiKit.ChatBg, TrackColor, true);
            list.track.gameObject.AddComponent<TrackInput>().List = list;
            list.mark = UiKit.Child("Mark", list.track);
            UiKit.Plate(list.mark, UiKit.ChatBg, MarkColor, false);
            list.make = make;
            list.bind = bind;
            list.rowH = rowH;
            list.width = rowsRect.W;
            int n = Math.Max(0, rowsRect.H / rowH);
            for (int i = 0; i < n; i++)
            {
                var s = UiKit.Child("Row" + i, list.rows);
                UiKit.Put(s, 0, i * rowH, rowsRect.W, rowH);
                list.slots.Add(s);
                list.slotData.Add(make(s, rowsRect.W));
            }
            list.view = new ListScroll(n, 0, 0);
            return list;
        }

        public void SetCount(int count, bool toTop)
        {
            view = new ListScroll(view.Rows, count, toTop ? 0 : view.Offset);
            Rebind();
        }

        public void Reveal(int index)
        {
            view = view.Reveal(index);
            Rebind();
        }

        public void Rebind()
        {
            for (int i = 0; i < slots.Count; i++)
                bind(slotData[i], view.IndexAt(i));
            int trackH = view.Rows * rowH;
            var (y, h) = view.Mark(trackH);
            UiKit.Put(mark, 0, y, track.rect.width > 0 ? Mathf.RoundToInt(track.rect.width) : 3, h);
            mark.gameObject.SetActive(view.CanScroll);
            trackImg.enabled = view.CanScroll;
        }

        public void ScrollBy(int n)
        {
            var next = view.ScrollBy(n);
            if (next.Offset == view.Offset)
                return;
            view = next;
            Rebind();
        }

        public void OnScroll(PointerEventData e)
        {
            if (e.scrollDelta.y != 0)
                ScrollBy(e.scrollDelta.y > 0 ? -WheelRows : WheelRows);
        }

        internal void OnTrack(PointerEventData e)
        {
            if (
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    track,
                    e.position,
                    e.pressEventCamera,
                    out var local
                )
            )
                return;
            int y = Mathf.RoundToInt(track.rect.yMax - local.y);
            var next = view.AtTrack(y, view.Rows * rowH);
            if (next.Offset == view.Offset)
                return;
            view = next;
            Rebind();
        }

        // The row slot under a screen point, or -1.
        public int SlotAt(Vector2 screen)
        {
            for (int i = 0; i < slots.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(slots[i], screen, null))
                    return i;
            return -1;
        }

        public RectTransform Slot(int i) => slots[i];

        public int SlotCount => slots.Count;
    }

    sealed class TrackInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IBeginDragHandler
    {
        internal VirtualList List;

        public void OnPointerDown(PointerEventData e) => List.OnTrack(e);

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e) => List.OnTrack(e);
    }
}
