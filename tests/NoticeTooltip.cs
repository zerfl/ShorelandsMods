using System;
using System.IO;
using System.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;

// Notices and item tooltips.
static partial class Program
{
    static void NoticeQueueing()
    {
        var q = new NoticeQueue();
        for (int i = 0; i < 5; i++)
            q.Add("m", "n" + i, 2, 0);
        q.Update(0);
        Check(q.Shown.Count() == 3 && q.Waiting == 2, "at most three notices at once, the rest wait");
        q.Add("m", "n0", 2, 1);
        Check(q.Waiting == 2, "the same notice again restarts it instead of queueing");
        q.Update(3.0);
        Check(q.Shown.Count() == 3 && q.Shown.Any(n => n.Text == "n0"), "n0 restarted at 1 s still shows at 3 s");
        q.Update(2 + NoticeQueue.FadeOut + 0.01);
        q.Update(3.5);
        Check(q.Waiting == 0 && q.Shown.Any(n => n.Text == "n4"), "waiting notices take the freed places");
        var n0 = new Notice { ShownAt = 10, Seconds = 2 };
        Check(
            NoticeQueue.Alpha(n0, 10) == 0
                && NoticeQueue.Alpha(n0, 11) == 1
                && NoticeQueue.Alpha(n0, 12 + NoticeQueue.FadeOut) < 1e-9,
            "notices fade in and out"
        );
    }

    static void NoticeApi()
    {
        using var rig = new Rig();
        rig.Mod(
            "n",
            "for i = 1, 8 do NoticeShow('notice ' .. i, 1) end NoticeShow('a long notice that has to wrap because it is much wider than the notice may be', 1)"
        );
        rig.Start();
        rig.Run(0.1);
        Check(
            rig.Ui.Notices.Count == 3 && rig.Ui.Notices[0].notice.Text == "notice 1",
            "three notices show, oldest first"
        );
        Check(
            rig.Notices.Queue.Waiting == 2,
            "five a second per mod; the rest of the burst is dropped",
            rig.Notices.Queue.Waiting.ToString()
        );
        rig.Run(4);
        Check(rig.Ui.Notices.Count == 0 && rig.Notices.Queue.Waiting == 0, "all shown and gone after their time");
        rig.Mod("w", "NoticeShow('a long notice that has to wrap because it is much wider than the notice may be', 1)");
        rig.Host.Reload("w");
        rig.Run(0.1);
        var lines = rig.Ui.Notices.Single().lines;
        Check(
            lines.Count > 1 && lines.All(l => 5 * l.Length <= NoticeArea.MaxWidth),
            "a long notice wraps inside its width",
            string.Join("|", lines)
        );
    }

    static void TooltipApi()
    {
        using var rig = new Rig();
        rig.Mod("a", "function OnItemTooltip(item) return 'A sees ' .. item.name .. ' <b>' end");
        rig.Mod("b", "function OnItemTooltip(item) return { 'not a string' } end");
        rig.Mod("c", "function OnItemTooltip(item) if item.id == 5 then error('tooltip broke') end return 'C' end");
        rig.Mod("d", "function OnItemTooltip(item) return nil end");
        rig.Start();
        Lua.LuaValue Item(int id)
        {
            var t = new Lua.LuaTable();
            t["id"] = id;
            t["name"] = "Rod";
            return t;
        }
        string lines = rig.Tooltips.Lines(TooltipSource.OwnBag, () => Item(1));
        string Line(string id, string text) =>
            "<color="
            + ModHost.Color.Mod
            + ">["
            + id
            + "]</color> <color="
            + TooltipArea.LineColor
            + ">"
            + text
            + "</color>";
        Check(
            lines == Line("a", "A sees Rod ˂b˃") + "\n" + Line("c", "C"),
            "one line per mod, in load order, escaped",
            lines
        );
        Check(
            rig.Env.ChatText.Contains("OnItemTooltip returned a table"),
            "a non-text result is said once and skipped"
        );
        string after = rig.Tooltips.Lines(TooltipSource.OwnBag, () => Item(5));
        Check(
            after == Line("a", "A sees Rod ˂b˃") && rig["c"].Status == ModStatus.Failed,
            "an error switches that mod off, the others still add their line",
            after
        );
        Check(
            rig.Env.ChatText.Split('\n').Count(l => l.Contains("OnItemTooltip returned")) == 1,
            "the non-text notice is not repeated"
        );
    }

    // OnItemTooltip runs only for an item the player holds; a mod's line carries its id in the mods' violet.
    static void TooltipOwnItemsOnly()
    {
        using var rig = new Rig();
        rig.Mod("spy", "calls = 0 function OnItemTooltip(item) calls = calls + 1 return 'seen' end");
        rig.Start();
        Lua.LuaValue Item()
        {
            var t = new Lua.LuaTable();
            t["id"] = 9999;
            t["meta"] = 160;
            return t;
        }
        int Calls() => (int)rig["spy"].Lua.Global("calls").Read<double>();
        Check(
            rig.Tooltips.Lines(TooltipSource.NotOwn, Item) == null && Calls() == 0,
            "a tooltip not of the player's own (another player, a trade partner, results, chat, shop) asks no mod"
        );
        CheckEach(
            "every other source (the player's own lists) asks the mod",
            Enum.GetValues<TooltipSource>().Where(s => s != TooltipSource.NotOwn),
            1,
            own =>
            {
                int before = Calls();
                return rig.Tooltips.Lines(own, Item) != null && Calls() == before + 1 ? null : "no line";
            }
        );
        string marked = rig.Tooltips.Lines(TooltipSource.OwnBag, Item);
        Check(
            marked.StartsWith("<color=" + ModHost.Color.Mod + ">[spy]</color> ")
                && marked.Contains("<color=" + TooltipArea.LineColor + ">seen</color>"),
            "a mod's tooltip line starts with its id in the mods' violet",
            marked
        );
    }
}
