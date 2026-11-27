using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Lua;
using ReworkedCopium;
using ReworkedCopium.Inventory;
using ReworkedCopium.Items;
using ReworkedCopium.Messages;
using ReworkedCopium.Player;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Game.Areas
{
    internal sealed class InventoryArea : Area
    {
        static readonly string[] Which = { "hotbar", "farming", "koi", "pets", "all" };
        static InventoryArea instance;
        readonly Harmony harmony;
        GameHook inventories,
            toasts;
        object followed;
        readonly List<object> subscribedTo = new List<object>();
        object onChange,
            statueSeen;
        bool changed;
        readonly ItemGains gains = new ItemGains();

        public InventoryArea(Harmony harmony)
        {
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Inventory";

        protected internal override void Register(AreaSetup s)
        {
            inventories = s.Hook(
                "PlayerModel inventories (PlayerHotbar, farming/stone/petInventory), InventoryComponent.OnChange, "
                    + "PlayerWallet.Otherstones, Database.GetItem",
                "your inventories",
                Touch
            );
            toasts = s.Hook(
                "PlayerUIController.UserCode_ToastNotifyItem__NetworkItem, UserCode_ToastNotifyItems__List`1",
                "the item_gained sound",
                PatchToasts
            );
            s.Function(
                    "InventoryGetItems( which:string ) -> items:table|nil",
                    "your items as a list of { id, name, count, meta, category } (as OnItemGained; empty slots left "
                        + "out); which = \"hotbar\", \"farming\", \"koi\" (the koi bag, not the statue: "
                        + "WalletGetKoiCounts), \"pets\" or \"all\"; nil when you are not in the world and for another "
                        + "which",
                    Items
                )
                .Needs(inventories, LuaValue.Nil);
            s.Callback(
                "OnItemGained( item:table )",
                "you have more of an item than before: { id, name, count (how many more), meta, category }; a koi "
                    + "(id 9999) is named after the koi its meta selects. Every gain: a catch, a harvest, a purchase, a "
                    + "trade, offering rewards; moving items between your bags or into or out of the statue is none. "
                    + "Called once your items have not changed for "
                    + LuaText.Num(ItemGains.SettleSeconds)
                    + " s, one call per item; not for what you have when you arrive in the world"
            );
            s.Callback(
                "OnInventoryChanged()",
                "something in your bags changed: gained, lost, used or moved (at most once a frame; read "
                    + "InventoryGetItems)"
            );
        }

        int Items(ApiCall c)
        {
            string which = c.String(1, "which");
            if (Array.IndexOf(Which, which) >= 0)
                return c.Return(List(which));
            c.Refuse(
                1,
                "\""
                    + TextEscape.Clip(which, 40)
                    + "\" is not one of \"hotbar\", \"farming\", \"koi\", \"pets\", \"all\""
            );
            return c.Return(LuaValue.Nil);
        }

        protected internal override void Update(double now)
        {
            if (!inventories.Available)
                return;
            Follow();
            if (changed)
            {
                changed = false;
                gains.Changed(now);
                Host.Fire("OnInventoryChanged");
            }
            if (StatueChanged())
                gains.Changed(now);
            var got = gains.Update(now, Host.Area<PlayerArea>()?.Ready == true, Count);
            if (got != null)
                foreach (var (id, meta, count) in got)
                    Host.Fire("OnItemGained", () => ItemTable(id, count, meta));
        }

        // The statue's koi arrive as a new array whenever they change.
        [MethodImpl(MethodImplOptions.NoInlining)]
        bool StatueChanged()
        {
            var stones = PlayerWallet.LocalWallet?.Otherstones;
            if (ReferenceEquals(stones, statueSeen))
                return false;
            statueSeen = stones;
            return true;
        }

        // Everything the player has, by (item id, meta): the four inventories and the statue's koi (item 9999).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Dictionary<(int Id, int Meta), int> Count()
        {
            var counts = new Dictionary<(int Id, int Meta), int>();
            var lp = PlayerModel.LocalPlayer;
            if (lp == null)
                return counts;
            void Add(int id, int meta, int n) =>
                counts[(id, meta)] = (counts.TryGetValue((id, meta), out int had) ? had : 0) + n;
            foreach (
                var inv in new InventoryComponent[]
                {
                    Hotbar(lp),
                    lp.farmingInventory,
                    lp.stoneInventory,
                    lp.petInventory,
                }
            )
            foreach (var item in inv.InventoryItems)
                if (item.itemId != 0 && item.quantity > 0)
                    Add(item.itemId, item.meta, item.quantity);
            foreach (int koi in PlayerWallet.LocalWallet?.Otherstones ?? Array.Empty<int>())
                Add(KoiItemId, koi, 1);
            return counts;
        }

        const int KoiItemId = 9999;

        void ToastSound() => Host.Area<SoundArea>()?.Event("item_gained");

        // ---- game access ----

        // Game-typed state lives here, so a type a game update removed cannot break loading this area.
        static class G
        {
            public static AccessTools.FieldRef<PlayerModel, PlayerHotbar> Hotbar;
        }

        static PlayerHotbar Hotbar(PlayerModel lp) => G.Hotbar(lp);

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Touch()
        {
            G.Hotbar = AccessTools.FieldRefAccess<PlayerModel, PlayerHotbar>("PlayerHotbar");
            onChange = new Action<List<NetworkItem>>(_ => changed = true);
            var lp = PlayerModel.LocalPlayer;
            if (lp != null)
                _ =
                    Hotbar(lp).InventoryItems.Count
                    + lp.farmingInventory.Capacity
                    + lp.stoneInventory.Capacity
                    + lp.petInventory.Capacity;
            _ = PlayerWallet.LocalWallet?.Otherstones;
            _ = Database.Instance;
            return typeof(InventoryComponent).GetEvent("OnChange") != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool PatchToasts()
        {
            var one = AccessTools.Method(
                typeof(ReworkedCopium.UI.PlayerUIController),
                "UserCode_ToastNotifyItem__NetworkItem"
            );
            var many = AccessTools.Method(
                typeof(ReworkedCopium.UI.PlayerUIController),
                "UserCode_ToastNotifyItems__List`1"
            );
            if (one == null || many == null)
                return false;
            var toast = new HarmonyMethod(typeof(InventoryArea), nameof(Toast));
            Patches.All(harmony, (one, null, toast), (many, null, toast));
            return true;
        }

        static void Toast()
        {
            try
            {
                instance.ToastSound();
            }
            catch (Exception) { }
        }

        // Subscribes to the four inventories of the local player, again whenever the local player changes.
        [MethodImpl(MethodImplOptions.NoInlining)]
        void Follow()
        {
            var lp = PlayerModel.LocalPlayer;
            if (ReferenceEquals(lp, followed))
                return;
            var handler = (Action<List<NetworkItem>>)onChange;
            foreach (var o in subscribedTo)
                if (o is InventoryComponent inv && inv != null)
                    inv.OnChange -= handler;
            subscribedTo.Clear();
            followed = lp;
            if (lp == null)
                return;
            foreach (
                var inv in new InventoryComponent[]
                {
                    Hotbar(lp),
                    lp.farmingInventory,
                    lp.stoneInventory,
                    lp.petInventory,
                }
            )
            {
                inv.OnChange += handler;
                subscribedTo.Add(inv);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LuaValue List(string which)
        {
            var lp = PlayerModel.LocalPlayer;
            if (lp == null)
                return LuaValue.Nil;
            var t = new LuaTable();
            int n = 0;
            void Add(InventoryComponent inv)
            {
                foreach (var item in inv.InventoryItems)
                    if (item.itemId != 0 && item.quantity > 0)
                        t[++n] = ItemTable(item.itemId, item.quantity, item.meta);
            }
            if (which == "hotbar" || which == "all")
                Add(Hotbar(lp));
            if (which == "farming" || which == "all")
                Add(lp.farmingInventory);
            if (which == "koi" || which == "all")
                Add(lp.stoneInventory);
            if (which == "pets" || which == "all")
                Add(lp.petInventory);
            return t;
        }

        // { id, name, count, meta, category }; a koi (OtherStone) is named after the koi its meta selects.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LuaTable ItemTable(int id, int count, int meta)
        {
            var def = Database.Instance != null ? Database.GetItem(id) : null;
            string name = def != null ? def.displayName : "";
            if (def is OtherStone)
                name = Database.GetCosmiKoi(meta).name;
            var t = new LuaTable(0, 5);
            t["id"] = id;
            t["name"] = name ?? "";
            t["count"] = count;
            t["meta"] = meta;
            t["category"] = def != null ? CategoryName(def.category) : "none";
            return t;
        }

        public static string CategoryName(ItemCategory c)
        {
            foreach (
                ItemCategory flag in new[]
                {
                    ItemCategory.Farming,
                    ItemCategory.Stone,
                    ItemCategory.Eggs,
                    ItemCategory.Pets,
                    ItemCategory.Tool,
                }
            )
                if ((c & flag) != 0)
                    return flag.ToString().ToLowerInvariant();
            return "none";
        }
    }
}
