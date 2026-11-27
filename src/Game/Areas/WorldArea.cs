using System;
using System.Runtime.CompilerServices;
using Lua;
using ReworkedCopium;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Game.Areas
{
    // Ambience everyone sees: light and rain.
    internal sealed class WorldArea : Area
    {
        // Below this light level it counts as night; the world clock ramps the light from 1 to 0.4.
        const double NightBelow = 0.7;
        const double RampSeconds = 2;

        static WorldArea instance;
        GameHook dayNight,
            lightEvents;
        bool? raining;
        double lastLevel = double.NaN,
            lastChange = double.NegativeInfinity;
        int direction;

        public WorldArea()
        {
            instance = this;
        }

        public override string Name => "World";

        protected internal override void Register(AreaSetup s)
        {
            dayNight = s.Hook("NetworkDayNight.Instance, currentIntensity, isRaining", "light and rain", TouchDayNight);
            lightEvents = s.Hook("NetworkDayNight.OnIntensityUpdated", "light change events", SubscribeLight);
            s.Function(
                    "WeatherIsRaining() -> raining:boolean|nil",
                    "whether it rains where you are; nil when you are not in the world",
                    c => c.Return(HasDayNight() ? (LuaValue)Raining() : LuaValue.Nil)
                )
                .Needs(dayNight, LuaValue.Nil);
            s.Function(
                    "LightGetLevel() -> level:number|nil",
                    "the day/night light from 0 to 1 (1 = full day; the night is about 0.4); nil when you are not in "
                        + "the world",
                    c => c.Return(HasDayNight() ? (LuaValue)Level() : LuaValue.Nil)
                )
                .Needs(dayNight, LuaValue.Nil);
            s.Function(
                    "DayStateGet() -> state:string|nil",
                    "\"day\", \"to_night\" (dusk), \"night\" or \"to_day\" (dawn), from the light level (night below 0.7) "
                        + "and its direction while it changes; nil when you are not in the world",
                    c => c.Return(HasDayNight() ? (LuaValue)DayState(Level(), Host.Env.Now) : LuaValue.Nil)
                )
                .Needs(dayNight, LuaValue.Nil);
            s.Callback(
                "OnLightChanged( level:number )",
                "the server sent a new light level (see LightGetLevel): in steps during dusk and dawn, and once at login"
            );
            s.Callback(
                "OnRainChanged( raining:boolean )",
                "rain started (true) or stopped (false) where you are; not at login (read WeatherIsRaining)"
            );
        }

        protected internal override void Update(double now)
        {
            if (!dayNight.Available)
                return;
            var sound = Host.Area<SoundArea>();
            if (!HasDayNight())
            {
                raining = null;
                sound?.SetRain(false);
                return;
            }
            bool r = Raining();
            if (raining.HasValue && raining.Value != r)
            {
                sound?.Event(r ? "rain_start" : "rain_stop");
                Host.Fire("OnRainChanged", r);
            }
            raining = r;
            sound?.SetRain(r);
            sound?.SetNight(Level() < NightBelow);
        }

        // The light level from the game's event; also tracks its direction for DayStateGet.
        public void Light(double level)
        {
            double now = Host.Env.Now;
            if (!double.IsNaN(lastLevel) && level != lastLevel)
            {
                direction = Math.Sign(level - lastLevel);
                lastChange = now;
            }
            lastLevel = level;
            Host.Post(() => Host.Fire("OnLightChanged", level));
        }

        string DayState(double level, double now)
        {
            if (now - lastChange < RampSeconds && direction != 0)
                return direction < 0 ? "to_night" : "to_day";
            return level < NightBelow ? "night" : "day";
        }

        // ---- game access ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchDayNight()
        {
            var d = NetworkDayNight.Instance;
            if (d != null)
                _ = d.currentIntensity + (d.isRaining ? 1 : 0);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool SubscribeLight()
        {
            NetworkDayNight.OnIntensityUpdated += level => instance.InGameEvent(() => instance.Light(level));
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool HasDayNight() => NetworkDayNight.Instance != null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Raining() => NetworkDayNight.Instance.isRaining;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static double Level() => NetworkDayNight.Instance.currentIntensity;
    }
}
