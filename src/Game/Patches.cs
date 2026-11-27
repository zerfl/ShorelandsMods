using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShorelandsMods.Game
{
    // A hook's patches applied as one: if one throws, those already applied are taken out again, so a hook reported
    // missing leaves none of its patches live.
    static class Patches
    {
        public static void All(
            Harmony harmony,
            params (MethodBase target, HarmonyMethod prefix, HarmonyMethod postfix)[] set
        )
        {
            var applied = new List<(MethodBase, MethodInfo)>();
            try
            {
                foreach (var (target, prefix, postfix) in set)
                {
                    harmony.Patch(target, prefix: prefix, postfix: postfix);
                    if (prefix != null)
                        applied.Add((target, prefix.method));
                    if (postfix != null)
                        applied.Add((target, postfix.method));
                }
            }
            catch
            {
                for (int i = applied.Count - 1; i >= 0; i--)
                    try
                    {
                        harmony.Unpatch(applied[i].Item1, applied[i].Item2);
                    }
                    catch (Exception) { }
                throw;
            }
        }
    }
}
