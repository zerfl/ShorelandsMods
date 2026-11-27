using System.Collections.Generic;
using System.Linq;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Core.Ui
{
    internal interface INoticeStage
    {
        bool Ready { get; }
        int MeasureText(string line);

        // The notices shown now, oldest first, each with its lines: the mod's id in brackets (drawn in the mod tint),
        // then the wrapped text; called when the set changes.
        void Show(IReadOnlyList<(Notice notice, List<string> lines)> shown);

        // Every frame while notices show: their opacity.
        void Fade(Notice n, double alpha);
    }

    // NoticeShow: a short text in the game's look at the top of the screen, queued so notices never overlap.
    internal sealed class NoticeArea : Area
    {
        public const int MaxWidth = 220,
            MaxLines = 4,
            MaxChars = 300,
            PerSecond = 5;

        readonly INoticeStage stage;
        readonly NoticeQueue queue = new NoticeQueue();
        bool wasReady;

        public NoticeArea(INoticeStage stage)
        {
            this.stage = stage;
        }

        public override string Name => "Notices";

        public NoticeQueue Queue => queue;

        sealed class State
        {
            public readonly RateLimit Rate = new RateLimit(PerSecond);
        }

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "NoticeShow( text:string, [seconds:number] ) -> ok:boolean",
                "shows a short text at the top centre for seconds (default 3, 0.5 to 30) in the game's look, under "
                    + "your mod's id in brackets in the mods' violet; up to 4 wrapped lines, at most 3 at once (the rest wait their turn), at most "
                    + PerSecond
                    + " a second per mod (more are dropped)",
                c =>
                {
                    double seconds = c.OptClamp(2, "seconds", 3, 0.5, 30);
                    string text = c.Text(1, "text");
                    if (text.Length > MaxChars)
                        text = text.Substring(0, MaxChars);
                    if (c.Mod.Get<State>().Rate.Take(Host.Env.Now))
                        queue.Add(c.Mod.Id, TextEscape.PlainLines(text), seconds, Host.Env.Now);
                    return c.ReturnOk();
                }
            );
        }

        protected internal override void ModStopping(Mod mod)
        {
            queue.RemoveMod(mod.Id);
            Publish();
        }

        public void Flush(double now)
        {
            if (!stage.Ready)
            {
                wasReady = false;
                return;
            }
            if (queue.Update(now) || !wasReady)
                Publish();
            wasReady = true;
            foreach (var n in queue.Shown)
                stage.Fade(n, NoticeQueue.Alpha(n, now));
        }

        void Publish()
        {
            if (!stage.Ready)
                return;
            stage.Show(
                queue
                    .Shown.OrderBy(n => n.ShownAt)
                    .Select(n =>
                        (
                            n,
                            new[] { "[" + n.ModId + "]" }
                                .Concat(TextFit.Wrap(n.Text, MaxWidth, MaxLines, stage.MeasureText))
                                .ToList()
                        )
                    )
                    .ToList()
            );
        }
    }
}
