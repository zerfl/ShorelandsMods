using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ShorelandsMods.Core;

// The console runner and helpers every subject uses.
//   (no arguments)       every scenario
//   --only <text>        scenarios whose name or subject contains the text (case-insensitive)
//   --skip-realtime      leaves out the scenarios tagged Realtime
//   --verbose            a line per scenario, and the measurements some print
//   --assembly <dll>, --package <zip>: ReleaseChecks.cs
static partial class Program
{
    // A scenario on wall-clock time (a real-time smoke test, a measurement against generous bounds).
    const bool Realtime = true;

    readonly record struct Scenario(string Subject, Action Test, bool Realtime = false)
    {
        public string Name => Test.Method.Name;
    }

    // Each scenario once.
    static readonly Scenario[] Scenarios =
    {
        new("ManifestPaths", Manifest),
        new("ManifestPaths", Order),
        new("ManifestPaths", Activity),
        new("ManifestPaths", Paths),
        new("ManifestPaths", Escape),
        new("ManifestPaths", VersionParsing),
        new("ManifestPaths", NeedsHostCompared),
        new("ManifestPaths", NeedsHostAtLoad),
        new("ManifestPaths", NeedsHostFailsClosed),
        new("ManifestPaths", ShippedVersions),
        new("Sandbox", Sandbox),
        new("Sandbox", DoFile),
        new("Sandbox", Guard),
        new("Sandbox", GuardNesting),
        new("Sandbox", GuardReset),
        new("Sandbox", SendGuardRules),
        new("Lifecycle", Lifecycle),
        new("Lifecycle", Reload),
        new("Lifecycle", Failures),
        new("Lifecycle", HostFolders),
        new("Lifecycle", BlockedLuaRunsNoSettings),
        new("Lifecycle", ReloadSaysManifestFailure),
        new("ErrorText", Errors),
        new("ErrorText", SyntaxHints),
        new("ErrorText", Arguments),
        new("ErrorText", SoftValues),
        new("ErrorText", UnknownOptionsNamed),
        new("ErrorText", ColourSpelling),
        new("SettingsKeys", Settings),
        new("SettingsKeys", Controls),
        new("SettingsKeys", TextSettings),
        new("SettingsKeys", SettingsWriteFailure),
        new("SettingsKeys", KeyApi),
        new("SettingsKeys", KeyClashes),
        new("SettingsKeys", ShippedKeys),
        new("SettingsKeys", KeySettingsOnlyByPlayer),
        new("DataExport", Data),
        new("DataExport", SessionStore),
        new("DataExport", Export),
        new("DataExport", WriteRates),
        new("DataExport", ExportWriteFailure),
        new("DataExport", DataWriteFailure),
        new("DataExport", DataKeysAreNotPaths),
        new("Commands", Commands),
        new("Commands", UnknownCommandHints),
        new("Commands", ModsOffCommand),
        new("ApiText", ApiText),
        new("ApiText", ApiGroups),
        new("ApiText", ApiTextNamesTheVersion),
        new("ApiText", TextHelpers),
        new("Assets", Headers),
        new("Assets", Scan),
        new("Assets", Conflicts),
        new("Assets", Xml),
        new("Assets", CueSize),
        new("Assets", Names),
        new("Assets", CueNames),
        new("Assets", ArtPacks),
        new("Assets", SoundPacks),
        new("Assets", LuaSound),
        new("Assets", CuePacks),
        new("Assets", VoiceGroupDropped),
        new("Assets", ModReportLines),
        new("Assets", IconTextureNotes),
        new("Assets", SoundLoopPaths),
        new("FileSafety", PngChecks),
        new("FileSafety", IconFiles),
        new("FileSafety", ArtBudget),
        new("FileSafety", SoundLengths),
        new("FileSafety", LinksInModFolders),
        new("FileSafety", LinksInHostFolders),
        new("FileSafety", ScanThread),
        new("FileSafety", ReservedModIds),
        new("FileSafety", SizeCaps),
        new("Hud", HudPlacement),
        new("Hud", TextFitting),
        new("Hud", HudApi),
        new("Hud", HudStacking),
        new("Window", Scrolling),
        new("Window", WindowParts),
        new("Window", WindowApi),
        new("Window", HostSearch),
        new("Window", WindowButtons),
        new("Window", WindowClosing),
        new("Window", SearchToldToMod),
        new("Window", SearchSetByMod),
        new("Window", WindowKeepsMenuReachable),
        new("Window", WindowStaysClosed),
        new("Window", EscapeHatch),
        new("Chat", ChatDecoration),
        new("Chat", ChatDecorApi),
        new("Chat", ModLinesMarked),
        new("Chat", PrintCapped),
        new("Chat", ChatDelivery),
        new("Chat", ChatWiring),
        new("Chat", LinkClickCallsOwner),
        new("Chat", LinkIdsOnlyTheHosts),
        new("Chat", LinkTextStaysText),
        new("Chat", LinkCaps),
        new("Chat", LinksDieWithTheMod),
        new("Chat", LinkClickIsGuarded),
        new("Chat", LinkWithoutClickHook),
        new("NoticeTooltip", NoticeQueueing),
        new("NoticeTooltip", NoticeApi),
        new("NoticeTooltip", TooltipApi),
        new("NoticeTooltip", TooltipOwnItemsOnly),
        new("ModsPage", ModsPage),
        new("ModsPage", ModsPageSeen),
        new("ModsPage", ModsPageLayoutFits),
        new("Observed", WalletChanges),
        new("Observed", OfferingRoundStarts),
        new("Observed", ItemGainCounting),
        new("AuthorTools", OriginalFiles),
        new("AuthorTools", AuthorCommands),
        new("AuthorTools", Previews),
        new("AuthorTools", PreviewFeedback),
        new("AuthorTools", InspectEntries),
        new("AuthorTools", InspectOnlyVisible),
        new("AuthorTools", TryCallbacks),
        new("Budget", Runaway, Realtime),
        new("Budget", CallBudget),
        new("Budget", Cost),
        new("Budget", HostTime),
        new("Budget", BudgetNotice),
        new("Budget", WindowBudget),
        new("Budget", TimerFlood),
        new("Budget", TimersAtCallLimit),
        new("Budget", RetainedStrings),
        new("Budget", RetainedElsewhere),
        new("Budget", CollectionBringsMeasureForward),
        new("Budget", TimerShares),
        new("Budget", HeavyMoments),
        new("Budget", HostWorkCharged),
        new("Budget", PerModCaps),
        new("Budget", ShippedExamplesLoad, Realtime),
        new("HostileInput", HostileText),
        new("HostileInput", HostileArguments),
        new("HostileInput", DataWalkBounded),
        new("HostileInput", SettingsBounds),
        new("HostileInput", XmlBounds),
    };

