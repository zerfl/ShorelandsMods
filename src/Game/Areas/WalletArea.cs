using System;
using System.Runtime.CompilerServices;
using Lua;
using ReworkedCopium.Player;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Game.Areas
{
    internal sealed class WalletArea : Area
    {
        GameHook wallet;
        object subscribed;
        readonly Action<int, int, int[]> onUpdate;
        readonly WalletChange change = new WalletChange();

        public WalletArea()
        {
            onUpdate = (currency, citb, stones) =>
                InGameEvent(() =>
                {
                    var (dc, dt) = change.Next(currency, citb, Host.Area<PlayerArea>()?.Ready == true);
                    if (dc != 0)
                        Host.Area<SoundArea>()?.Event(dc > 0 ? "wallet_gain" : "wallet_loss");
                    Host.Post(() => Host.Fire("OnWalletChanged", 4, new Args(currency, citb, dc, dt)));
                });
        }

        public override string Name => "Wallet";

        protected internal override void Register(AreaSetup s)
        {
            wallet = s.Hook(
                "PlayerWallet.LocalWallet, Currency, Citb, Otherstones, OnWalletUpdate",
                "your wallet",
                Touch
            );
            s.Function(
                    "WalletGetCurrency() -> currency:number|nil",
                    "your currency (the number on the wallet); nil when you are not in the world",
                    c => c.Return(Has() ? Currency() : LuaValue.Nil)
                )
                .Needs(wallet, LuaValue.Nil);
            s.Function("WalletGetCitb() -> citb:number|nil", "your CITB; nil when you are not in the world", c =>
                    c.Return(Has() ? Citb() : LuaValue.Nil)
                )
                .Needs(wallet, LuaValue.Nil);
            s.Function(
                    "WalletGetKoiCounts() -> counts:table|nil",
                    "the koi in your statue (not those in your bags: InventoryGetItems( \"koi\" )): { [koi id] = how "
                        + "many }; nil when you are not in the world",
                    c => c.Return(Has() ? KoiCounts() : LuaValue.Nil)
                )
                .Needs(wallet, LuaValue.Nil);
            s.Callback(
                "OnWalletChanged( currency:number, citb:number, currency_change:number, citb_change:number )",
                "your wallet changed: the new currency and CITB, and by how much each changed since the last call (a "
                    + "purchase gives a negative currency_change). Also when only the koi in your statue changed (both "
                    + "changes 0). The wallet at login is the starting point: its first call never reports the balance "
                    + "as a gain"
            );
        }

        protected internal override void Update(double now)
        {
            if (wallet.Available)
                Follow();
        }

        // Subscribes to the local wallet's event, again whenever the local wallet changes (login, logout).
        [MethodImpl(MethodImplOptions.NoInlining)]
        void Follow()
        {
            var w = PlayerWallet.LocalWallet;
            if (ReferenceEquals(w, subscribed))
                return;
            if (subscribed is PlayerWallet old && old != null)
                old.OnWalletUpdate -= onUpdate;
            if (w != null)
                w.OnWalletUpdate += onUpdate;
            subscribed = w;
            if (w != null)
                change.Baseline(w.Currency, w.Citb);
            else
                change.Reset();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Touch()
        {
            var w = PlayerWallet.LocalWallet;
            if (w != null)
                _ = w.Currency + w.Citb + (w.Otherstones?.Length ?? 0);
            return typeof(PlayerWallet).GetEvent("OnWalletUpdate") != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Has() => PlayerWallet.LocalWallet != null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Currency() => PlayerWallet.LocalWallet.Currency;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Citb() => PlayerWallet.LocalWallet.Citb;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LuaTable KoiCounts()
        {
            var t = new LuaTable();
            foreach (int id in PlayerWallet.LocalWallet.Otherstones ?? Array.Empty<int>())
                t[id] = (t[id].TryRead<double>(out var n) ? n : 0) + 1;
            return t;
        }
    }
}
