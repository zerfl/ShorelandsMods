using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Xml;
using ShorelandsMods.Core;

// mod.xml, load order, activity names, paths a mod may name, the game's text escape, versions.
static partial class Program
{
    // ---- pure logic ----

    static void Manifest()
    {
        var m = ModManifest.Parse(
            "<Mod name=\"Session stats\" description=\"d\" version=\"1.2.0\" author=\"a\" made_for_game=\"0.69i\" needs_host=\"0.2.1\" />"
        );
        Check(
            m.Name == "Session stats" && m.Version == "1.2.0" && m.NeedsHost?.ToString() == "0.2.1",
            "manifest fields"
        );
        Check(ModManifest.Parse("<Mod name=\"x\"/>").NeedsHost == null, "no needs_host, no requirement");
        Check(Problem(() => ModManifest.Parse("<Mod description=\"x\"/>")).Contains("name"), "manifest without name");
        Check(Problem(() => ModManifest.Parse("<Mod name=\"x\">")).Contains("not valid XML"), "broken xml");
        Check(Problem(() => ModManifest.Parse("<Other name=\"x\"/>")).Contains("<Mod"), "wrong root");
        string xxe =
            "<?xml version=\"1.0\"?><!DOCTYPE m [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><Mod name=\"&e;\"/>";
        Check(Problem(() => ModManifest.Parse(xxe)).Contains("not valid XML"), "DTD refused (no external entities)");

        Check(ModId.Problem("session_stats2") == null, "valid id");
        foreach (var bad in new[] { "Session", "2mods", "_x", "a-b", "a b", "", new string('a', 41) })
            Check(ModId.Problem(bad) != null, "invalid id " + bad);
    }

    static void Order()
    {
        var o = LoadOrder.Arrange(new[] { "b", "a", "d", "c" }, new[] { "c", "gone", "a" });
        Check(string.Join(",", o) == "c,a,b,d", "saved order first, then alphabetical", string.Join(",", o));
        Check(string.Join(",", LoadOrder.Split(" a, b ,,c")) == "a,b,c", "order list parsing");
    }

    static void Activity()
    {
        foreach (
            var st in new[]
            {
                "FISHING",
                "ACTION_FISHING_CAST",
                "ACTION_FISHING_WAIT",
                "ACTION_FISHING_BITE",
                "ACTION_FISHING_CATCH",
            }
        )
            Check(Activities.Fold(st) == "fishing", "every fishing step is \"fishing\": " + st);
        Check(Activities.Fold("IDLE") == "idle" && Activities.Fold("IDLE_BLOCKING") == "idle", "idle states");
        Check(
            Activities.Fold("WALKING") == "walking"
                && Activities.Fold("ACTION_HOEING") == "farming"
                && Activities.Fold("ACTION_DIALOGUE") == "talking",
            "other activities"
        );
        Check(Activities.Fold("ACTION_REEL_NEW") == null, "an unknown state is not guessed (no fishing->idle leak)");
    }

    static void Paths()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smods-paths", "mymod");
        string ok(string p) => ModPaths.ResolveLua(dir, "mymod", p, out _, out _);
        Check(ok("files/util.lua") == Path.GetFullPath(Path.Combine(dir, "files", "util.lua")), "relative path");
        Check(ok("mods/mymod/files/util.lua") == ok("files/util.lua"), "Noita mods/<id>/ prefix");
        Check(ok("files\\util.LUA") != null, "backslashes and upper-case extension");
        foreach (
            var bad in new[]
            {
                "../other/init.lua",
                "files/../../x.lua",
                "/etc/x.lua",
                "C:/Windows/x.lua",
                "C:x.lua",
                "\\\\server\\share\\x.lua",
                "files/x.txt",
                "mods/other/init.lua/../../x.lua",
                "",
                "files/./x.lua",
                "init.lua\0.txt",
            }
        )
            Check(ok(bad) == null, "dofile path refused: " + bad.Replace("\0", "\\0"));
        Check(
            ok("mods/other/init.lua") == Path.GetFullPath(Path.Combine(dir, "mods", "other", "init.lua")),
            "other mod's path stays inside"
        );

