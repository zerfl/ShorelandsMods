using System;
using System.Collections.Generic;
using Lua;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Core
{
    // How much the wallet changed, for OnWalletChanged. The values the host saw when the wallet appeared (login) are
    // the baseline, and updates that arrive before the player is in the world only move it: so the first event after
    // a login never reports the whole balance as a gain, whatever order the server's updates come in.
    internal sealed class WalletChange
    {
        int? currency,
            citb;

        public void Reset() => currency = citb = null;

        public void Baseline(int currency, int citb)
        {
            this.currency = currency;
            this.citb = citb;
        }

        public (int Currency, int Citb) Next(int currency, int citb, bool inWorld)
        {
            var change =
                this.currency.HasValue && inWorld ? (currency - this.currency.Value, citb - this.citb.Value) : (0, 0);
            Baseline(currency, citb);
            return change;
        }
    }

    // OnOfferingRoundStarted from the public countdown: rounds run back to back and the game sends nothing when one
    // starts, but the end time (now + seconds left) jumps when the server sets the next one.
    internal sealed class OfferingRounds
    {
        // NetworkTime is smoothed; two readings of one round agree to well under this.
        public const double Tolerance = 2;
        double? end;

        // left: the countdown now, or null between two rounds. True when a new round started; never for the round
        // running when the player arrives in the world.
        public bool Next(double now, bool inWorld, double? left)
        {
            if (!inWorld)
            {
                end = null;
                return false;
            }
            if (left == null)
                return false;
            double e = now + left.Value;
            bool started = end.HasValue && Math.Abs(e - end.Value) > Tolerance;
            end = e;
            return started;
        }
    }

    // OnItemGained from the player's own items: every item and statue koi counted by (id, meta), compared once nothing
    // changed for SettleSeconds. A move between bags or into or out of the statue arrives as two updates a few frames
    // apart and cancels out; the count when the player arrives in the world is the baseline.
    internal sealed class ItemGains
    {
        public const double SettleSeconds = 0.4;
        Dictionary<(int Id, int Meta), int> counted;
        double changedAt = double.NegativeInfinity;
        bool dirty;

        public void Changed(double now)
        {
            dirty = true;
            changedAt = now;
        }

        // Every frame. count: the player's items now. Null, or what went up and by how much.
        public List<(int Id, int Meta, int Count)> Update(
            double now,
            bool inWorld,
            Func<Dictionary<(int Id, int Meta), int>> count
        )
        {
            if (!inWorld)
            {
                counted = null;
                return null;
            }
            if (now - changedAt < SettleSeconds || counted != null && !dirty)
                return null;
            dirty = false;
            var before = counted;
            counted = count();
            if (before == null)
                return null;
            List<(int, int, int)> gains = null;
            foreach (var kv in counted)
            {
                before.TryGetValue(kv.Key, out int had);
                if (kv.Value > had)
                    (gains = gains ?? new List<(int, int, int)>()).Add((kv.Key.Id, kv.Key.Meta, kv.Value - had));
            }
            gains?.Sort();
            return gains;
        }
    }

    // The three RPCs that carry a player's line in 0.69i: PlayerModel.ReceivePublicChat on the speaker's own model,
    // ChatManager.RpcReceive (global), ChatManager.TargetReceive (whispers). NPC lines come through
    // Merchant/GossipNpc.TargetSay, server notes through the info RPCs; neither is a route.
    internal enum ChatRoute
    {
        Public,
        Global,
        Whisper,
    }

    // Which received lines reach OnChatMessage. A line must have the type its route carries and a sender id. A public
    // line comes from the model it arrived on, so its id must be that model's. A global line or whisper may come from
    // a player out of range (not spawned): it passes when the sender is the local player or a spawned player, or when
    // the id has the form every player id has (decimal digits; NPC and server lines carry none) and the name is a
    // player name (letters, digits, _ . -; NPC and server names are bracketed).
    internal static class ChatSenders
    {
        // What ChatArea does with a line: a player's line plays the chat or whisper sound (not for your own) and goes to
        // OnChatMessage; any other line does nothing.
        public static void Deliver(
            ModHost host,
            ChatRoute route,
            string type,
            string playerId,
            string sender,
            string text,
            string speakerId,
            string localId,
            Func<string, bool> spawned
        )
        {
            string kind = Kind(route, type, playerId, sender, speakerId, localId, spawned);
            if (kind == null)
                return;
            if (playerId != localId)
                host.Area<SoundArea>()?.Event(kind == "private" ? "whisper" : "chat");
            host.Post(() => host.Fire("OnChatMessage", () =>
                {
                    var t = new LuaTable(0, 3);
                    t["type"] = kind;
                    t["sender"] = sender ?? "";
                    t["text"] = text ?? "";
                    return t;
                }));
        }

        public static string Kind(
            ChatRoute route,
            string type,
            string playerId,
            string name,
            string speakerId,
            string localId,
            Func<string, bool> spawned
        )
        {
            string kind =
                route == ChatRoute.Public && type == "PUBLIC" ? "public"
                : route == ChatRoute.Global && type == "GLOBAL" ? "global"
                : route == ChatRoute.Whisper && type == "PRIVATE" ? "private"
                : null;
            if (kind == null || string.IsNullOrEmpty(playerId))
                return null;
            if (route == ChatRoute.Public)
                return playerId == speakerId ? kind : null;
            if (playerId == localId || spawned(playerId))
                return kind;
            return PlayerIdForm(playerId) && PlayerNameForm(name) ? kind : null;
        }

        static bool PlayerIdForm(string id)
        {
            if (id.Length > 19)
                return false;
            foreach (char c in id)
                if (c < '0' || c > '9')
                    return false;
            return true;
        }

        static bool PlayerNameForm(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 32)
                return false;
            foreach (char c in name)
                if (
                    !(
                        (c >= 'a' && c <= 'z')
                        || (c >= 'A' && c <= 'Z')
                        || (c >= '0' && c <= '9')
                        || c == '_'
                        || c == '.'
                        || c == '-'
                    )
                )
                    return false;
            return true;
        }
    }
}
