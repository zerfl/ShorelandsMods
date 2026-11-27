using System.Collections.Generic;

namespace ShorelandsMods.Core.Areas
{
    // The areas that need nothing from the game except the command sink. The game plugin adds its own after these.
    internal static class CoreAreas
    {
        public static List<Area> Create(ICommandSink commands) =>
            new List<Area>
            {
                new LifecycleArea(),
                new OutputArea(),
                new TimeArea(),
                new SettingsArea(),
                new DataArea(),
                new SessionArea(),
                new ExportArea(),
                new CommandsArea(commands),
                new ColorArea(),
                new TextArea(),
            };
    }
}
