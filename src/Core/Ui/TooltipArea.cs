using System;
using System.Collections.Generic;
using Lua;

namespace ShorelandsMods.Core.Ui
{
    // Whose item a shown tooltip belongs to, as the game side identified it from the component showing it. NotOwn
    // unless one of the local player's own lists was recognised: another player's inventory, a trade partner's offer,
    // offering results, chat links, shops and anything unknown.
    internal enum TooltipSource
    {
        NotOwn,
        OwnBag,
        OwnOfferPicker,
        OwnTradeSide,
        OwnStatue,
    }

    // OnItemTooltip: each loaded mod may add one line to the tooltip of an item the player holds, in load order. The
    // game side calls Lines when a tooltip of an item is about to show (the player hovers it), never when the game
    // merely prepares one.
    internal sealed class TooltipArea : Area
    {
        public const int MaxChars = 120;
        public const string LineColor = "#A8D8FF";
        readonly HashSet<string> warned = new HashSet<string>();

        public override string Name => "Item tooltips";

        protected internal override void Register(AreaSetup s)
        {
            s.Callback(
                "OnItemTooltip( item:table ) -> string|nil",
                "the player hovers an item of their own (bags, the offering and statue pickers, their side of a trade, "
                    + "the koi or pet in their statue) and its tooltip shows: { id, name, meta, category } (as "
                    + "OnItemGained); return one line to add below it (at most "
                    + MaxChars
                    + " characters, a number shows as text) or nil. The line is shown after the mod's [id]; lines of "
                    + "several mods follow in load order. Not called for another player's items, a trade partner's "
                    + "offer, offering results, chat links or shop goods, nor when the game only prepares a tooltip; "
                    + "another result type is skipped with one notice"
            );
        }

        protected internal override void ModStopping(Mod mod) => warned.Remove(mod.Id);

        // The lines to append (rich text, escaped and coloured by the host), or null when no mod adds one. Mods are
        // asked only for an item the local player holds.
        public string Lines(TooltipSource source, Func<LuaValue> item)
        {
            if (source == TooltipSource.NotOwn)
                return null;
            List<string> lines = null;
            foreach (var mod in Host.Mods.ToArrayCopy())
            {
                if (mod.Status != ModStatus.Loaded || mod.Lua == null || !mod.Lua.HasFunction("OnItemTooltip"))
                    continue;
                var r = Host.Ask(mod, "OnItemTooltip", item());
                if (r.Type == LuaValueType.Nil)
                    continue;
                if (!r.TryRead<string>(out var text))
                {
                    if (r.Type == LuaValueType.Number)
                        text = r.ToString();
                    else
                    {
                        if (warned.Add(mod.Id))
                            Host.Notice(
                                mod,
                                "OnItemTooltip returned "
                                    + LuaText.Describe(r)
                                    + "; it must return a text or nil, so it is not shown."
                            );
                        continue;
                    }
                }
                // Cut before escaping: the escape only adds characters, and Clip cuts again.
                if (text.Length > 2 * MaxChars)
                    text = text.Substring(0, 2 * MaxChars);
                text = TextEscape.Tmp(text).Trim();
                if (text.Length == 0)
                    continue;
                (lines = lines ?? new List<string>()).Add(
                    "<color="
                        + ModHost.Color.Mod
                        + ">["
                        + TextEscape.Tmp(mod.Id)
                        + "]</color> <color="
                        + LineColor
                        + ">"
                        + TextEscape.Clip(text, MaxChars)
                        + "</color>"
                );
            }
            return lines == null ? null : string.Join("\n", lines);
        }
    }

    static class ListCopy
    {
        public static T[] ToArrayCopy<T>(this IReadOnlyList<T> list)
        {
            var a = new T[list.Count];
            for (int i = 0; i < a.Length; i++)
                a[i] = list[i];
            return a;
        }
    }
}
