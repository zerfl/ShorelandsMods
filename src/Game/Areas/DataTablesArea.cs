using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Lua;
using ReworkedCopium;
using ReworkedCopium.Farming;
using ReworkedCopium.Messages;
using ShorelandsMods.Core;

namespace ShorelandsMods.Game.Areas
{
    // Static game data every client has: items, koi, crops. Available once the world's Database exists.
    internal sealed class DataTablesArea : Area
    {
        GameHook items,
            koi,
            crops;

        public override string Name => "Data tables";

        static class G
        {
            public static AccessTools.FieldRef<Database, Dictionary<int, CosmiKoiData>> KoiDb;
        }

        protected internal override void Register(AreaSetup s)
        {
            items = s.Hook("Database.GetItem, ItemDataSO fields", "item data", TouchItems);
            koi = s.Hook("Database.KoiDB (private)", "koi data", TouchKoi);
            crops = s.Hook("Database.GetCrop, CropSO fields", "crop data", TouchCrops);
            s.Function(
                    "ItemGetInfo( id:number ) -> info:table|nil",
                    "the item's { id, name, description, category (\"farming\", \"stone\", \"eggs\", \"pets\", \"tool\" or "
                        + "\"none\"), max_stack, tradeable, usable } from the game's tables; nil for an unknown id and "
                        + "until the world's tables are loaded (at the first login)",
                    c => c.Return(c.Whole(1, "id") is int id ? Item(id) : LuaValue.Nil)
                )
                .Needs(items, LuaValue.Nil);
            s.Function(
                    "KoiGetInfo( id:number ) -> info:table|nil",
                    "a koi's { id, name, type } (type: the colour it swims in, as the game numbers it: 1 blue, 2 dark blue, 3 aqua, "
                        + "4 green, 5 yellow, 6 red, 7 pink, 8 purple; 0 for #0 \"None\", the table's empty entry); nil for an unknown id and "
                        + "until the world's tables are loaded (at the first login)",
                    c => c.Return(c.Whole(1, "id") is int id ? Koi(id) : LuaValue.Nil)
                )
                .Needs(koi, LuaValue.Nil);
            s.Function(
                    "KoiGetAll() -> list:table|nil",
                    "every koi as a list of { id, name, type } (as KoiGetInfo), by id, starting with #0 \"None\" (about 1,800; about 2 ms, so call it once, in "
                        + "OnPlayerReady, and keep the result); nil until the world's tables are loaded (at the first login)",
                    c => c.Return(AllKoi())
                )
                .Needs(koi, LuaValue.Nil);
            s.Function(
                    "CropGetInfo( id:number ) -> info:table|nil",
                    "a crop's { id, produce (the item id it gives), produce_amount, growth_time (seconds), stages }; nil "
                        + "for an unknown crop id and until the world's tables are loaded (at the first login)",
                    c => c.Return(c.Whole(1, "id") is int id ? Crop(id) : LuaValue.Nil)
                )
                .Needs(crops, LuaValue.Nil);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchItems()
        {
            var d = Database.Instance != null ? Database.GetItem(1) : null;
            if (d != null)
                _ = d.displayName + d.description + d.category + d.maxStackSize + d.tradeable + d.usable;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchKoi()
        {
            G.KoiDb = AccessTools.FieldRefAccess<Database, Dictionary<int, CosmiKoiData>>("KoiDB");
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchCrops()
        {
            var c = Database.Instance != null ? Database.GetCrop(1) : null;
            if (c != null)
                _ =
                    c.id
                    + c.produceAmount
                    + c.growthTime
                    + (c.produce != null ? c.produce.id : 0)
                    + c.growthStages.Length;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LuaValue Item(int id)
        {
            var d = Database.Instance != null ? Database.GetItem(id) : null;
            if (d == null)
                return LuaValue.Nil;
            var t = new LuaTable(0, 7);
            t["id"] = d.id;
            t["name"] = d.displayName ?? "";
            t["description"] = d.description ?? "";
            t["category"] = InventoryArea.CategoryName(d.category);
            t["max_stack"] = d.maxStackSize;
            t["tradeable"] = d.tradeable;
            t["usable"] = d.usable;
            return t;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LuaValue Koi(int id)
        {
            if (Database.Instance == null || !G.KoiDb(Database.Instance).TryGetValue(id, out var k))
                return LuaValue.Nil;
            return KoiTable(k);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LuaValue AllKoi()
        {
            if (Database.Instance == null)
                return LuaValue.Nil;
            var all = G.KoiDb(Database.Instance);
            var t = new LuaTable(all.Count, 0);
            int i = 0;
            foreach (var kv in all.OrderBy(kv => kv.Key))
                t[++i] = KoiTable(kv.Value);
            return t;
        }

        static LuaTable KoiTable(CosmiKoiData k)
        {
            var t = new LuaTable(0, 3);
            t["id"] = k.id;
            t["name"] = k.name ?? "";
            t["type"] = k.type;
            return t;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LuaValue Crop(int id)
        {
            if (Database.Instance == null || !Database.IsValidCrop(id))
                return LuaValue.Nil;
            CropSO c = Database.GetCrop(id);
            var t = new LuaTable(0, 5);
            t["id"] = c.id;
            t["produce"] = c.produce != null ? c.produce.id : 0;
            t["produce_amount"] = c.produceAmount;
            t["growth_time"] = c.growthTime;
            t["stages"] = c.growthStages != null ? c.growthStages.Length : 0;
            return t;
        }
    }
}
