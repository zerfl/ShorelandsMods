using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    internal sealed class TimeArea : Area
    {
        sealed class Timer
        {
            public int Id;
            public LuaFunction Fn;
            public string What;
            public double Due,
                Every;
            public bool Dead;
        }

        sealed class State
        {
            public readonly List<Timer> Timers = new List<Timer>();
            public int NextId = 1;

            // The first timer to look at in the next frame: where the frame share stopped this one.
            public int ResumeId;
        }

        public override string Name => "Time";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "TimeGetSeconds() -> seconds:number",
                "real seconds since this mod was loaded (starts again at 0 when it reloads); os.clock() counts from the game's start",
                c => c.Return(Host.Env.Now - c.Mod.LoadedAt)
            );
            s.Function(
                "TimerAfter( seconds:number, fn:function ) -> id:number|nil",
                "calls fn() once after seconds (0 to 86400, real time); the id is for TimerCancel; nil when the mod "
                    + "already has "
                    + Limits.TimersPerMod
                    + " timers",
                c => Add(c, false)
            );
            s.Function(
                "TimerEvery( seconds:number, fn:function ) -> id:number|nil",
                "calls fn() every seconds ("
                    + LuaText.Num(Limits.TimerMinEvery)
                    + " to 86400, real time) until TimerCancel( id ) or the mod stops; nil when the mod already has "
                    + Limits.TimersPerMod
                    + " timers",
                c => Add(c, true)
            );
            s.Function(
                "TimerCancel( id:number )",
                "stops a timer; an id that already ran, was cancelled or never existed is ignored",
                c =>
                {
                    int? id = c.Whole(1, "id");
                    if (id.HasValue)
                        c.Mod.Resources.Remove("timer", id.Value.ToString(CultureInfo.InvariantCulture));
                    return c.Return();
                }
            );
        }

        int Add(ApiCall c, bool repeat)
        {
            double seconds = c.Clamp(1, "seconds", repeat ? Limits.TimerMinEvery : 0, 86400);
            var fn = c.Func(2, "fn");
            var st = c.Mod.Get<State>();
            if (c.Mod.Resources.CountOf("timer") >= Limits.TimersPerMod)
            {
                c.Refuse(0, "the mod already has " + Limits.TimersPerMod + " timers, so no new one was started");
                return c.Return(LuaValue.Nil);
            }
            var t = new Timer
            {
                Id = st.NextId++,
                Fn = fn,
                What = (repeat ? "the TimerEvery function" : "the TimerAfter function") + LuaText.Where(fn),
                Due = Host.Env.Now + seconds,
                Every = repeat ? seconds : 0,
            };
            st.Timers.Add(t);
            c.Mod.Resources.Add("timer", t.Id.ToString(CultureInfo.InvariantCulture), () => t.Dead = true);
            return c.Return(t.Id);
        }

        protected internal override IEnumerable<LuaFunction> Held(Mod mod) =>
            mod.Find<State>()?.Timers.Where(t => !t.Dead).Select(t => t.Fn) ?? Enumerable.Empty<LuaFunction>();

        // Due timers in creation order, starting where the last frame stopped. Once the mod used its frame share,
        // the rest wait for the next frame (at least one runs), so no mod's timers can fill a frame.
        protected internal override void Update(double now)
        {
            var mods = Host.Mods;
            for (int m = 0; m < mods.Count; m++)
            {
                var mod = mods[m];
                var st = mod.Find<State>();
                if (st == null || mod.Status != ModStatus.Loaded)
                    continue;
                var timers = st.Timers;
                int n = timers.Count,
                    start = 0;
                while (start < n && timers[start].Id < st.ResumeId)
                    start++;
                st.ResumeId = 0;
                bool ran = false;
                for (int k = 0; k < n && mod.Status == ModStatus.Loaded; k++)
                {
                    var t = timers[(start + k) % n];
                    if (t.Dead || now < t.Due)
                        continue;
                    if (ran && mod.Load.FrameMs(Host.Frame) >= Limits.FrameShareMs)
                    {
                        st.ResumeId = t.Id;
                        break;
                    }
                    if (t.Every > 0)
                        t.Due = now - t.Due >= t.Every ? now + t.Every : t.Due + t.Every;
                    else
                    {
                        t.Dead = true;
                        mod.Resources.Forget("timer", t.Id.ToString(CultureInfo.InvariantCulture));
                    }
                    ran = true;
                    Host.CallTimer(mod, t.What, t.Fn);
                }
                timers.RemoveAll(t => t.Dead);
            }
        }
    }
}
