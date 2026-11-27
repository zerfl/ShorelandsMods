using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;

// Author tools: exported originals, author commands, previews, inspect mode, /try.
static partial class Program
{
    // Exported originals land where a mod's art/ folder expects them: every planned file, copied into art/textures/,
    // is read back by the asset scan as the texture it came from.
    static void OriginalFiles()
    {
        var names = new NamesCatalog();
        var loaded = new List<(string, int, int)>
        {
            ("hotbar_latest", 224, 32),
            ("creature_4", 128, 128),
            ("creature_4", 64, 64),
            ("Pointer", 12, 12),
            ("pointer", 12, 12),
            ("a.b c", 8, 8),
            ("bad:name", 8, 8),
            ("trailing.", 8, 8),
            ("nul", 8, 8),
            ("AUX.backup", 8, 8),
        };
        foreach (var (n, w, h) in loaded)
            names.Seen(n, w, h);
        var skipped = new List<(string Name, string Why)>();
        var plan = Originals.Plan(loaded, names, skipped);
        Check(
            plan.Any(p => p.File == "textures/creature_4.png" && p.W == 64)
                && plan.Any(p => p.File == "textures/128x128/creature_4.png" && p.W == 128),
            "a name with two sizes: the smaller in textures/, the other in a folder of its size",
            string.Join("|", plan.Select(p => p.File))
        );
        Check(
            plan.Count(p => p.Name.Equals("pointer", StringComparison.OrdinalIgnoreCase)) == 1
                && skipped.Any(s => s.Name == "pointer" && s.Why.Contains("only in case")),
            "names that differ only in case give one file",
            string.Join("|", skipped)
        );
        Check(
            new[] { "bad:name", "trailing.", "nul", "AUX.backup" }.All(n => skipped.Any(s => s.Name == n))
                && plan.All(p => !new[] { "bad:name", "trailing.", "nul", "AUX.backup" }.Contains(p.Name)),
            "names no Windows file can carry are skipped with a reason",
            string.Join("|", skipped)
        );
        string ReadBack(string rel, int w, int h)
        {
            string mod = TempDir("original");
            string file = Path.Combine(mod, "art", rel);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, Files.Png(w, h));
            var scan = new AssetScan();
            scan.Load(mod);
            return string.Join(
                "|",
                scan.Files.Select(f => f.Kind + ":" + f.Key).Concat(scan.Ignored.Select(i => i.ToString()))
            );
        }
        CheckEach("every exported texture in art/textures/ is read back as its name", plan, plan.Count, p =>
        {
            string got = ReadBack(Path.Combine("textures", Path.GetFileName(p.File)), p.W, p.H);
            return got == "Texture:" + p.Name ? null : got;
        });
        Check(
            ReadBack(Originals.Item(101), 32, 32) == "Item:101" && ReadBack(Originals.Koi(160), 32, 32) == "Koi:160",
            "exported icons are read back by id",
            ReadBack(Originals.Item(101), 32, 32) + " " + ReadBack(Originals.Koi(160), 32, 32)
        );
    }

    static void AuthorCommands()
    {
        var tools = new FakeTools();
        var audio = new FakeAudio();
        using var rig = new Rig(
            new SoundArea(audio),
            new ArtArea(new FakeArt()),
            new CueArea(new FakeCues()),
            new ToolsArea(tools)
        );
        File.WriteAllText(
            Path.Combine(rig.Env.ModsDir, "_names.txt"),
            "-- Game version .\n[textures]\nhotbar_latest\t224x32\n"
        );
        rig.Mod("grabby", "CommandRegister('export', 'x') CommandRegister('inspect', 'x')");
        rig.Start();
        Check(
            new[] { "inspect", "export", "preview" }.All(rig.Commands.Added.ContainsKey),
            "the tool commands are the host's"
        );
        Check(
            rig.Env.ChatText.Contains("/export is not available")
                && rig.Env.ChatText.Contains("/inspect is not available"),
            "a mod cannot take a tool's command name",
            rig.Env.ChatText
        );
        rig.Commands.Type("export hotbar_latest");
        rig.Commands.Type("export item 101");
        rig.Commands.Type("export koi 160");
        rig.Commands.Type("export all");
        Check(
            string.Join("|", tools.Asked) == "texture hotbar_latest|item 101|koi 160|all",
            "/export by texture name, item id, koi id, all",
            string.Join("|", tools.Asked)
        );
        rig.Commands.Type("export hotbar_lates");
        rig.Commands.Type("export item ten");
        Check(
            tools.Asked.Count == 4
                && rig.Env.ChatText.Contains("did you mean hotbar_latest?")
                && rig.Env.ChatText.Contains("/export item needs the item"),
            "an unknown name or a bad id says why and exports nothing",
            rig.Env.ChatText
        );
        rig.Commands.Type("inspect");
        Check(tools.Inspecting && rig.Env.ChatText.Contains("Inspect mode is on"), "/inspect turns it on");
        rig.Commands.Type("inspect");
        Check(!tools.Inspecting, "/inspect again turns it off");
        tools.InWorld = false;
        rig.Commands.Type("inspect");
        Check(
            !tools.Inspecting && rig.Env.ChatText.Contains("once you are in the world"),
            "inspect mode needs the world"
        );
    }

    // A preview plays the mod's own file for a name even where another mod's file wins, once (a loop too), in the
    // preview voice; a cue plays its sound and shows its image.
    static void Previews()
    {
        var audio = new FakeAudio();
        var cues = new FakeCues();
        using var rig = new Rig(
            new SoundArea(audio),
            new ArtArea(new FakeArt()),
            new CueArea(cues),
            new ToolsArea(new FakeTools())
        );
        rig.Mod("first", null);
        rig.WriteBytes("first", "sounds/chat.wav", Files.Wav(0.2));
        rig.WriteBytes("first", "sounds/music_town.wav", Files.Wav(0.2));
        rig.Write("first", "sounds/sounds.xml", "<Sounds><Sound name=\"chat\" volume=\"0.4\"/></Sounds>");
        rig.WriteBytes("first", "cues/fishing_bite.wav", Files.Wav(0.2));
        rig.WriteBytes("first", "cues/fishing_bite.png", Files.Png(8, 8));
        rig.Mod("second", null);
        rig.WriteBytes("second", "sounds/chat.wav", Files.Wav(0.2));
        rig.Start();
        var tools = rig.Host.Area<ToolsArea>();
        string loading = tools.Preview(rig["first"], "chat", true);
        audio.ReadyAll();
        rig.Run(0.1);
        Check(loading != null && loading.Contains("still loading"), "a clip still loading says so", loading);
        audio.Plays.Clear();
        Check(tools.Preview(rig["first"], "sounds/chat.wav", true) == null, "a file name works as well as its name");
        Check(
            audio.Plays.SequenceEqual(new[] { "chat.wav@0.4 preview" }),
            "the overridden mod's own file plays, at its sounds.xml volume, in the preview voice",
            string.Join("|", audio.Plays)
        );
        audio.Plays.Clear();
        tools.Preview(rig["first"], "music_town", true);
        Check(
            audio.Plays.SequenceEqual(new[] { "music_town.wav@1 preview" }) && audio.Loops.Count == 0,
            "a loop's file plays once, not as a loop",
            string.Join("|", audio.Plays) + " loops " + audio.Playing
        );
        rig.Host.Area<SoundArea>().StopPreview();
        Check(!rig.Host.Area<SoundArea>().PreviewPlaying, "a preview can be stopped");
        audio.Plays.Clear();
        tools.Preview(rig["first"], "fishing_bite", true);
        Check(
            audio.Plays.SequenceEqual(new[] { "fishing_bite.wav@1 preview" })
                && cues.Previews.Count == 1
                && cues.Previews[0].ModId == "first",
            "a cue plays its sound and shows its image",
            string.Join("|", audio.Plays)
        );
        string unknown = tools.Preview(rig["second"], "whisper", true);
        Check(
            unknown != null && unknown.Contains("no sound or cue called \"whisper\"") && unknown.Contains("chat"),
            "an unknown name lists the mod's names",
            unknown
        );
        Check(
            ToolsArea.PreviewName("cues/fishing_bite.png") == "fishing_bite" && ToolsArea.PreviewName("chat") == "chat",
            "preview names from files"
        );
    }

    // A preview repeats a short cue so it is seen, says how long a sound is and says when the game's volume is 0.
    static void PreviewFeedback()
    {
        Check(CueRules.PreviewCycles(1.0) == 3, "a 1 s bite shows 3 times", CueRules.PreviewCycles(1.0).ToString());
        Check(
            CueRules.PreviewCycles(0.05) >= 2 && CueRules.PreviewTotal(0.05, CueRules.PreviewCycles(0.05)) >= 3,
            "a very short cue repeats for seconds"
        );
        Check(CueRules.PreviewCycles(8) == 1, "a cue of several seconds shows once");
        int k = CueRules.PreviewAt(1.2, 1.0, 3, out bool drawn, out _);
        Check(k == 0 && !drawn, "between two showings the image is not drawn");
        k = CueRules.PreviewAt(1.5, 1.0, 3, out drawn, out double at);
        Check(k == 1 && drawn && System.Math.Abs(at - 0.1) < 1e-9, "the second showing starts its frames again");
        Check(CueRules.PreviewAt(4.3, 1.0, 3, out _, out _) == -1, "after the last showing the preview is over");

        var audio = new FakeAudio();
        var cues = new FakeCues();
        var tools = new FakeTools();
        using var rig = new Rig(
            new SoundArea(audio),
            new ArtArea(new FakeArt()),
            new CueArea(cues),
            new ToolsArea(tools)
        );
        rig.Mod("pack", null);
        rig.WriteBytes("pack", "sounds/whisper.wav", Files.Wav(0.8));
        rig.WriteBytes("pack", "cues/fishing_bite.wav", Files.Wav(0.25));
        rig.WriteBytes("pack", "cues/fishing_bite.png", Files.Png(12, 16));
        rig.Start();
        audio.ReadyAll();
        rig.Run(0.1);
        int lines = rig.Env.ChatLines.Count;
        string Typed(string line)
        {
            rig.Commands.Type(line);
            string said = string.Join("|", rig.Env.ChatLines.Skip(lines)).Replace('\uFF07', '\'');
            lines = rig.Env.ChatLines.Count;
            return said;
        }
        string said = Typed("preview pack whisper");
        Check(
            said.Contains("Playing whisper of pack (0.8 s)."),
            "a sound preview says its length, from the file",
            said
        );
        Check(!said.Contains("volume"), "with the volume up nothing is said about it");
        said = Typed("preview pack fishing_bite");
        Check(
            cues.Cycles.LastOrDefault() == 3
                && said.Contains("pack's fishing_bite cue, 3 times 1 s each at your character, with its sound")
                && rig.Notices.Queue.Waiting + rig.Notices.Queue.Shown.Count() == 1,
            "a cue preview at the character repeats the bite 3 times and says so in chat and on screen",
            said
        );
        audio.MasterVolume = 0;
        said = Typed("preview pack whisper");
        Check(said.Contains("volume is at 0"), "at volume 0 a preview says why nothing is heard", said);
        tools.InWorld = false;
        said = Typed("preview pack fishing_bite");
        Check(
            said.Contains("Playing the sound of pack's fishing_bite cue (0.")
                && said.Contains("once you are in the world"),
            "outside the world a cue plays its sound and says where its image shows",
            said
        );
    }

    // Inspect mode lists one entry per nameable thing: a row of the same tile once, an icon as its id, the cursor
    // last even when the list is cut; the mark follows the pointer to a new topmost thing. Every hit here is half
    // see-through, so all of them show.
    static void InspectEntries()
    {
        InspectHit Tex(string name, HitKind kind = HitKind.World) =>
            new InspectHit
            {
                Kind = kind,
                Texture = name,
                W = 16,
                H = 16,
                Alpha = 0.5f,
            };
        var hits = new List<InspectHit>
        {
            Tex("hotbar_latest", HitKind.Screen),
            new InspectHit
            {
                Kind = HitKind.Screen,
                ItemId = 101,
                Name = "Fishing Rod",
                IconW = 24,
                IconH = 24,
                Alpha = 0.5f,
            },
            new InspectHit
            {
                Kind = HitKind.Screen,
                KoiId = 69,
                Name = "Hat",
                KoiCovered = true,
                Alpha = 0.5f,
            },
            Tex("path", HitKind.Ground),
            Tex("path", HitKind.Ground),
            Tex("tree_1"),
            Tex("tree_2"),
            new InspectHit
            {
                Kind = HitKind.World,
                Texture = "BiteMark",
                W = 5,
                H = 12,
                NotReplaceable = "made in code",
                Alpha = 0.5f,
            },
            Tex("pointer", HitKind.Cursor),
        };
        var e = InspectModel.Entries(hits);
        Check(e.Count == 6 && e.Last().Hit.Kind == HitKind.Cursor, "at most 6 entries, cursor last");
        Check(e.Count(x => x.Title == "path") == 1, "a texture drawn twice is listed once");
        Check(
            e[1].Detail.Contains("art/items/101.png") && e[1].Title.Contains("Fishing Rod"),
            "an item icon is named by its id",
            e[1].Detail
        );
        Check(e[2].Detail.Contains("another plugin draws"), "a koi another plugin draws says so", e[2].Detail);
        var made = InspectModel.Entries(hits.Where(h => h.Texture == "BiteMark"))[0];
        Check(!made.Exportable && made.Detail.Contains("cannot be replaced"), "art made in code cannot be saved");
        var same = InspectModel.Entries(hits.Where(h => h.Texture != "tree_1"));
        var moved = InspectModel.Entries(hits.Skip(1));
        Check(
            InspectModel.Keep(e, 2, same) == 2 && InspectModel.Keep(e, 4, same) == 0,
            "on the same topmost thing the mark stays while its entry is listed"
        );
        Check(InspectModel.Keep(e, 2, moved) == 0, "pointing at something new marks the topmost entry");
    }

    // Inspect mode lists what is drawn at the pixel, not what is hidden behind it, and never whose character it is.
    static void InspectOnlyVisible()
    {
        InspectHit Hit(string tex, HitKind kind, float alpha, PlayerPart player = PlayerPart.None) =>
            new InspectHit
            {
                Kind = kind,
                Texture = tex,
                W = 32,
                H = 32,
                Alpha = alpha,
                Player = player,
            };
        var cursor = Hit("pointer", HitKind.Cursor, 1);
        var tile = Hit("grass", HitKind.Ground, 1);
        string Listed(params InspectHit[] hits) => string.Join(",", InspectModel.Entries(hits).Select(e => e.Title));

        Check(
            Listed(Hit("tree", HitKind.World, 1), Hit("body", HitKind.World, 1, PlayerPart.Another), tile, cursor)
                == "tree,grass,pointer",
            "a player behind a tree is not listed; the tile under the tree and the cursor are",
            Listed(Hit("tree", HitKind.World, 1), Hit("body", HitKind.World, 1, PlayerPart.Another), tile, cursor)
        );
        Check(
            Listed(Hit("tree", HitKind.World, 0), Hit("body", HitKind.World, 1, PlayerPart.Another), tile, cursor)
                == "body,grass,pointer",
            "where the tree's pixel is clear, what is behind it is listed"
        );
        Check(
            Listed(
                Hit("glow", HitKind.World, 0.5f),
                Hit("body", HitKind.World, 1),
                Hit("stone", HitKind.World, 1),
                tile
            ) == "glow,body,grass",
            "a see-through sprite on top and what shows through it are listed, nothing under that"
        );
        Check(
            Listed(Hit("plate", HitKind.Screen, 1), Hit("tree", HitKind.World, 1), tile, cursor) == "plate,pointer",
            "under opaque screen art neither the world nor its tile is listed"
        );
        Check(Listed(tile, Hit("water", HitKind.Ground, 1), cursor) == "grass,pointer", "bare ground: its top tile");
        var other = InspectModel.Entries(new[] { Hit("idling_front_1", HitKind.World, 1, PlayerPart.Another) })[0];
        var mine = InspectModel.Entries(new[] { Hit("idling_front_1", HitKind.World, 1, PlayerPart.Yours) })[0];
        Check(
            other.Detail.StartsWith("another player's character ") && mine.Detail.StartsWith("your character "),
            "a character is described as yours or another player's, nothing more",
            other.Detail + " / " + mine.Detail
        );
    }

    static void TryCallbacks()
    {
        var spy = new SpyArea();
        var tools = new FakeTools();
        using var rig = new Rig(spy, new ToolsArea(tools));
        rig.Mod(
            "t",
            @"
function OnZoneChanged(zone) Out('zone', zone) GamePrint('welcome to ' .. zone) end
function OnChatMessage(m) Out('chat', m.type, m.sender, m.text) end
function OnKey(id) Out('key', id) end
function OnWalletChanged(a, b, c, d) Out('wallet', a, b, c, d) end
function OnItemGained(i) Out('item', i.id, i.name, i.count, i.meta, i.category) end
function OnItemTooltip(i) Out('tooltip', i.id, i.count) return 'Seen before' end
function OnSettingChanged(id, v) Out('setting', id, v) end
function OnRainChanged(r) Out('rain', r) end
function OnPlayerReady() local n = 0 for i = 1, 400000 do n = n + i end Out('ready') end
function Plain() GamePrint('not a test') end
CommandRegister('try', 'mine')
"
        );
        rig.Write(
            "t",
            "settings.lua",
            "mod_settings = { { id = 'toggle', ui_name = 'Toggle', type = 'key', value_default = 'F4' },"
                + " { id = 'color', ui_name = 'Colour', value_default = 'White', values = { {'White','White'} } } }"
        );
        rig.Mod("other", "function OnZoneChanged(zone) Out('other', zone) end");
        rig.Mod("bad", "function OnZoneChanged(zone) error('boom') end");
        rig.Start();
        int lines = rig.Env.ChatLines.Count;
        string Try(string args)
        {
            rig.Commands.Type("try " + args);
            string said = string.Join("|", rig.Env.ChatLines.Skip(lines)).Replace('＂', '"');
            lines = rig.Env.ChatLines.Count;
            return said;
        }
        Check(rig.Env.ChatText.Contains("/try is not available"), "no mod can take /try", rig.Env.ChatText);
        string said = Try("t town");
        Check(spy.Out.LastOrDefault() == "zone\ttown", "a zone name calls OnZoneChanged with it", said);
        Check(!spy.Out.Any(o => o.StartsWith("other")), "no other mod hears it");
        Check(
            said.Contains("[test] [t] welcome to town") && said.Contains("[test] t OnZoneChanged(\"town\") ran ("),
            "the mod's chat lines and the result are marked as a test",
            said
        );
        var lua = rig["t"].Lua;
        lua.Call(lua.Global("Plain"), Limits.Callback);
        Check(
            rig.Env.ChatLines.Last().Contains("not a test") && !rig.Env.ChatLines.Last().Contains("[test]"),
            "outside /try nothing is marked"
        );
        lines = rig.Env.ChatLines.Count;
        Try("t whisper Friend see you  at the dock");
        Check(
            spy.Out.Last() == "chat\tprivate\tFriend\tsee you  at the dock",
            "whisper: a private message from the name, the rest as typed",
            spy.Out.Last()
        );
        Try("t global Friend");
        Check(spy.Out.Last() == "chat\tglobal\tFriend\thello", "a chat line without text says hello");
        Try("t earn 250");
        Check(spy.Out.Last() == "wallet\t5250\t3\t250\t0", "earn: the wallet now plus the amount", spy.Out.Last());
        Try("t spend 40");
        Check(spy.Out.Last() == "wallet\t4960\t3\t-40\t0", "spend: a negative change");
        Try("t item 101 2");
        Check(
            spy.Out.Last() == "item\t101\tItem 101\t2\t0\tfarming",
            "item: the table OnItemGained gets",
            spy.Out.Last()
        );
        Try("t koi 160");
        Check(spy.Out.Last() == "item\t9999\tKoi 160\t1\t160\tfarming", "koi: item 9999 with the koi as meta");
        said = Try("t item 5000");
        Check(
            said.Contains("there is no item 5000") && spy.Out.Last().StartsWith("item\t9999"),
            "an unknown item calls nothing",
            said
        );
        said = Try("t tooltip 101");
        Check(
            spy.Out.Last() == "tooltip\t101\tnil" && said.Contains("gave \"Seen before\""),
            "tooltip: the table without a count, and the line it returns",
            said
        );
        Try("t key");
        Check(spy.Out.Last() == "key\ttoggle", "key alone: the mod's only key setting");
        said = Try("t key F4");
        Check(
            said.Contains("key takes the id of a key setting: toggle"),
            "key with another word names the key settings",
            said
        );
        Try("t setting color");
        Check(spy.Out.Last() == "setting\tcolor\tWhite", "setting: its id and its value now");
        Try("t rain stop");
        Check(spy.Out.Last() == "rain\tfalse", "rain stop: OnRainChanged( false )");
        said = Try("t ready");
        Check(
            spy.Out.Last() == "ready" && rig["t"].Status == ModStatus.Loaded && said.Contains("of 250 ms"),
            "ready: OnPlayerReady with init.lua's budget, as the game's call",
            said
        );
        Try("t OnZoneChanged holyland");
        Check(spy.Out.Last() == "zone\tholyland", "a callback by name with a typed value");
        Try("t OnWalletChanged 1 2 \"3\" true");
        Check(
            spy.Out.Last() == "wallet\t1\t2\t3\ttrue",
            "typed values: numbers, quoted text, booleans",
            spy.Out.Last()
        );
        said = Try("t OnChatMessage hi");
        Check(said.Contains("gets a table; try whisper"), "a callback that gets a table points to its shortcut", said);
        said = Try("t day");
        Check(said.Contains("it has no function OnLightChanged"), "an event the mod does not handle says so", said);
        said = Try("t frobnicate");
        Check(
            said.Contains("\"frobnicate\" is nothing /try knows") && said.Contains("/try t lists"),
            "an unknown word says how to list",
            said
        );
        said = Try("t");
        Check(
            said.Contains("town, playerland, holyland (OnZoneChanged)")
                && said.Contains("key toggle (OnKey)")
                && !said.Contains("OnLightChanged"),
            "/try <mod> lists the shortcuts of the callbacks it has",
            said
        );
        rig.Mod(
            "all",
            string.Join(
                "\n",
                new[]
                {
                    "OnPlayerReady",
                    "OnZoneChanged",
                    "OnActivityChanged",
                    "OnChatMessage",
                    "OnRainChanged",
                    "OnLightChanged",
                    "OnWalletChanged",
                    "OnItemGained",
                    "OnInventoryChanged",
                    "OnOfferingHasOwnChanged",
                    "OnOfferingRoundStarted",
                    "OnKey",
                    "OnSettingChanged",
                    "OnItemTooltip",
                    "OnWindowClosed",
                }.Select(f => "function " + f + "() end")
            )
        );
        rig.Write(
            "all",
            "settings.lua",
            "mod_settings = { { id = 'k', ui_name = 'K', type = 'key', value_default = 'F4' } }"
        );
        rig.Host.ReloadAll();
        var everything = TryCommand.Shortcuts(_ => true, rig.Host.Area<ToolsArea>().FactsOf(rig["all"]));
        var broken = everything
            .Select(x => TryCommand.Example(x.Words))
            .Where(e => !Try("all " + e).Contains(" ran ("))
            .ToList();
        Check(
            everything.Count == 15 && broken.Count == 0,
            "the example of every shortcut (the Mods page lists them) works",
            string.Join(", ", broken)
        );
        said = Try("tt town");
        Check(said.Contains("There is no mod \"tt\"; did you mean t?"), "an unknown mod is named", said);
        said = Try("bad town");
        Check(
            rig["bad"].Status == ModStatus.Failed && said.Contains("stopped the mod"),
            "an error in the tried callback stops the mod as the event would",
            said
        );
        said = Try("bad town");
        Check(said.Contains("bad is not running (failed"), "a stopped mod cannot be tried", said);
    }
}