        Check(ModPaths.ExportFileName("currency", out _) == "currency.txt", "export name gets .txt");
        Check(ModPaths.ExportFileName("last item.txt", out _) == "last item.txt", "export name keeps one .txt");
        foreach (var bad in new[] { "../x", "a/b", "c:x", "", ".hidden", "con", "nul.txt", "x?", new string('a', 65) })
            Check(ModPaths.ExportFileName(bad, out _) == null, "export name refused: " + bad);
    }

    static void Escape()
    {
        string managed = (string)AppContext.GetData("ShorelandsMods.Tests.Managed");
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
        var game = Assembly
            .LoadFrom(Path.Combine(managed, "Game.dll"))
            .GetType("ReworkedCopium.StringExtensions")
            .GetMethod("EscapeTMPText");
        string[] attacks =
        {
            "plain text",
            "<color=red>red</color>",
            "</noparse><b>x",
            "a\\nb",
            "a\\u0041b",
            "\\\u200b",
            "&lt;b&gt;",
            "<sprite=0>",
            "say \"hi\" it's",
            "tab\there\r\nnew",
            ":@3: emote",
            "",
        };
        foreach (var a in attacks)
            Check(
                TextEscape.Tmp(a)
                    == ((string)game.Invoke(null, new object[] { a }))
                        ?.Replace('＆', '&')
                        .Replace('＂', '"')
                        .Replace('＇', '\''),
                "escape equals the game's, quotes and & kept, for " + a
            );
        Check(!TextEscape.Chat("x :@3: y").Contains(":@"), "chat escape breaks :@N:");
        Check(TextEscape.Chat("<b>").IndexOf('<') < 0, "chat escape removes <");
    }

    static SemVer? V(string s) => SemVer.TryParse(s, out var v) ? v : (SemVer?)null;

    static void VersionParsing()
    {
        Check(
            V("0.2.0")?.ToString() == "0.2.0" && V("1.2")?.ToString() == "1.2.0" && V("3")?.ToString() == "3.0.0",
            "short forms read as x.y.0"
        );
        foreach (
            var bad in new[]
            {
                "",
                "two",
                "1.2.3.4",
                "1..2",
                ".1",
                "1.",
                "v1.2",
                "1.2.0-beta",
                "-1",
                "1.x",
                "1 .2",
                "1234567890",
            }
        )
            Check(V(bad) == null, "not a version: \"" + bad + "\"");
        Check(V("0.10.0")?.CompareTo(V("0.9.9").Value) > 0, "numbers compare as numbers, not text");
        Check(
            V("1.0.0")?.CompareTo(V("0.99.99").Value) > 0 && V("0.2.1")?.CompareTo(V("0.2.0").Value) > 0,
            "major, then minor, then patch"
        );
        Check(
            Problem(() => ModManifest.Parse("<Mod name=\"x\" needs_host=\"two\"/>"))
                == "mod.xml: needs_host must be a host version like 0.2.0 (numbers with dots between them), not \"two\"",
            "garbage in needs_host is a sentence",
            Problem(() => ModManifest.Parse("<Mod name=\"x\" needs_host=\"two\"/>"))
        );
        Check(
            ModManifest.Parse("<Mod name=\"x\" needs_host=\" 0.2 \"/>").NeedsHost?.ToString() == "0.2.0",
            "spaces around it are fine"
        );
    }

    static void NeedsHostCompared()
    {
        string U(string needs, string have) => SemVer.Unmet(V(needs).Value, V(have).Value);
        Check(
            U("0.3.0", "0.2.1") == "it needs host 0.3.0, you have 0.2.1; update ShorelandsMods to use it",
            "a newer minor",
            U("0.3.0", "0.2.1")
        );
        Check(U("1.0.0", "0.9.0").Contains("needs host 1.0.0, you have 0.9.0"), "a higher major");
        Check(U("0.2.2", "0.2.1") != null, "a newer patch");
        Check(
            U("0.2.1", "0.2.1") == null && U("0.2", "0.2.1") == null && U("0.1.9", "0.2.0") == null,
            "this host or an older one: runs"
        );
    }

    static void NeedsHostAtLoad()
    {
        using var rig = new Rig();
        rig.Env.HostVersion = "0.2.1";
        rig.Mod("same", "x = 1", "<Mod name=\"s\" needs_host=\"0.2.1\"/>");
        rig.Mod("short", "x = 1", "<Mod name=\"s\" needs_host=\"0.2\"/>");
        rig.Mod("none", "x = 1", "<Mod name=\"s\"/>");
        rig.Mod("newer", "x = 1", "<Mod name=\"s\" needs_host=\"0.3.0\"/>");
        rig.Mod("major", "x = 1", "<Mod name=\"s\" needs_host=\"1\"/>");
        rig.Mod("garbage", "x = 1", "<Mod name=\"s\" needs_host=\"latest\"/>");
        rig.Start();
        string S(string id) => rig[id].Status + ": " + rig[id].Reason;
        Check(
            new[] { "same", "short", "none" }.All(id => rig[id].Status == ModStatus.Loaded),
            "met or not asked: loads"
        );
        Check(S("newer").StartsWith("Skipped: it needs host 0.3.0, you have 0.2.1"), "newer skipped", S("newer"));
        Check(
            S("major").StartsWith("Skipped: it needs host 1.0.0, you have 0.2.1"),
            "higher major skipped",
            S("major")
        );
        Check(
            rig["garbage"].Status == ModStatus.Failed
                && rig["garbage"].Reason.Contains("needs_host must be a host version"),
            "garbage fails",
            S("garbage")
        );
    }

    static void NeedsHostFailsClosed()
    {
        foreach (var blank in new[] { "", "  " })
            Check(
                Problem(() => ModManifest.Parse("<Mod name=\"x\" needs_host=\"" + blank + "\"/>"))
                    ?.StartsWith("mod.xml: needs_host must be a host version") == true,
                "a blank needs_host=\"" + blank + "\" stops the mod"
            );
        using var rig = new Rig();
        rig.Env.HostVersion = "dev";
        rig.Mod("asks", "x = 1", "<Mod name=\"a\" needs_host=\"0.1.0\"/>");
        rig.Mod("free", "x = 1", "<Mod name=\"f\"/>");
        rig.Start();
        Check(
            rig["asks"].Status == ModStatus.Skipped && rig["asks"].Reason.Contains("cannot be compared"),
            "a host version that does not parse skips a mod with needs_host",
            rig["asks"].Status + ": " + rig["asks"].Reason
        );
        Check(rig["free"].Status == ModStatus.Loaded, "a mod without needs_host still loads");
        string dir = Files.RepoRoot();
        var proj = new XmlDocument();
        if (dir != null)
            proj.Load(Path.Combine(dir, "src", "ShorelandsMods.csproj"));
        string prefix = dir == null ? null : proj.SelectSingleNode("//VersionPrefix")?.InnerText;
        Check(SemVer.TryParse(prefix, out _), "the csproj's version parses as the host reads it", prefix);
    }

    // The csproj's VersionPrefix is the host's version; the examples need at most that; the examples and the guide's
    // samples carry a three-part version.
    static void ShippedVersions()
    {
        string dir = Files.RepoRoot();
        if (dir == null)
        {
            Check(false, "the repository is found from the test's folder");
            return;
        }
        var proj = new XmlDocument();
        proj.Load(Path.Combine(dir, "src", "ShorelandsMods.csproj"));
        string prefix = proj.SelectSingleNode("//VersionPrefix")?.InnerText;
        Check(
            prefix != null && Regex.IsMatch(prefix, @"^\d+\.\d+\.\d+$"),
            "the csproj has a MAJOR.MINOR.PATCH version",
            prefix
        );
        if (V(prefix) is not SemVer host)
            return;
        string[] Xmls(string folder) =>
            Directory
                .GetDirectories(Path.Combine(dir, folder))
                .Select(d => Path.Combine(d, "mod.xml"))
                .Where(File.Exists)
                .ToArray();
        var examples = Xmls("examples");
        Check(examples.Length >= 9, "the examples' mod.xml files are found", examples.Length.ToString());
        foreach (var x in examples)
        {
            var m = ModManifest.Read(x);
            string name = Path.GetFileName(Path.GetDirectoryName(x));
            Check(
                Regex.IsMatch(m.Version, @"^\d+\.\d+\.\d+$") && m.NeedsHost != null,
                name + ": a MAJOR.MINOR.PATCH version and needs_host",
                m.Version
            );
            Check(
                m.NeedsHost == null || m.NeedsHost.Value.CompareTo(host) <= 0,
                name + ": runs on this host",
                m.NeedsHost?.ToString()
            );
        }
        string guide = Path.Combine(dir, "guide", "guide.md");
        foreach (Match v in Regex.Matches(File.ReadAllText(guide), "\\bversion=\"([^\"]*)\""))
            Check(
                Regex.IsMatch(v.Groups[1].Value, @"^\d+\.\d+\.\d+$"),
                "guide.md: version=\"" + v.Groups[1].Value + "\""
            );
    }
}
