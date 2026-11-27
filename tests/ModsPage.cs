using System;
using System.Collections.Generic;
using System.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;

// The mods page.
static partial class Program
{
    static void ModsPage()
    {
        using var rig = new Rig();
        rig.Mod("a", "x = 1");
        rig.Mod("b", "error('boom')");
        rig.Mod("c", "x = 1");
        rig.Env.Off.Add("c");
        rig.Start();
        Check(
            ModsPageModel.Summary(rig.Host.Mods) == "3 mods: 1 on, 1 off, 1 failed",
            "summary",
            ModsPageModel.Summary(rig.Host.Mods)
        );
        Check(
            ModsPageModel.StateWord(rig["c"]) == "Off" && ModsPageModel.Problem(rig["c"]) == null,
            "a switched-off mod has no problem line"
        );
        string p = ModsPageModel.Problem(rig["b"]);
        Check(
            p.StartsWith("init.lua line 1: boom") && p.EndsWith("until its files change or you reload."),
            "a failed mod says why, as a sentence",
            p
        );
        rig.Host.SwitchOff("c", false);
        Check(
            rig["c"].Status == ModStatus.Loaded && !rig.Env.Off.Contains("c"),
            "switching on starts the mod and saves it"
        );
        rig.Host.Move("c", -5);
        Check(
            string.Join(",", rig.Host.Mods.Select(m => m.Id)) == "c,a,b" && rig.Host.OrderOf("c") == 0,
            "move to the top, clamped"
        );
        Check(
            rig["a"].Status == ModStatus.Loaded && rig["c"].Status == ModStatus.Loaded,
            "moved mods are running again"
        );
        Check(
            ModsPageModel.AssetSummary(
                new List<AssetLine>
                {
                    new AssetLine("a.png", AssetState.Applied),
                    new AssetLine("b.png", AssetState.Ignored, "wrong size"),
                }
            ) == "2 files: 1 applied, 1 not used",
            "asset summary"
        );
    }

    static void ModsPageSeen()
    {
        using var rig = new Rig();
        rig.Mod("a", "x = 1");
        rig.Mod("b", "n = 0 function OnTick() n = n + 1 if n == 3 then error('late') end end");
        rig.Mod("c", "x = 1");
        rig.Start();
        var seen = new ModsSeen();
        seen.Take(rig.Host.Mods);
        rig.Run(0.1);
        Check(!seen.Changed(rig.Host.Mods), "running mods leave the page as drawn");
        rig.Run(0.5);
        Check(rig["b"].Status == ModStatus.Failed && seen.Changed(rig.Host.Mods), "a mod failing while open");
        seen.Take(rig.Host.Mods);
        rig.Env.Clock += 1;
        rig.Host.Reload("a");
        Check(seen.Changed(rig.Host.Mods), "a reload (same state, new Mod)");
        seen.Take(rig.Host.Mods);
        rig.Host.SwitchOff("c", true);
        Check(seen.Changed(rig.Host.Mods), "switched off");
        seen.Take(rig.Host.Mods);
        rig.Host.Move("c", -1);
        Check(seen.Changed(rig.Host.Mods), "moved");
    }

    // The mods page fits the game's canvas at every size: no larger than 620x420 nor the canvas, its parts inside it
    // and apart, the list's rows inside the list pane, the button row centred.
    static void ModsPageLayoutFits()
    {
        var sizes = new[] { (W: 640, H: 360), (W: 854, H: 480), (W: 1280, H: 720), (W: 1920, H: 1080) };
        CheckEach("the mods page fits", sizes, 4, s =>
        {
            var L = ModsPageLayout.Compute(s.W, s.H, 10, 60, 90, 50);
            if (L.W > Math.Min(620, s.W) || L.H > Math.Min(420, s.H))
                return "page " + L.W + "x" + L.H;
            var parts = new (string Name, URect R)[]
            {
                ("summary", L.Summary),
                ("folder", L.Folder),
                ("list", L.List),
                ("list track", L.ListTrack),
                ("detail", L.Detail),
                ("detail track", L.DetailTrack),
                ("inspect", L.Inspect),
                ("reload", L.Reload),
                ("back", L.Back),
            };
            foreach (var p in parts)
                if (!p.R.Inside(L.W, L.H) || p.R.W <= 0 || p.R.H <= 0)
                    return p.Name + " is outside";
            foreach (var a in parts)
            foreach (var b in parts)
                if (string.CompareOrdinal(a.Name, b.Name) < 0 && a.R.Overlaps(b.R))
                    return a.Name + " overlaps " + b.Name;
            if (L.ListRows < 1 || L.ListRows * ModsPageLayout.RowH > L.List.H - 2)
                return L.ListRows + " rows in a list of " + L.List.H;
            if (Math.Abs(L.Inspect.X - (L.W - L.Back.Right)) > 1)
                return "the buttons are not centred";
            return null;
        });
        Check(
            ModsPageLayout.Compute(1920, 1080, 10, 60, 90, 50).ListRows
                > ModsPageLayout.Compute(640, 360, 10, 60, 90, 50).ListRows,
            "a larger canvas shows more rows"
        );
    }
}
