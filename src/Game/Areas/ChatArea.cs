using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ReworkedCopium.Chat;
using ReworkedCopium.Messages;
using ReworkedCopium.Player;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Game.Areas
{
    // Player chat lines only (public, global, private): the sender's name and the text, after the game's ignore list.
    // Taken from the RPCs that carry players' lines (ChatRoute); ChatSenders decides which pass.
    sealed class ChatArea : Area
    {
        static ChatArea instance;
        readonly GameEnv env;
        readonly Harmony harmony;

        public ChatArea(GameEnv env, Harmony harmony)
        {
            this.env = env;
            this.harmony = harmony;
            instance = this;
        }

        public override string Name => "Chat";

        protected internal override void Register(AreaSetup s)
        {
            env.UseChat(
                s.Hook(
                    "ChatManager.PrintInfoMessage, ChatView.Instance",
                    "chat output (GamePrint, host messages)",
                    TouchOutput
                )
            );
            s.Hook(
                "PlayerModel.UserCode_ReceivePublicChat, ChatManager.UserCode_RpcReceive/TargetReceive",
                "chat events",
                Patch
            );
            s.Callback(
                "OnChatMessage( msg:table )",
                "a player's chat line arrived: { type = \"public\"|\"global\"|\"private\", sender, text }, after the "
                    + "game's ignore list. Your own lines arrive too (sender is then your name, as PlayerGetName gives "
                    + "it). Server, info and NPC lines (a merchant's \"Thanks for the purchase!\") never arrive; "
                    + "ChatHighlight and ChatSetTimestamps still decorate them"
            );
        }

        // A line as one of the routes delivered it; speakerId: the entity id of the model a public line arrived on.
        public void Message(
            ChatRoute route,
            string type,
            string playerId,
            string sender,
            string text,
            string speakerId
        ) => ChatSenders.Deliver(Host, route, type, playerId, sender, text, speakerId, LocalId(), Spawned);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string LocalId() => PlayerModel.LocalPlayer != null ? PlayerModel.LocalPlayer.EntityId : null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Spawned(string playerId)
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<PlayerModel>(UnityEngine.FindObjectsSortMode.None))
                if (p.EntityId == playerId)
                    return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TouchOutput()
        {
            _ = ChatView.Instance;
            return typeof(ChatManager).GetMethod("PrintInfoMessage") != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Patch()
        {
            var pub = AccessTools.Method(typeof(PlayerModel), "UserCode_ReceivePublicChat__ChatMessage");
            var global = AccessTools.Method(typeof(ChatManager), "UserCode_RpcReceive__ChatMessage");
            var whisper = AccessTools.Method(
                typeof(ChatManager),
                "UserCode_TargetReceive__NetworkConnectionToClient__ChatMessage"
            );
            if (pub == null || global == null || whisper == null)
                return false;
            Patches.All(
                harmony,
                (pub, null, Postfix(nameof(PublicPostfix))),
                (global, null, Postfix(nameof(GlobalPostfix))),
                (whisper, null, Postfix(nameof(WhisperPostfix)))
            );
            return true;
        }

        static HarmonyMethod Postfix(string name) =>
            new HarmonyMethod(typeof(ChatArea).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        static void PublicPostfix(PlayerModel __instance, ChatMessage incoming) =>
            Received(ChatRoute.Public, incoming, __instance.EntityId);

        static void GlobalPostfix(ChatMessage incoming) => Received(ChatRoute.Global, incoming, null);

        static void WhisperPostfix(ChatMessage incoming) => Received(ChatRoute.Whisper, incoming, null);

        static void Received(ChatRoute route, ChatMessage m, string speakerId)
        {
            try
            {
                if (!ChatIgnoreList.IsIgnored(m))
                    instance.Message(route, m.type.ToString(), m.playerId, m.name, m.message, speakerId);
            }
            catch (Exception) { }
        }
    }
}
