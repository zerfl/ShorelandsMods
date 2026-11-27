namespace ShorelandsMods.Core
{
    // The send guard's decisions (Game/SendGuard.cs patches Mirror's send methods and asks these): while Lua runs,
    // every Command and every message to the server is refused, named for the log and the mod's stop.
    internal static class SendRules
    {
        // What a Command is refused as, or null to let it through.
        public static string Command(string functionFullName) =>
            LuaGuard.Depth > 0 ? "Command " + ShortName(functionFullName) : null;

        // A message's id (its first two bytes, little-endian; 0 if shorter) when it is refused, or -1.
        public static int Message(bool toServer, byte[] bytes, int offset, int count)
        {
            if (LuaGuard.Depth <= 0 || !toServer)
                return -1;
            return count >= 2 ? bytes[offset] | bytes[offset + 1] << 8 : 0;
        }

        // "System.Void ReworkedCopium.Fishing.FishingManager::CmdRequestFishingSpots(...)" -> "FishingManager.CmdRequestFishingSpots"
        public static string ShortName(string full)
        {
            int paren = full.IndexOf('(');
            string s = paren > 0 ? full.Substring(0, paren) : full;
            int space = s.LastIndexOf(' ');
            s = s.Substring(space + 1).Replace("::", ".");
            int dot = s.LastIndexOf('.', s.LastIndexOf('.') - 1);
            return dot >= 0 ? s.Substring(dot + 1) : s;
        }
    }
}
