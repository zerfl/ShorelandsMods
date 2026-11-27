using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Mirror;
using ShorelandsMods.Core;

namespace ShorelandsMods.Game
{
    // While Lua runs (LuaGuard.Depth > 0), every Command and every client message is dropped, logged with a stack trace
    // and reported to the host, which switches the mod off.
    static class SendGuard
    {
        static ManualLogSource log;
        static Dictionary<ushort, string> messageNames;

        public static bool Installed { get; private set; }

        public static bool Install(Harmony harmony, ManualLogSource logger)
        {
            log = logger;
            try
            {
                var cmd = AccessTools.Method(typeof(NetworkBehaviour), "SendCommandInternal");
                var send = AccessTools.Method(
                    typeof(NetworkConnection),
                    "Send",
                    new[] { typeof(ArraySegment<byte>), typeof(int) }
                );
                if (cmd == null || send == null)
                {
                    log.LogError("send guard: Mirror's send methods were not found; Lua mods stay off");
                    return false;
                }
                // Built now, not while a mod's time budget runs.
                MessageName(0);
                harmony.Patch(
                    cmd,
                    prefix: new HarmonyMethod(typeof(SendGuard), nameof(CommandPrefix)) { priority = Priority.First }
                );
                harmony.Patch(
                    send,
                    prefix: new HarmonyMethod(typeof(SendGuard), nameof(MessagePrefix)) { priority = Priority.First }
                );
                Installed = true;
                return true;
            }
            catch (Exception e)
            {
                log.LogError("send guard could not be installed; Lua mods stay off: " + e);
                return false;
            }
        }

        static bool CommandPrefix(string functionFullName)
        {
            string refused = SendRules.Command(functionFullName);
            if (refused == null)
                return true;
            Refuse(refused);
            return false;
        }

        static bool MessagePrefix(NetworkConnection __instance, ArraySegment<byte> segment)
        {
            int id = SendRules.Message(
                __instance is NetworkConnectionToServer,
                segment.Array,
                segment.Offset,
                segment.Count
            );
            if (id < 0)
                return true;
            Refuse("message " + MessageName((ushort)id));
            return false;
        }

        static void Refuse(string what)
        {
            try
            {
                string mod = LuaGuard.Current?.ModId ?? "?";
                log.LogWarning(
                    "REFUSED " + what + " while Lua of mod " + mod + " was running\n" + Environment.StackTrace
                );
                LuaGuard.Refused(what);
            }
            catch (Exception) { }
        }

        static string MessageName(ushort id)
        {
            if (messageNames == null)
            {
                messageNames = new Dictionary<ushort, string>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types.Where(t => t != null).ToArray();
                    }
                    foreach (var t in types)
                    {
                        if (
                            !t.IsValueType
                            || !typeof(NetworkMessage).IsAssignableFrom(t)
                            || t.ContainsGenericParameters
                        )
                            continue;
                        try
                        {
                            var f = typeof(NetworkMessageId<>).MakeGenericType(t).GetField("Id");
                            messageNames[(ushort)f.GetValue(null)] = t.Name;
                        }
                        catch (Exception) { }
                    }
                }
            }
            return messageNames.TryGetValue(id, out var n) ? n : "id " + id;
        }
    }
}
