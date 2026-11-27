namespace ShorelandsMods.Core.Areas
{
    // The callbacks the host itself drives (ModHost.Update and StopMod).
    internal sealed class LifecycleArea : Area
    {
        public override string Name => "Lifecycle";

        protected internal override void Register(AreaSetup s)
        {
            s.Callback(
                "OnTick( seconds:number )",
                "ten times a second, also at the main menu; seconds since the previous tick (about 0.1)"
            );
            s.Callback(
                "OnFrame( seconds:number )",
                "every frame, also at the main menu; seconds since the previous frame. Only called if defined; its "
                    + "time counts against the mod's budget, so keep it small"
            );
            s.Callback(
                "OnModUnload()",
                "before this mod is reloaded, switched off or the game closes (not when it failed); saved data is "
                    + "written after it"
            );
        }
    }
}
