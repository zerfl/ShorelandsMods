namespace ShorelandsMods.Core
{
    internal static class Activities
    {
        // The game's PLAYERSTATE name -> the coarse activity a mod may see. Every fishing step is "fishing", so no
        // event or read can reveal a bite. Unknown states give null:
        // the caller keeps the previous value rather than guess, since a new fishing step must not look like "idle".
        public static string Fold(string state)
        {
            switch (state)
            {
                case "IDLE":
                case "IDLE_BLOCKING":
                    return "idle";
                case "WALKING":
                    return "walking";
                case "ACTION_HOEING":
                    return "farming";
                case "ACTION_DIALOGUE":
                    return "talking";
                case "FISHING":
                    return "fishing";
                default:
                    return state != null && state.StartsWith("ACTION_FISHING") ? "fishing" : null;
            }
        }
    }
}
