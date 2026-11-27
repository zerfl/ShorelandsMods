using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Lua;
using Mirror;
using ReworkedCopium;
using ShorelandsMods.Core;

namespace ShorelandsMods.Game.Areas
{
    // The public offering countdown and the player's own offering flag. Other players' offerings and the results stay
    // unreadable.
    internal sealed class OfferingArea : Area
    {
        static OfferingArea instance;
        readonly Harmony harmony;
        readonly OfferingRounds rounds = new OfferingRounds();
        GameHook countdown,
            ownEvents;
        bool own,
            wasReady;

        public OfferingArea(Harmony harmony)
        {
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Offerings";

        protected internal override void Register(AreaSetup s)
        {
            countdown = s.Hook(
                "GlobalTimeManager.eventNetworkTime, NetworkTime.time",
                "the offering countdown",
                TouchCountdown
            );
            ownEvents = s.Hook(
                "PlayerUIController.UserCode_UpdateOfferingsTimer__Boolean",
                "your offering in the round",
                PatchOwn
            );
            s.Function(
                    "OfferingGetSecondsLeft() -> seconds:number|nil",
                    "seconds until the running offering round ends: the timer the game shows at the top left, the "
                        + "same for everyone. Rounds run back to back (usually 600 s); nil when you are not "
                        + "in the world and for a moment between two rounds",
                    c => c.Return(Left() is double left ? left : LuaValue.Nil)
                )
                .Needs(countdown, LuaValue.Nil);
            s.Function(
                    "OfferingHasOwn() -> has_own:boolean|nil",
                    "true while an offering of yours is in the running round (the game shows its timer in blue), "
                        + "false otherwise; nil when you are not in the world",
                    c => c.Return(Ready() ? own : LuaValue.Nil)
                )
                .Needs(ownEvents, LuaValue.Nil);
            s.Callback(
                "OnOfferingHasOwnChanged( has_own:boolean )",
                "true: your offering joined the running round; false: you have none in it any more (the round ended "
                    + "and its results came, which also means a new round started). The server repeats the state at "
                    + "login; this is called only when it differs from what OfferingHasOwn gave"
            );
            s.Callback(
                "OnOfferingRoundStarted( seconds:number )",
                "a new offering round started (the countdown jumped up); seconds: its time left. Not for the round "
                    + "that runs when you arrive in the world (read OfferingGetSecondsLeft)"
            );
        }

        bool Ready() => Host.Area<PlayerArea>()?.Ready == true;

        protected internal override void Update(double now)
        {
            bool ready = Ready();
            if (wasReady && !ready)
                own = false;
            wasReady = ready;
            if (!countdown.Available)
                return;
            double? left = Left();
            if (rounds.Next(now, ready, left))
                Host.Fire("OnOfferingRoundStarted", left.Value);
        }

        void Own(bool has)
        {
            if (has == own)
                return;
            own = has;
            Host.Post(() => Host.Fire("OnOfferingHasOwnChanged", has));
        }

        // ---- game access ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchCountdown()
        {
            var g = GlobalTimeManager.Instance;
            if (g != null)
                _ = g.eventNetworkTime - NetworkTime.time;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool PatchOwn()
        {
            var m = AccessTools.Method(
                typeof(ReworkedCopium.UI.PlayerUIController),
                "UserCode_UpdateOfferingsTimer__Boolean"
            );
            if (m == null)
                return false;
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(OfferingArea), nameof(OwnPostfix)));
            return true;
        }

        static void OwnPostfix(bool isOnOfferings)
        {
            try
            {
                instance.Own(isOnOfferings);
            }
            catch (Exception) { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static double? Left()
        {
            var g = GlobalTimeManager.Instance;
            if (g == null)
                return null;
            double left = g.eventNetworkTime - NetworkTime.time;
            return left > 0 ? left : (double?)null;
        }
    }
}
