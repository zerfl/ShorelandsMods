using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

// Chat commands: registering, near misses, /mods off.
static partial class Program
{
    static void Commands()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("a_first", "Out('a', CommandRegister('stats', 'x')) function OnCommand_stats(a) Out('a ran', a) end");
        rig.Mod(
            "b_second",
            "Out('b', CommandRegister('stats', 'x'), CommandRegister('ignore', 'x'), CommandRegister('channels', 'x'), CommandRegister('mods', 'x'), CommandRegister('quiet', 'x'))"
        );
        rig.Mod("c_bad", "Out('c', CommandRegister('Bad Name', 'x'))");
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        Check(
            o.Contains("a\ttrue") && o.Contains("b\tfalse\tfalse\tfalse\tfalse\ttrue"),
            "first mod keeps a name; game, server and host names refused",
            string.Join("|", o)
        );
        Check(rig.Env.ChatText.Contains("a_first has it"), "refusal names the earlier mod", rig.Env.ChatText);
        rig.Commands.Type("stats some args");
        Check(o.Contains("a ran\tsome args"), "typing the command calls OnCommand_<name>");
        rig.Commands.Type("quiet");
        Check(rig.Env.ChatText.Contains("no function OnCommand_quiet"), "missing handler is reported");
        Check(
            rig.Commands.Added.ContainsKey("mods") && rig.Commands.Added.ContainsKey("reloadmods"),
            "host commands added"
        );
        rig.Commands.ServerTree("channels", "stats");
        Check(
            !rig.Commands.Added.ContainsKey("stats") && rig.Env.ChatText.Contains("was removed"),
            "a server command that arrives later wins"
        );
        rig.Commands.Type("mods");
        Check(
            rig.Env.ChatLines.Any(l => l.Contains("a_first 1.0: loaded"))
                && rig.Env.ChatLines.Any(l => l.Contains("c_bad 1.0: loaded")),
            "/mods lists every mod with state",
            rig.Env.ChatText
        );
        Check(
            rig.Env.ChatText.Contains("ShorelandsMods 9.8.7. Mods ("),
            "/mods names the host's version",
            rig.Env.ChatText
        );
        rig.Commands.Type("reloadmods");
        rig.Run(0.1);
        Check(rig.Env.ChatText.Contains("Mods reloaded"), "/reloadmods reloads");
    }

    static void UnknownCommandHints()
    {
        using var rig = new Rig(new ToolsArea(new FakeTools()));
        rig.Mod("town", "CommandRegister('visits', 'x') function OnCommand_visits() end");
        rig.Start();
        int lines = rig.Env.ChatLines.Count;
        string Typed(string line)
        {
            rig.Commands.Type(line);
            string said = string.Join("|", rig.Env.ChatLines.Skip(lines));
            lines = rig.Env.ChatLines.Count;
            return said;
        }
        Check(Typed("modz").Contains("Did you mean /mods?"), "a typo of a host command names it");
        Check(Typed("visit").Contains("Did you mean /visits?"), "a typo of a mod's command names it");
        Check(Typed("Mods").Contains("Did you mean /mods?"), "a command typed in capitals names it");
        Check(Typed("/mods").Contains("Type /mods with one slash."), "two slashes are pointed out");
        Check(Typed("tr").Contains("/try"), "a short word one letter off names the command");
        Check(Typed("xyz") == "", "a short word two letters off is not taken for a command");
        Check(Typed("dance") == "", "a word far from every command adds nothing to the game's answer");
        Check(Typed("channels") == "", "a server command is never hinted at");
    }

    // /mods off <id> switches a mod off from chat, which removes its windows, HUD and notices; /mods on again.
    static void ModsOffCommand()
    {
        using var rig = new Rig();
        rig.Mod(
            "loud",
            "WindowCreate('w', 'Loud', 4000, 4000) WindowOpen('w') HudText('h', 'x') "
                + "function OnTick() NoticeShow('again') end"
        );
        rig.Start();
        rig.Run(0.5);
        rig.Commands.Type("mods off loud");
        rig.Run(0.1);
        Check(
            rig["loud"].Status == ModStatus.Off
                && rig.Windows.Windows.Count == 0
                && rig.Hud.Widgets.Count == 0
                && rig.Ui.Notices.Count == 0
                && rig.Env.Off.Contains("loud"),
            "/mods off stops the mod and its windows, HUD and notices, and is saved",
            rig["loud"].Status + " " + rig.Ui.Notices.Count
        );
        rig.Commands.Type("mods on loud");
        rig.Run(0.1);
        Check(rig["loud"].Status == ModStatus.Loaded && !rig.Env.Off.Contains("loud"), "/mods on starts it again");
    }
}