    static int failed,
        passed;
    static bool verbose;

    static void Check(bool cond, string name, string detail = null)
    {
        if (cond)
            passed++;
        else
        {
            failed++;
            Console.WriteLine("FAIL " + name + (detail == null ? "" : "  [" + detail + "]"));
        }
    }

    // One assertion over many items: there are at least atLeast of them (so it cannot pass on none) and no item has a
    // problem (null: none); the detail names the first that does.
    static void CheckEach<T>(string name, IEnumerable<T> items, int atLeast, Func<T, string> problem)
    {
        var list = items.ToList();
        string first = list.Select(i => (Item: i, Problem: problem(i)))
            .Where(x => x.Problem != null)
            .Select(x => Shorten(x.Item?.ToString()) + ": " + x.Problem)
            .FirstOrDefault();
        Check(
            list.Count >= atLeast && first == null,
            name,
            first ?? (list.Count < atLeast ? list.Count + " items, expected at least " + atLeast : null)
        );
    }

    static string Shorten(string s) => s == null || s.Length <= 60 ? s : s.Substring(0, 60) + "...";

    // A measurement worth reading when asked (--verbose).
    static void Note(string text)
    {
        if (verbose)
            Console.WriteLine("  " + text);
    }

    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--assembly")
            return Release("host assembly", () => HostAssembly(args[1]));
        if (args.Length == 2 && args[0] == "--package")
            return Release("package", () => Package(args[1]));
        string only = null;
        bool skipRealtime = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--only" && i + 1 < args.Length)
                only = args[++i];
            else if (args[i] == "--skip-realtime")
                skipRealtime = true;
            else if (args[i] == "--verbose")
                verbose = true;
            else
            {
                Console.WriteLine("unknown argument: " + args[i]);
                return 2;
            }
        }
        var chosen = Scenarios
            .Where(s =>
                only == null
                || s.Name.Contains(only, StringComparison.OrdinalIgnoreCase)
                || s.Subject.Contains(only, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        int realtimeSkipped = skipRealtime ? chosen.RemoveAll(s => s.Realtime) : 0;
        if (chosen.Count == 0)
        {
            Console.WriteLine("no scenario matches");
            return 2;
        }
        var sw = Stopwatch.StartNew();
        Watchdog.Start();
        int scenariosFailed = 0;
        foreach (var s in chosen)
        {
            // The memory budget reads heap growth: another scenario's garbage must not count.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int before = failed;
            var t = Stopwatch.StartNew();
            try
            {
                s.Test();
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL " + s.Name + " threw " + e);
            }
            bool ok = failed == before;
            if (!ok)
                scenariosFailed++;
            if (!ok || verbose)
                Console.WriteLine(
                    (ok ? "ok   " : "FAILED ") + s.Subject + "/" + s.Name + " (" + t.ElapsedMilliseconds + " ms)"
                );
        }
        DeleteRunTemp();
        Console.WriteLine(
            (scenariosFailed == 0 ? "ALL PASS" : scenariosFailed + " FAILED")
                + " ("
                + (chosen.Count - scenariosFailed)
                + " of "
                + chosen.Count
                + " scenarios passed"
                + (realtimeSkipped > 0 ? ", " + realtimeSkipped + " realtime skipped" : "")
                + "; "
                + passed
                + " assertions, "
                + failed
                + " failed; "
                + sw.ElapsedMilliseconds
                + " ms)"
        );
        return scenariosFailed == 0 ? 0 : 1;
    }

    static string Problem(Action a)
    {
        try
        {
            a();
            return "";
        }
        catch (ModProblem p)
        {
            return p.Message;
        }
    }

    // Folders for one run, all under one root deleted at the end.
    static readonly string RunTemp = Path.Combine(
        Path.GetTempPath(),
        "smods-run-" + Guid.NewGuid().ToString("N").Substring(0, 8)
    );
    static int tempN;

    internal static string TempDir(string name)
    {
        string dir = Path.Combine(RunTemp, name + "-" + ++tempN);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void DeleteRunTemp()
    {
        try
        {
            if (Directory.Exists(RunTemp))
                Directory.Delete(RunTemp, true);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Console.WriteLine("could not delete " + RunTemp + ": " + e.Message);
        }
    }

    static string TempFile(string name, byte[] bytes)
    {
        string f = Path.Combine(TempDir("file"), name);
        File.WriteAllBytes(f, bytes);
        return f;
    }

    static int Count(string s, string part)
    {
        int n = 0;
        for (int i = s.IndexOf(part); i >= 0; i = s.IndexOf(part, i + 1))
            n++;
        return n;
    }
}
