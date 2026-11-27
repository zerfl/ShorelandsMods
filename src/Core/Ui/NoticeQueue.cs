using System;
using System.Collections.Generic;

namespace ShorelandsMods.Core.Ui
{
    internal sealed class Notice
    {
        public string ModId,
            Text;
        public double Seconds,
            ShownAt = -1;
        public int Slot;
    }

    // Notices wait in arrival order and show at most Visible at once, stacked in slots so none overlap; a slot is
    // given back when its notice has faded out. The same text from the same mod while it is shown or waiting is not
    // queued again (it restarts the shown one's time instead).
    internal sealed class NoticeQueue
    {
        public const int Visible = 3,
            MaxWaiting = 20;
        public const double FadeIn = 0.15,
            FadeOut = 0.35;

        readonly List<Notice> waiting = new List<Notice>();
        readonly Notice[] shown = new Notice[Visible];
        public int Dropped;

        public IEnumerable<Notice> Shown
        {
            get
            {
                foreach (var n in shown)
                    if (n != null)
                        yield return n;
            }
        }

        public int Waiting => waiting.Count;

        public void Add(string modId, string text, double seconds, double now)
        {
            foreach (var n in shown)
                if (n != null && n.ModId == modId && n.Text == text && now < n.ShownAt + n.Seconds)
                {
                    n.ShownAt = now;
                    n.Seconds = seconds;
                    return;
                }
            foreach (var n in waiting)
                if (n.ModId == modId && n.Text == text)
                    return;
            if (waiting.Count >= MaxWaiting)
            {
                waiting.RemoveAt(0);
                Dropped++;
            }
            waiting.Add(
                new Notice
                {
                    ModId = modId,
                    Text = text,
                    Seconds = seconds,
                }
            );
        }

        // True if what is shown changed (a notice appeared or went away).
        public bool Update(double now)
        {
            bool changed = false;
            for (int s = 0; s < shown.Length; s++)
                if (shown[s] != null && now >= shown[s].ShownAt + shown[s].Seconds + FadeOut)
                {
                    shown[s] = null;
                    changed = true;
                }
            for (int s = 0; s < shown.Length && waiting.Count > 0; s++)
                if (shown[s] == null)
                {
                    var n = waiting[0];
                    waiting.RemoveAt(0);
                    n.ShownAt = now;
                    n.Slot = s;
                    shown[s] = n;
                    changed = true;
                }
            return changed;
        }

        // 0..1: fading in, fully shown, fading out.
        public static double Alpha(Notice n, double now)
        {
            double t = now - n.ShownAt;
            if (t < FadeIn)
                return Math.Max(0, t / FadeIn);
            double end = n.Seconds;
            if (t <= end)
                return 1;
            return Math.Max(0, 1 - (t - end) / FadeOut);
        }

        public void RemoveMod(string modId)
        {
            waiting.RemoveAll(n => n.ModId == modId);
            for (int s = 0; s < shown.Length; s++)
                if (shown[s] != null && shown[s].ModId == modId)
                    shown[s] = null;
        }
    }
}
