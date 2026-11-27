using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ShorelandsMods.Core.Ui;
using TMPro;
using UnityEngine;

namespace ShorelandsMods.Game.Ui
{
    // Notices at the top centre, under the game's zone header and the HUD widgets stacked there, oldest on top: the
    // game's plate with its labels, fading in and out as a whole.
    sealed class NoticeStage : INoticeStage
    {
        // Under the game's windows (150) and mod windows: a window in use is never covered by a notice.
        public const int Order = 140,
            Top = 24,
            PadX = 6,
            PadY = 4,
            Gap = 3;

        sealed class View
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public readonly List<TextMeshProUGUI> Lines = new List<TextMeshProUGUI>();
        }

        readonly Action<string> log;
        readonly Dictionary<Notice, View> views = new Dictionary<Notice, View>();
        RectTransform layer;
        Canvas root;
        float drawnAt;

        // Below the HUD widgets stacked at the top centre, so a notice does not cover them.
        public Func<int> TopFrom;

        public NoticeStage(Action<string> log)
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
                layer = UiKit.Layer("ShorelandsMods notices", root, Order, false);
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

        public int MeasureText(string line) => UiKit.Measure(line);

        public void Show(IReadOnlyList<(Notice notice, List<string> lines)> shown)
        {
            var keep = new HashSet<Notice>();
            int canvasW = Mathf.RoundToInt(((RectTransform)root.transform).rect.width);
            int y = Math.Max(Top, TopFrom != null ? TopFrom() + Gap : 0);
            int lh = UiKit.LineH;
            foreach (var (n, lines) in shown)
            {
                keep.Add(n);
                if (!views.TryGetValue(n, out var v) || v.Root == null)
                {
                    v = new View { Root = UiKit.Child("Notice", layer) };
                    UiKit.Plate(v.Root, UiKit.Bg2, Color.white, false);
                    v.Group = v.Root.gameObject.AddComponent<CanvasGroup>();
                    v.Group.alpha = 0;
                    v.Group.blocksRaycasts = false;
                    foreach (var line in lines)
                    {
                        var t = UiKit.Label("Line", v.Root, v.Lines.Count == 0 ? UiKit.ModTint : Color.white, true);
                        t.text = line;
                        v.Lines.Add(t);
                    }
                    views[n] = v;
                }
                int widest = 0;
                foreach (var line in lines)
                    widest = Math.Max(widest, UiKit.Measure(line));
                int w = widest + 2 * PadX,
                    h = lines.Count * lh + 2 * PadY;
                for (int i = 0; i < v.Lines.Count; i++)
                    UiKit.Put(v.Lines[i].rectTransform, PadX, PadY + i * lh, widest + 1, lh);
                UiKit.Put(v.Root, (canvasW - w) / 2, y, w, h);
                y += h + Gap;
            }
            foreach (var n in new List<Notice>(views.Keys))
                if (!keep.Contains(n))
                {
                    if (views[n].Root != null)
                        UnityEngine.Object.Destroy(views[n].Root.gameObject);
                    views.Remove(n);
                }
        }

        public void Fade(Notice n, double alpha)
        {
            if (views.TryGetValue(n, out var v) && v.Root != null)
                v.Group.alpha = (float)alpha;
        }
    }
}
