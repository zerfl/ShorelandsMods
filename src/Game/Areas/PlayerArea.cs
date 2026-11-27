using System.Runtime.CompilerServices;
using Lua;
using ReworkedCopium;
using ReworkedCopium.Player;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Game.Areas
{
    internal sealed class PlayerArea : Area
    {
        GameHook player,
            zone,
            zoneEvents;
        string activity;

        public bool Ready { get; private set; }

        public override string Name => "Player";

        protected internal override void Register(AreaSetup s)
        {
            player = s.Hook("PlayerModel.LocalPlayer, Name, currentState", "your name and activity", TouchPlayer);
            zone = s.Hook("MapInfo.Instance, Current, ArrivedAt", "the current zone", TouchZone);
            zoneEvents = s.Hook("MapInfo.OnArrived", "zone change events", SubscribeZone);
            s.Function(
                    "PlayerGetName() -> name:string|nil",
                    "your character's name as chat shows it; nil when you are not in the world",
                    c => c.Return(Ready ? LuaText.OrNil(NameNow()) : LuaValue.Nil)
                )
                .Needs(player, LuaValue.Nil);
            s.Function(
                    "PlayerGetActivity() -> activity:string|nil",
                    "\"idle\", \"walking\", \"farming\", \"fishing\" (every step of it, from the cast to the catch) or "
                        + "\"talking\"; nil when you are not in the world",
                    c => c.Return(Ready ? LuaText.OrNil(activity) : LuaValue.Nil)
                )
                .Needs(player, LuaValue.Nil);
            s.Function(
                    "ZoneGet() -> zone:string|nil",
                    "where you are: \"town\", \"playerland\" (the islands), \"holyland\" or \"unknown\"; nil when you are "
                        + "not in the world",
                    c => c.Return(Ready ? LuaText.OrNil(ZoneNow()) : LuaValue.Nil)
                )
                .Needs(zone, LuaValue.Nil);
            s.Callback(
                "OnPlayerReady()",
                "you are in the world: once after each login (when you arrive in your first zone), and when this mod "
                    + "loads or reloads while you are in the world. Reads give nil before it. It has init.lua's time "
                    + "budget (250 ms): the place to build lists from the world's tables (KoiGetAll)"
            );
            s.Callback(
                "OnZoneChanged( zone:string )",
                "you arrived in a zone, also the first one after login (zone as ZoneGet gives it)"
            );
            s.Callback(
                "OnActivityChanged( activity:string )",
                "your activity changed (activity as PlayerGetActivity gives it); fishing is one \"fishing\" from "
                    + "the cast to the catch, on purpose, so no step of it can be timed"
            );
        }

        protected internal override void Update(double now)
        {
            if (!player.Available || !zone.Available)
                return;
            bool ready = IsReady();
            if (ready != Ready)
            {
                Ready = ready;
                Host.Area<SoundArea>()?.SetZone(ready ? ZoneNow() : null);
                activity = ready ? Activities.Fold(StateNow()) ?? "idle" : null;
                if (ready)
                    Host.FireSetup("OnPlayerReady");
            }
            if (Ready)
                Observe(StateNow());
        }

        // The player's state by its enum name; fires OnActivityChanged when the folded value changes.
        public void Observe(string state)
        {
            string a = Activities.Fold(state);
            if (a == null || a == activity)
                return;
            activity = a;
            Host.Fire("OnActivityChanged", a);
        }

        protected internal override void ModStarted(Mod mod)
        {
            if (Ready)
                Host.FireOne(mod, "OnPlayerReady", Limits.Init);
        }

        void OnArrived(string zoneName)
        {
            var sound = Host.Area<SoundArea>();
            sound?.SetZone(zoneName);
            sound?.Event("zone_" + zoneName);
            Host.Post(() => Host.Fire("OnZoneChanged", zoneName));
        }

        // ---- game access (typed; only called once the hooks resolved) ----

        // A member a game update removed makes these throw when compiled, which marks the hook missing.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchPlayer()
        {
            var lp = PlayerModel.LocalPlayer;
            if (lp != null)
                _ = lp.Name + lp.currentState;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchZone()
        {
            var mi = MapInfo.Instance;
            if (mi != null)
                _ = mi.Current.ToString() + mi.ArrivedAt;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool SubscribeZone()
        {
            G.Area = this;
            MapInfo.OnArrived += G.Arrived;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool IsReady()
        {
            var mi = MapInfo.Instance;
            return PlayerModel.LocalPlayer != null && mi != null && !float.IsNegativeInfinity(mi.ArrivedAt);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string NameNow() => PlayerModel.LocalPlayer?.Name;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string StateNow() => PlayerModel.LocalPlayer?.currentState.ToString();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string ZoneNow() => MapInfo.Instance != null ? G.ZoneName(MapInfo.Instance.Current) : null;

        // Game-typed signatures live here, so a changed zone type cannot break loading this area.
        static class G
        {
            public static PlayerArea Area;

            public static void Arrived(MapZones z) => Area.InGameEvent(() => Area.OnArrived(ZoneName(z)));

            public static string ZoneName(MapZones z) => z.ToString().ToLowerInvariant();
        }
    }
}
