namespace ShorelandsMods.Core
{
    // "Lua is running", read by the send guard. Set around every call into Lua (callbacks, timers, commands, init),
    // and cleared by the host at the start of every frame in case an unwinding skipped the reset.
    internal static class LuaGuard
    {
        public static int Depth { get; private set; }
        public static ModRuntime Current { get; private set; }
        public static int Refusals { get; private set; }

        internal static ModRuntime Enter(ModRuntime rt)
        {
            var previous = Current;
            Depth++;
            Current = rt;
            return previous;
        }

        internal static void Exit(ModRuntime previous)
        {
            Depth--;
            Current = previous;
        }

        // Called by the send guard for a send it dropped; the host switches the mod off when the call returns.
        public static void Refused(string what)
        {
            Refusals++;
            if (Current != null && Current.RefusedSend == null)
                Current.RefusedSend = what;
        }

        // Returns whether the flag had been left set.
        public static bool ResetForFrame()
        {
            if (Depth == 0)
                return false;
            Depth = 0;
            Current = null;
            return true;
        }
    }
}
