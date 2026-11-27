using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

// _api.txt, its groups, and the text helpers.
static partial class Program
{
    static void ApiText()
    {
        using var rig = new Rig();
        rig.Start();
        string text = File.ReadAllText(Path.Combine(rig.Env.ModsDir, "_api.txt"));
        var lines = text.Split('\n');
        CheckEach("_api.txt lists every function once, with its description", rig.Host.Api.Functions, 35, f =>
            lines.Count(l => l.StartsWith(f.Name + "(")) != 1 ? f.Name + " not once"
            : !text.Contains(f.Description) ? f.Name + " without its description"
            : null
        );
        Check(
            text.Contains("OnCommand_<name>( args:string )") && text.Contains("OnTick( seconds:number )"),
            "callbacks listed"
        );
        string flat = text.Replace("\n-- ", " ");
        Check(
            flat.Contains("1,500 ms in any 5 seconds")
                && flat.Contains("at most 32 MB")
                && flat.Contains("a call may take 20 ms")
                && flat.Contains("at most 256x256 pixels; 256 different files per mod")
                && flat.Contains("64 timers"),
            "_api.txt states the per-mod limits",
            text.Substring(0, Math.Min(text.Length, 1600))
        );
    }

    static void ApiGroups()
    {
        using var rig = new Rig();
        rig.Start();
        var lines = rig.Host.Api.ApiText(rig.Env.HostVersion).Split('\n');
        int Line(string start) => Array.FindIndex(lines, l => l.StartsWith(start));
        Check(
            Line("-- Lifecycle") < Line("-- Output")
                && Line("-- Settings") < Line("ModSettingGet(")
                && Line("ModSettingSet(") < Line("OnSettingChanged(")
                && Line("OnSettingChanged(") < Line("-- Saved data"),
            "groups in reading order, each callback with its functions"
        );
        Check(
            lines.Where(l => l.Length > 0 && !l.StartsWith("--")).All(l => l.Contains("  -- ") && l.IndexOf('(') > 0),
            "every line has its description"
        );
    }

    static void ApiTextNamesTheVersion()
    {
        using var rig = new Rig();
        rig.Env.HostVersion = "0.4.2";
        rig.Start();
        string text = File.ReadAllText(Path.Combine(rig.Env.ModsDir, "_api.txt"));
        Check(
            text.StartsWith("-- ShorelandsMods 0.4.2: ") && text.Contains("needs_host=\"0.4.2\""),
            "_api.txt names the host's version",
            text.Substring(0, 200)
        );
    }

    static void TextHelpers()
    {
        Check(TextFormat.Number(1234567, 0) == "1,234,567", "thousands separated", TextFormat.Number(1234567, 0));
        Check(TextFormat.Number(-1234.56, 1) == "-1,234.6", "negative with decimals", TextFormat.Number(-1234.56, 1));
        Check(TextFormat.Number(2.5, 0) == "3" && TextFormat.Number(999, 0) == "999", "half away from zero; small");
        Check(TextFormat.Duration(252, true) == "4:12", "clock", TextFormat.Duration(252, true));
        Check(TextFormat.Duration(4000, true) == "1:06:40", "clock with hours", TextFormat.Duration(4000, true));
        Check(TextFormat.Duration(59.9, true) == "0:59", "whole seconds, rounded down");
        Check(
            TextFormat.Duration(45, false) == "45 s"
                && TextFormat.Duration(252, false) == "4 min"
                && TextFormat.Duration(4000, false) == "1 h 06 min",
            "long",
            TextFormat.Duration(4000, false)
        );
        Check(TextFormat.Duration(-5, true) == "-0:05", "negative", TextFormat.Duration(-5, true));
    }
}
