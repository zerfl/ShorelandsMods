using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Lua;
using ReworkedCopium.Chat;
using ReworkedCopium.Inventory;
using ReworkedCopium.Messages;
using ReworkedCopium.Player;
using ReworkedCopium.Shop;
using ReworkedCopium.UI;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;
using ShorelandsMods.Game.Areas;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShorelandsMods.Game.Ui
{
    // Chat decoration in the game: each line's arrival time is kept (the line's text is swapped for an equal copy so
    // the copy identifies it), and the line the chat formats is decorated as it is drawn. Every hook catches
    // everything: an exception while the chat draws blanks it until restart.
    sealed class ChatDisplayHooks : Area
    {
        static ChatDisplayHooks instance;
        static readonly ConditionalWeakTable<string, Arrival> arrivals = new ConditionalWeakTable<string, Arrival>();
        readonly Harmony harmony;
        ChatDecorArea decor;
        static bool failed;

        sealed class Arrival
        {
            public DateTime At;
        }

        public ChatDisplayHooks(Harmony harmony)
        {
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Chat display hooks";

        protected internal override void Register(AreaSetup s)
        {
            var hook = s.Hook(
                "ChatView.AppendMessage, FormatMessage, historyDirty",
                "chat timestamps and highlights",
                Patch
            );
            foreach (var name in new[] { "ChatSetTimestamps", "ChatHighlight", "ChatHighlightClear" })
                Host.Api.Find(name)?.Needs(hook, LuaValue.Nil);
        }

        protected internal override void HostStarted()
        {
            decor = Host.Area<ChatDecorArea>();
            if (decor != null)
                decor.Changed += Redraw;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Patch()
        {
            var append = AccessTools.Method(typeof(ChatView), "AppendMessage", new[] { typeof(ChatMessage) });
            var format = AccessTools.Method(typeof(ChatView), "FormatMessage", new[] { typeof(ChatMessage) });
            if (append == null || format == null || AccessTools.Field(typeof(ChatView), "historyDirty") == null)
                return false;
            Patches.All(
                harmony,
                (append, new HarmonyMethod(typeof(ChatDisplayHooks), nameof(Arrived)), null),
                (format, null, new HarmonyMethod(typeof(ChatDisplayHooks), nameof(Formatted)))
            );
            return true;
        }

        static void Arrived(ref ChatMessage incoming)
        {
            try
            {
                if (incoming.message == null)
                    return;
                incoming.message = new string(incoming.message.ToCharArray());
                arrivals.Add(incoming.message, new Arrival { At = DateTime.Now });
            }
            catch (Exception e)
            {
                Once(e);
            }
        }

        static void Formatted(ChatMessage incoming, ref string __result)
        {
            try
            {
                var d = instance?.decor;
                if (d == null || !d.Active || __result == null)
                    return;
                DateTime? at =
                    incoming.message != null && arrivals.TryGetValue(incoming.message, out var a)
                        ? a.At
                        : (DateTime?)null;
                __result = d.Decorate(__result, at) ?? __result;
            }
            catch (Exception e)
            {
                Once(e);
            }
        }

        static void Once(Exception e)
        {
            if (failed)
                return;
            failed = true;
            instance?.Host.Env.Log(
                LogLevel.Error,
                "chat decoration failed (logged once; the line is drawn as the game made it): " + e
            );
        }

        // Lines already shown are formatted again with the new decoration.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Redraw()
        {
            try
            {
                var view = ChatView.Instance;
                if (view != null)
                    Traverse.Create(view).Field("historyDirty").SetValue(true);
            }
            catch (Exception e)
            {
                Once(e);
            }
        }
    }

    // GamePrintLink in the game. ChatView.OnPointerClick hands every link id that is not "item:..." to
    // PlayerContextMenu.PopUp, whose Whisper sends to the server. A prefix runs first, finds the link under the click
    // as the game does (TMP_TextUtilities.FindIntersectingLink on chatHistory) and, for a host id (ChatLinkId), hands
    // it to OutputArea.Click and skips the game's handler. Hover (UpdateItemLinkHover) acts on item links only. Without
    // this hook GamePrintLink returns false and prints nothing, so no host link is ever drawn that the game could take.
    sealed class ChatLinkClicks : Area
    {
        static ChatLinkClicks instance;
        static bool failed;
        readonly Harmony harmony;

        public ChatLinkClicks(Harmony harmony)
        {
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Chat link clicks";

        protected internal override void Register(AreaSetup s)
        {
            var hook = s.Hook("ChatView.OnPointerClick, chatHistory", "clickable chat lines", Patch);
            Host.Api.Find("GamePrintLink")?.Needs(hook, false);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Patch()
        {
            var click = AccessTools.Method(typeof(ChatView), "OnPointerClick", new[] { typeof(PointerEventData) });
            var field = AccessTools.Field(typeof(ChatView), "chatHistory");
            if (click == null || field == null || !typeof(TMP_Text).IsAssignableFrom(field.FieldType))
                return false;
            G.History = AccessTools.FieldRefAccess<ChatView, TMP_Text>(field);
            harmony.Patch(
                click,
                prefix: new HarmonyMethod(typeof(ChatLinkClicks), nameof(Clicked)) { priority = Priority.First }
            );
            return true;
        }

        // False skips the game's handler. A click whose link cannot be read is not passed on either.
        static bool Clicked(ChatView __instance, PointerEventData eventData)
        {
            string id;
            try
            {
                var text = G.History(__instance);
                int i = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, null);
                if (i < 0)
                    return true;
                id = text.textInfo.linkInfo[i].GetLinkID();
            }
            catch (Exception e)
            {
                Once(e);
                return false;
            }
            if (!ChatLinkId.IsHost(id))
                return true;
            try
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                    instance?.Host.Area<OutputArea>()?.Click(id);
            }
            catch (Exception e)
            {
                Once(e);
            }
            return false;
        }

        static void Once(Exception e)
        {
            if (failed)
                return;
            failed = true;
            instance?.Host.Env.Log(LogLevel.Error, "chat link click failed (logged once; the click was dropped): " + e);
        }

        // Game-typed state lives here, so a changed chat class disables links only.
        static class G
        {
            public static AccessTools.FieldRef<ChatView, TMP_Text> History;
        }
    }

    // OnItemTooltip in the game: ItemTooltip.TryBuild marks the content it made with its item, and when the tooltip
    // system shows such a content (the player hovers it) for a component of one of the local player's own lists, the
    // mods' lines are added. Inventory slots build their tooltips when they fill, so mods are asked at hover time only.
    sealed class TooltipHooks : Area
    {
        static TooltipHooks instance;
        static readonly ConditionalWeakTable<string, Item> items = new ConditionalWeakTable<string, Item>();
        readonly Harmony harmony;
        static bool failed;

        sealed class Item
        {
            public int Id,
                Meta;
        }

        public TooltipHooks(Harmony harmony)
        {
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Item tooltip hooks";

        protected internal override void Register(AreaSetup s)
        {
            s.Hook(
                "ItemTooltip.TryBuild, TooltipSystem.ShowTooltip, the slot lists of OfferingUI, TradeUI and the statue menus",
                "item tooltip lines (OnItemTooltip)",
                Patch
            );
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Patch()
        {
            var build = AccessTools.Method(
                typeof(ReworkedCopium.UI.ItemTooltip),
                "TryBuild",
                new[]
                {
                    typeof(ItemDataSO),
                    typeof(int),
                    typeof(string).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                }
            );
            var show = AccessTools.Method(
                typeof(ReworkedCopium.TooltipSystem),
                "ShowTooltip",
                new[] { typeof(string), typeof(string), typeof(Component), typeof(bool) }
            );
            if (build == null || show == null || !Own.Resolve())
                return false;
            // Last: another plugin's postfix that appends to the content would
            // otherwise make a new string after it was marked, and the mods' lines would be lost.
            Patches.All(
                harmony,
                (build, null, new HarmonyMethod(typeof(TooltipHooks), nameof(Built)) { priority = Priority.Last }),
                (show, new HarmonyMethod(typeof(TooltipHooks), nameof(Showing)), null)
            );
            return true;
        }

        static void Built(ItemDataSO data, int meta, ref string content, bool __result)
        {
            try
            {
                if (__result && data != null && content != null)
                    items.AddOrUpdate(content, new Item { Id = data.id, Meta = meta });
            }
            catch (Exception e)
            {
                Once(e);
            }
        }

        static void Showing(ref string content, Component newOwner)
        {
            try
            {
                if (content == null || !items.TryGetValue(content, out var item))
                    return;
                var source = Own.SourceOf(newOwner);
                var area = instance?.Host.Area<TooltipArea>();
                string lines = area?.Lines(source, () => Table(item));
                if (lines != null)
                    content = content + "\n" + lines;
            }
            catch (Exception e)
            {
                Once(e);
            }
        }

        static LuaValue Table(Item item)
        {
            var t = InventoryArea.ItemTable(item.Id, 1, item.Meta);
            t["count"] = LuaValue.Nil;
            return t;
        }

        static void Once(Exception e)
        {
            if (failed)
                return;
            failed = true;
            instance?.Host.Env.Log(LogLevel.Error, "item tooltip lines failed (logged once): " + e);
        }
    }

    // Which own list, if any, a component showing an item tooltip belongs to. Only a positive match counts: the bag
    // slots of the local player's inventories, the slots an offering or statue picker lists from them, the trade
    // screen's own offer and inventory, and the statue menus' selected koi or pet (the player's own stored ones).
    // Another player's inventory, the partner's offer, the offering results and chat links match none of these.
    static class Own
    {
        static AccessTools.FieldRef<PlayerModel, ReworkedCopium.Inventory.PlayerHotbar> hotbar;
        static AccessTools.FieldRef<ReworkedCopium.OfferingUI, List<OfferingSlot>> pickerSlots;
        static AccessTools.FieldRef<ReworkedCopium.OfferingUI, OfferingSlot> pickerSelected;
        static AccessTools.FieldRef<TradeUI, List<OfferingSlot>> tradeMine,
            tradeInventory;
        static AccessTools.FieldRef<ReworkedCopium.OtherstoneStatueMenu, OfferingSlot> koiSelected;
        static AccessTools.FieldRef<ReworkedCopium.PetStatueMenu, OfferingSlot> petSelected;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Resolve()
        {
            hotbar = AccessTools.FieldRefAccess<PlayerModel, ReworkedCopium.Inventory.PlayerHotbar>("PlayerHotbar");
            pickerSlots = AccessTools.FieldRefAccess<ReworkedCopium.OfferingUI, List<OfferingSlot>>(
                "instantiatedSlots"
            );
            pickerSelected = AccessTools.FieldRefAccess<ReworkedCopium.OfferingUI, OfferingSlot>("selectedItem");
            tradeMine = AccessTools.FieldRefAccess<TradeUI, List<OfferingSlot>>("mySlots");
            tradeInventory = AccessTools.FieldRefAccess<TradeUI, List<OfferingSlot>>("inventorySlots");
            koiSelected = AccessTools.FieldRefAccess<ReworkedCopium.OtherstoneStatueMenu, OfferingSlot>(
                "selectedStoneSlot"
            );
            petSelected = AccessTools.FieldRefAccess<ReworkedCopium.PetStatueMenu, OfferingSlot>("selectedPetSlot");
            return AccessTools.Field(typeof(ReworkedCopium.Inventory.Slot), "tooltip") != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TooltipSource SourceOf(Component owner)
        {
            if (owner is ReworkedCopium.TooltipTrigger trigger)
            {
                var slot = trigger.GetComponentInParent<ReworkedCopium.Inventory.Slot>();
                return slot != null && slot.tooltip == trigger && OwnController(slot.controller)
                    ? TooltipSource.OwnBag
                    : TooltipSource.NotOwn;
            }
            if (!(owner is OfferingSlot s))
                return TooltipSource.NotOwn;
            foreach (var ui in All<ReworkedCopium.OfferingUI>())
                if (pickerSelected(ui) == s || (pickerSlots(ui)?.Contains(s) ?? false))
                    return TooltipSource.OwnOfferPicker;
            foreach (var ui in All<TradeUI>())
                if ((tradeMine(ui)?.Contains(s) ?? false) || (tradeInventory(ui)?.Contains(s) ?? false))
                    return TooltipSource.OwnTradeSide;
            foreach (var ui in All<ReworkedCopium.OtherstoneStatueMenu>())
                if (koiSelected(ui) == s)
                    return TooltipSource.OwnStatue;
            foreach (var ui in All<ReworkedCopium.PetStatueMenu>())
                if (petSelected(ui) == s)
                    return TooltipSource.OwnStatue;
            return TooltipSource.NotOwn;
        }

        static T[] All<T>()
            where T : UnityEngine.Object =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        static bool OwnController(ReworkedCopium.Inventory.BaseStorageController c)
        {
            var lp = PlayerModel.LocalPlayer;
            return c != null
                && lp != null
                && (
                    c == hotbar(lp)?.controller
                    || c == lp.farmingInventory?.controller
                    || c == lp.stoneInventory?.controller
                    || c == lp.petInventory?.controller
                );
        }
    }
}
