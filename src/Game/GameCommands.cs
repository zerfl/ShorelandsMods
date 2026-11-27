using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ReworkedCopium.Commands;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Game
{
    // Local slash commands in the game's client dispatcher (CommandHandler.ClientDispatcher), which the chat input
    // tries before sending a line (ChatView.SendMessage: CommandHandler.TryHandleLocally with the text after the
    // slash). The dispatcher has no way to remove a command, so Remove takes it out of the root node's private
    // children/literals dictionaries. A postfix on TryHandleLocally reports a first word no dispatcher knows.
    sealed class GameCommands : ICommandSink
    {
        static GameCommands instance;
        readonly Harmony harmony;
        readonly Action<string> logError;
        readonly HashSet<string> ours = new HashSet<string>();
        FieldInfo children,
            literals;
        object lastServerTree;
        bool? available;

        public event Action ServerCommandsChanged;
        public event Action<string> Unknown;

        public GameCommands(Harmony harmony, Action<string> logError)
        {
            this.harmony = harmony;
            this.logError = logError;
            instance = this;
        }

        public bool Available => available ?? (available = Resolve()).Value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Resolve()
        {
            children = AccessTools.Field(typeof(CommandNode), "children");
            literals = AccessTools.Field(typeof(CommandNode), "literals");
            if (CommandHandler.ClientDispatcher == null || children == null || literals == null)
                return false;
            lastServerTree = ServerTree();
            harmony.Patch(
                AccessTools.Method(typeof(CommandHandler), nameof(CommandHandler.TryHandleLocally)),
                postfix: new HarmonyMethod(typeof(GameCommands), nameof(AfterTry))
            );
            return true;
        }

        static void AfterTry(string input)
        {
            try
            {
                string word = (input ?? "").Split(' ')[0];
                if (
                    word.Length > 0
                    && CommandHandler.ClientDispatcher.Root.GetChild(word) == null
                    && CommandHandler.ServerCommands?.Root.GetChild(word) == null
                )
                    instance.Unknown?.Invoke(word);
            }
            catch (Exception e)
            {
                instance.logError("unknown command hint failed: " + e);
            }
        }

        public string Owner(string name)
        {
            if (!ours.Contains(name) && CommandHandler.ClientDispatcher.Root.GetChild(name) != null)
                return "game";
            if (CommandHandler.ServerCommands?.Root.GetChild(name) != null)
                return "server";
            return null;
        }

        public void Add(string name, Action<string> run)
        {
            CommandHandler.ClientDispatcher.Register(
                CommandBuilders
                    .Literal(name)
                    .Executes(ctx =>
                    {
                        run("");
                        return 1;
                    })
                    .Then(
                        CommandBuilders
                            .Argument("args", Arguments.GreedyString())
                            .Executes(ctx =>
                            {
                                run(ctx.GetArgument<string>("args"));
                                return 1;
                            })
                    )
            );
            ours.Add(name);
        }

        public void Remove(string name)
        {
            var root = CommandHandler.ClientDispatcher.Root;
            ((IDictionary)children.GetValue(root)).Remove(name);
            ((IDictionary)literals.GetValue(root)).Remove(name);
            ours.Remove(name);
        }

        // Every frame: the server sends its command tree after login. Resolve touched ServerTree, so a removed member
        // leaves this unavailable instead of failing here.
        public void Poll()
        {
            if (available != true)
                return;
            var tree = ServerTree();
            if (ReferenceEquals(tree, lastServerTree))
                return;
            lastServerTree = tree;
            ServerCommandsChanged?.Invoke();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static object ServerTree() => CommandHandler.ServerCommands;
    }
}
