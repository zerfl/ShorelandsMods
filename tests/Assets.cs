using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

// Art, sounds and cues: file headers, names, xml, load-order conflicts, the report, the sound rules and the
// Lua sound functions, driven through the host with fake game stages.
static partial class Program
{
    static void Headers()
    {
        Check(
            FileHeaders.Png(TempFile("a.png", Files.Png(224, 32)), out int w, out int h, out _) && w == 224 && h == 32,
            "PNG size from its header"
        );
        var truncated = Files.Png(10, 10).Take(20).ToArray();
        Check(
            !FileHeaders.Png(TempFile("t.png", truncated), out _, out _, out string p1) && p1.Contains("not a PNG"),
            "cut-off PNG refused",
            p1
        );
        Check(
            !FileHeaders.Png(
                TempFile(
                    "j.png",
                    new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                ),
                out _,
                out _,
                out _
            ),
            "JPEG named .png refused"
        );
        Check(
            !FileHeaders.Png(TempFile("big.png", Files.Png(5000, 10)), out _, out _, out string p2)
                && p2.Contains("at most"),
            "oversized PNG refused",
            p2
        );

        Check(
            FileHeaders.Audio(TempFile("a.wav", Files.Wav(0.5)), out double s1, out _) && Math.Abs(s1 - 0.5) < 0.001,
            "WAV length",
            s1.ToString()
        );
        Check(
            FileHeaders.Audio(Files.Fixture("tone.ogg"), out double s2, out string p3) && Math.Abs(s2 - 0.5) < 0.01,
            "OGG Vorbis length from the last page",
            s2 + " " + p3
        );
        Check(
            !FileHeaders.Audio(Files.Fixture("opus.ogg"), out _, out string p4) && p4.Contains("Opus"),
            "Opus refused with a hint",
            p4
        );
        Check(
            !FileHeaders.Audio(TempFile("x.ogg", Files.Wav(0.1)), out _, out string p5) && p5.Contains("not an OGG"),
            "WAV named .ogg refused",
            p5
        );
        var cut = File.ReadAllBytes(Files.Fixture("tone.ogg")).Take(200).ToArray();
        Check(
            !FileHeaders.Audio(TempFile("cut.ogg", cut), out _, out string p6),
            "OGG cut off before its last page refused",
            p6
        );
    }

    static void Scan()
    {
        using (var rig = new Rig())
        {
            rig.Mod("pack", null);
            foreach (
                var f in new[]
                {
                    "art/textures/hotbar_latest.png",
                    "art/items/12.png",
                    "art/koi/69.png",
                    "art/items/hoe.png",
                    "art/other/x.png",
                    "art/textures/deep/x.png",
                    "art/textures/notes.jpg",
                    "art/README.txt",
                    "art/textures/Thumbs.db",
                    "art/textures/desktop.ini",
                    "cues/notes.md",
                }
            )
                rig.WriteBytes("pack", f, Files.Png(4, 4));
            foreach (
                var f in new[]
                {
                    "sounds/wallet_gain.ogg",
                    "sounds/wallet_gain.wav",
                    "sounds/walet_loss.wav",
                    "sounds/chat.mp3",
                    "cues/fishing_bite.png",
                    "cues/fishing_bite.wav",
                    "cues/fishing_bit.png",
                }
            )
                rig.WriteBytes("pack", f, new byte[4]);
            rig.Write("pack", "sounds/sounds.xml", "<Sounds/>");
            var scan = new AssetScan();
            scan.Load(Path.Combine(rig.Env.ModsDir, "pack"));
            string used = string.Join(",", scan.Files.Select(f => f.Kind + ":" + f.Key));
            Check(
                used
                    == "Item:12,Koi:69,Texture:hotbar_latest,CueImage:fishing_bite,CueSound:fishing_bite,Sound:wallet_gain",
                "files classified by folder and name",
                used
            );
            string Why(string rel) => scan.Ignored.FirstOrDefault(l => l.File == rel)?.Detail ?? "(used)";
            Check(Why("art/items/hoe.png").Contains("item id"), "item file must be an id", Why("art/items/hoe.png"));
            Check(Why("art/other/x.png").Contains("textures/, items/ and koi/"), "unknown art folder");
            Check(Why("art/textures/deep/x.png").Contains("no deeper"), "subfolder of textures");
            Check(Why("art/textures/notes.jpg").Contains(".png"), "non-png in art");
            Check(
                !scan.Ignored.Any(l =>
                    l.File.EndsWith("README.txt")
                    || l.File.EndsWith(".db")
                    || l.File.EndsWith(".ini")
                    || l.File.EndsWith(".md")
                ),
                "notes and Thumbs.db/desktop.ini are not reported",
                string.Join(",", scan.Ignored.Select(l => l.File))
            );
            Check(
                Why("sounds/walet_loss.wav").Contains("did you mean wallet_loss"),
                "unknown sound name suggests the nearest",
                Why("sounds/walet_loss.wav")
            );
            Check(Why("sounds/chat.mp3").Contains(".ogg and .wav"), "mp3 refused");
            Check(Why("sounds/wallet_gain.wav").Contains("wallet_gain.ogg"), "second file for the same sound named");
            Check(Why("cues/fishing_bit.png").Contains("did you mean fishing_bite"), "unknown cue slot suggests");
            Check(scan.SoundsXml != null && scan.CuesXml == null, "sounds.xml found");
        }
    }

    static void Conflicts()
    {
        var order = new List<string> { "a", "b", "c" };
        var o = new Overrides<string>(id => order.IndexOf(id));
        o.Add("tex/x", "b", "B");
        o.Add("tex/x", "a", "A");
        Check(o.WinnerValue("tex/x") == "B", "later mod wins whatever the claim order");
        o.TakeDirty();
        o.RemoveMod("b");
        Check(
            o.WinnerValue("tex/x") == "A" && o.Dirty.Contains("tex/x"),
            "removing the winner hands the name to the next mod"
        );
        o.RemoveMod("a");
        Check(o.WinnerValue("tex/x") == null && !o.Has("tex/x"), "no claims left: the game's own");
        order.Reverse();
        o.Add("k", "a", "A");
        o.Add("k", "c", "C");
        Check(o.WinnerValue("k") == "A", "the load order decides, not the name");
    }

    static void Xml()
    {
        var p = new List<string>();
        var s = AssetXml.Sounds(
            "<Sounds>\n<Sound name=\"chat\" volume=\"0.4\" cooldown=\"2\"/>\n<Sound name=\"whisper\" volume=\"7\" colldown=\"1\"/>\n<Sound name=\"bogus\"/>\n</Sounds>",
            p
        );
        Check(s["chat"].Volume == 0.4 && s["chat"].Cooldown == 2, "sound volume and cooldown read");
        Check(
            s["whisper"].Volume == 1
                && p.Any(x => x.Contains("line 3") && x.Contains("volume=\"7\"") && x.Contains("0 to 1")),
            "volume out of range: default kept, line named",
            string.Join("|", p)
        );
        Check(p.Any(x => x.Contains("did you mean cooldown")), "unknown attribute suggests");
        Check(p.Any(x => x.Contains("line 4") && x.Contains("bogus")), "unknown sound name in xml");
        p.Clear();
        AssetXml.Sounds("<Sounds><Sound name=\"chat\"", p);
        Check(p.Count == 1 && p[0].Contains("not valid XML"), "broken xml is one problem", string.Join("|", p));
        p.Clear();
        AssetXml.Sounds("<Cues/>", p);
        Check(p.Count == 1 && p[0].Contains("<Sounds>"), "wrong root named");
        p.Clear();
        var c = AssetXml.Cues(
            "<Cues><Cue slot=\"fishing_bite\" hide_native=\"0\" anchor=\"head\" offset_y=\"24\" scale=\"2\" frames=\"4\" fps=\"12\" seconds=\"0.8\" volume=\"0.6\"/></Cues>",
            p
        );
        var b = c["fishing_bite"];
        Check(
            b.HideNative == false
                && b.Anchor == "mark"
                && b.OffsetY == 24
                && b.Scale == 2
                && b.Frames == 4
                && b.Fps == 12
                && b.Seconds == 0.8
                && b.Volume == 0.6,
            "cue attributes read"
        );
        Check(
            p.Count == 1 && p[0].Contains("anchor") && p[0].Contains("mark, player or bobber"),
            "bad anchor reported, default kept",
            string.Join("|", p)
        );
        p.Clear();
        c = AssetXml.Cues("<Cues><Cue slot=\"fishing_tug\" scale=\"1.5\"/></Cues>", p);
        Check(c["fishing_tug"].Scale == 1 && p[0].Contains("whole number"), "scale must be whole");
        Check(
            c["fishing_tug"].HideNative == null && c["fishing_tug"].Seconds == null,
            "unset hide_native and seconds stay unset"
        );
    }

    static void CueSize()
    {
        Check(CueRules.Problem(44, 12, 4, 2, out int fw) == null && fw == 11, "11x12 frames at 2x fit");
        Check(CueRules.Problem(44, 12, 3, 1, out _).Contains("3 equal frames"), "strip that does not split");
        Check(CueRules.Problem(64, 64, 1, 1, out _) == null, "64x64 at 1x is the limit");
        Check(
            CueRules.Problem(33, 10, 1, 2, out _).Contains("66x20"),
            "33 px at 2x is over 64",
            CueRules.Problem(33, 10, 1, 2, out _)
        );
        Check(CueRules.Problem(128, 16, 4, 3, out _).Contains("96x48"), "frame size, not strip size, is capped");
    }

    static void Names()
    {
        var n = new NamesCatalog { GameVersion = "0.69i" };
        Check(n.Seen("hoe", 24, 24) && !n.Seen("hoe", 24, 24), "a size is new once");
        n.Seen("LDR_LLL1_0", 16, 16);
        n.Seen("LDR_LLL1_0", 64, 64);
        n.Seen("Font Texture", 256, 256);
        n.Items[12] = ("Watering Can", 24, 24);
        n.Koi[69] = "Some Koi";
        string text = n.ToText();
        Check(
            text.Contains("LDR_LLL1_0\t16x16\nLDR_LLL1_0\t64x64")
                && text.Contains("12\t24x24\tWatering Can")
                && text.Contains("fishing_bite")
                && text.Contains("music_town_night"),
            "names file lists textures, items, sounds and cues"
        );
        var again = new NamesCatalog { GameVersion = "0.69i" };
        again.Merge(text);
        Check(
            again.SizesOf("Font Texture").Single() == (256, 256)
                && again.SizesOf("LDR_LLL1_0").Count() == 2
                && again.Items[12].Name == "Watering Can"
                && again.Koi[69] == "Some Koi",
            "the union survives a restart"
        );
        Check(again.ToText() == text, "merging and writing changes nothing");
        again.KoiShown = new List<int> { 0, 160 };
        string noted = again.ToText();
        Check(noted.Contains("shows only for: 0, 160\n69\tSome Koi"), "names file says which koi a mod can show");
        var merged = new NamesCatalog { GameVersion = "0.69i" };
        merged.Merge(noted);
        Check(merged.Koi.Count == 1 && merged.ToText() == text, "the koi note is not read back as a koi");
        var newer = new NamesCatalog { GameVersion = "0.70a" };
        newer.Merge(text);
        Check(!newer.KnowsTexture("hoe") && newer.Items.Count == 0, "another game version starts over");
    }

    // _names.txt says what the game draws at each cue slot, and the size of its "!".
    static void CueNames()
    {
        string text = new NamesCatalog().ToText();
        Check(
            AssetNames.CueSlots.All(s => text.Contains(s + "\t")) && text.Contains("5x12 pixels"),
            "each cue slot line says what the game draws",
            text.Substring(text.IndexOf("[cues]"))
        );
    }

    static Rig AssetRig(FakeArt art, FakeAudio audio, FakeCues cues) =>
        new Rig(new SoundArea(audio), new ArtArea(art), new CueArea(cues));

    static void ArtPacks()
    {
        var art = new FakeArt();
        using (var rig = AssetRig(art, new FakeAudio(), new FakeCues()))
        {
            File.WriteAllText(
                Path.Combine(rig.Env.ModsDir, "_names.txt"),
                "-- Game version .\n[textures]\nhotbar_latest\t224x32\n"
            );
            foreach (var id in new[] { "pack_a", "pack_b" })
            {
                rig.Mod(id, null);
                rig.WriteBytes(id, "art/textures/hotbar_latest.png", Files.Png(224, 32));
            }
            rig.WriteBytes("pack_a", "art/textures/pointer.png", Files.Png(12, 12));
            rig.WriteBytes("pack_b", "art/textures/hotbar_latest_big.png", Files.Png(10, 10));
            rig.Mod("pack_bad", null);
            rig.WriteBytes("pack_bad", "art/textures/hotbar_latest.png", Files.Png(225, 32));
            rig.WriteBytes("pack_bad", "art/textures/broken.png", new byte[] { 1, 2, 3 });
            rig.WriteBytes("pack_bad", "art/items/12.png", Files.Png(24, 24));
            rig.WriteBytes("pack_bad", "art/koi/5.png", Files.Png(24, 24));
            rig.Start();
            rig.Run(0.1);
            Check(
                art.Now["texture/hotbar_latest"].ModId == "pack_b",
                "the later pack wins a texture (pack_bad's is the wrong size)"
            );
            Check(art.Now["texture/pointer"].ModId == "pack_a", "a texture only one pack has");
            var a = rig.Host.AssetReport(rig["pack_a"]);
            Check(
                a.Any(l =>
                    l.File == "art/textures/hotbar_latest.png"
                    && l.State == AssetState.Overridden
                    && l.Detail == "pack_b"
                ),
                "the loser's report names the winner"
            );
            var bad = rig.Host.AssetReport(rig["pack_bad"]);
            string Line(string f) => bad.FirstOrDefault(l => l.File == f)?.ToString() ?? "(none)";
            Check(
                Line("art/textures/hotbar_latest.png").Contains("225x32")
                    && Line("art/textures/hotbar_latest.png").Contains("224x32"),
                "wrong size named with both sizes",
                Line("art/textures/hotbar_latest.png")
            );
            Check(Line("art/textures/broken.png").Contains("not a PNG"), "corrupt PNG reported");
            Check(
                Line("art/items/12.png").Contains("applied"),
                "item icons wait (claimed) while the tables are unknown",
                Line("art/items/12.png")
            );
            Check(
                rig.Env.ChatText.Contains("[pack_bad] 2 asset files are not used"),
                "one chat line for the problems at load",
                rig.Env.ChatText
            );

            // The tables arrive: item 12 is 24x24, koi 5 does not exist.
            art.Icons = new Dictionary<string, (int, int)> { ["item/12"] = (24, 24) };
            rig.Host.Area<ArtArea>().IconsKnown();
            rig.Run(0.1);
            Check(
                art.Now.ContainsKey("item/12") && !art.Now.ContainsKey("koi/5"),
                "unknown koi id dropped once the tables are known"
            );
            Check(
                rig.Env.ChatText.Contains("art/koi/5.png is not used: there is no such id"),
                "late rejection said in chat"
            );

            // A texture seen for the first time with another size than the claim.
            rig.Host.Area<ArtArea>().TextureSeen("pointer", 16, 16);
            rig.Run(0.1);
            Check(!art.Now.ContainsKey("texture/pointer"), "a claim the real texture does not fit is dropped");

            // Switching the winner off shows the other pack, then the original.
            art.Sets.Clear();
            rig.Env.Off.Add("pack_b");
            rig.Host.Reload("pack_b");
            rig.Run(0.1);
            Check(
                art.Now["texture/hotbar_latest"].ModId == "pack_a",
                "winner off: the next pack's file",
                string.Join(",", art.Sets)
            );
            Check(
                art.Sets.Count(s => s.StartsWith("texture/hotbar_latest=")) == 1,
                "applied once per frame, not once per step"
            );
            rig.Env.Off.Add("pack_a");
            rig.Host.Reload("pack_a");
            rig.Run(0.1);
            Check(!art.Now.ContainsKey("texture/hotbar_latest"), "both off: the game's own");

            // A reload that changes nothing for the winner applies nothing.
            rig.Env.Off.Clear();
            rig.Host.ReloadAll();
            rig.Run(0.1);
            art.Sets.Clear();
            rig.Host.Reload("pack_a");
            rig.Run(0.1);
            Check(
                art.Now["texture/hotbar_latest"].ModId == "pack_b"
                    && !art.Sets.Any(s => s.StartsWith("texture/hotbar_latest=")),
                "reloading the loser leaves the winner alone",
                string.Join(",", art.Sets)
            );

            var lines = CommandsArea.ModReport(rig.Host, rig["pack_bad"]);
            Check(
                lines[0].StartsWith("pack_bad") && lines.Any(l => l.Contains("broken.png: ignored")),
                "/mods <id> lists the files",
                string.Join("|", lines)
            );
            Check(
                File.ReadAllText(Path.Combine(rig.Env.ModsDir, "_names.txt")).Contains("pointer\t16x16"),
                "_names.txt written with what was seen"
            );
        }
    }

    static void SoundPacks()
    {
        var audio = new FakeAudio();
        using (var rig = AssetRig(new FakeArt(), audio, new FakeCues()))
        {
            rig.Mod("snd", null);
            foreach (
                var n in new[]
                {
                    "wallet_gain",
                    "chat",
                    "music_town",
                    "music_town_night",
                    "music_holyland",
                    "rain_loop",
                }
            )
                rig.WriteBytes("snd", "sounds/" + n + ".wav", Files.Wav(0.2));
            rig.WriteBytes("snd", "sounds/whisper.wav", Files.Wav(40));
            rig.Write(
                "snd",
                "sounds/sounds.xml",
                "<Sounds><Sound name=\"chat\" volume=\"0.3\" cooldown=\"1\"/><Sound name=\"zone_town\"/></Sounds>"
            );
            rig.Mod("snd2", null);
            rig.WriteBytes("snd2", "sounds/wallet_gain.wav", Files.Wav(0.2));
            rig.Start();
            var sound = rig.Host.Area<SoundArea>();
            sound.Event("chat");
            Check(audio.Plays.Count == 0, "nothing plays before the clip loaded");
            audio.ReadyAll();
            rig.Run(0.05);
            sound.Event("chat");
            sound.Event("chat");
            rig.Run(0.5);
            sound.Event("chat");
            rig.Run(0.6);
            sound.Event("chat");
            Check(
                string.Join("|", audio.Plays) == "chat.wav@0.3 pack|chat.wav@0.3 pack",
                "volume from sounds.xml; cooldown of 1 s holds",
                string.Join("|", audio.Plays)
            );
            audio.Plays.Clear();
            sound.Event("wallet_gain");
            Check(
                audio.Plays.Single() == "wallet_gain.wav@1 pack"
                    && audio.Clips.Values.Count(c => c.Path.Contains("snd2")) == 1,
                "the later mod's file plays"
            );
            var rep = rig.Host.AssetReport(rig["snd"]);
            Check(
                rep.Any(l =>
                    l.File == "sounds/whisper.wav" && l.State == AssetState.Ignored && l.Detail.Contains("40 s")
                ),
                "a 40 s event sound is refused"
            );
            Check(
                rep.Any(l => l.File == "sounds/sounds.xml" && l.Detail.Contains("zone_town")),
                "xml entry without a file reported"
            );
            Check(
                rep.Any(l =>
                    l.File == "sounds/wallet_gain.wav" && l.State == AssetState.Overridden && l.Detail == "snd2"
                ),
                "sound conflict reported"
            );

            // Music and rain loops.
            sound.SetZone("town");
            rig.Run(0.05);
            Check(audio.Playing == "music_town.wav", "zone music starts", audio.Playing);
            sound.SetNight(true);
            sound.SetRain(true);
            rig.Run(0.05);
            Check(audio.Playing == "music_town_night.wav,rain_loop.wav", "night variant and rain loop", audio.Playing);
            sound.SetZone("playerland");
            rig.Run(0.05);
            Check(audio.Playing == "rain_loop.wav", "a zone without music: music stops, rain goes on", audio.Playing);
            sound.SetZone("holyland");
            rig.Run(0.05);
            Check(
                audio.Playing == "music_holyland.wav,rain_loop.wav",
                "no night file: the day music at night",
                audio.Playing
            );
            sound.SetZone(null);
            rig.Run(0.05);
            Check(audio.Playing == "", "out of the world: silence", audio.Playing);

            // Reload: loops stop, clips are unloaded after the fade, then loaded again.
            sound.SetZone("town");
            sound.SetRain(false);
            sound.SetNight(false);
            rig.Run(0.05);
            int before = audio.Clips.Count;
            rig.Host.Reload("snd");
            Check(audio.Playing == "", "the reloaded mod's music stops", audio.Playing);
            rig.Run(SoundArea.Fade + 0.3);
            Check(
                audio.Unloaded.Contains("music_town.wav") && audio.Clips.Count == before,
                "old clips unloaded, new ones loaded",
                audio.Clips.Count + " vs " + before
            );
            audio.ReadyAll();
            rig.Run(0.05);
            Check(audio.Playing == "music_town.wav", "music back after the reload", audio.Playing);

            // A clip the game cannot decode is reported and dropped.
            audio.Fail("music_town.wav");
            rig.Run(0.05);
            Check(rig.Env.ChatText.Contains("sounds/music_town.wav does not play"), "undecodable clip said in chat");
        }
    }

    static void LuaSound()
    {
        var audio = new FakeAudio();
        using (var rig = AssetRig(new FakeArt(), audio, new FakeCues()))
        {
            rig.Mod(
                "ls",
                @"
                R = {}
                R.vol = tostring(SoundPlay('files/a.wav', 2))
                R.ok = tostring(SoundPlay('files/a.wav', 0.5))
                for i = 1, 4 do SoundLoop('l' .. i, 'files/a.wav') end
                R.fifth = tostring(SoundLoop('l5', 'files/a.wav'))
                R.again = tostring(SoundLoop('l1', 'files/a.wav', 0.2))
                for i = 1, 8 do SoundPlay('files/a.wav') end
                R.ret = select('#', SoundPlay('files/a.wav')) .. tostring(SoundPlay('files/a.wav'))
                function Stop() SoundStop('l2') end
                "
            );
            rig.WriteBytes("ls", "files/a.wav", Files.Wav(0.2));
            rig.WriteBytes("ls", "files/long.wav", Files.Wav(31));
            rig.Start();
            var lua = rig["ls"].Lua;
            string R(string k) => lua.Global("R").Read<Lua.LuaTable>()[k].ToString();
            void Refused(string call, string expect, string name)
            {
                var r = Soft(rig, call, id => rig.WriteBytes(id, "files/long.wav", Files.Wav(31)));
                Check(r.Result == "false" && r.Loaded && r.Notices.Contains(expect), name, r.Result + " " + r.Notices);
            }
            Refused("SoundPlay('../other/x.wav')", "inside the mod folder", "SoundPlay cannot leave the mod folder");
            Refused("SoundPlay('C:/Windows/Media/ding.wav')", "cannot use", "absolute path refused");
            Refused("SoundPlay('init.lua')", ".ogg or .wav", "only sound files");
            Refused("SoundPlay('files/nope.wav')", "no file files/nope.wav", "missing file named");
            Refused("SoundPlay('files/long.wav')", "31 s long", "clip length capped");
            Check(
                R("vol") == "false" && rig.Env.ChatRead.Contains("volume must be from 0 to 1"),
                "volume clamped, said"
            );
            Check(R("ok") == "true", "a fine call returns true");
            Check(R("fifth") == "false" && rig.Env.ChatRead.Contains("4 loops"), "at most 4 loops", R("fifth"));
            Check(R("again") == "true", "the same loop id replaces");
            Check(R("ret") == "1true", "SoundPlay says nothing about voices: true with all of them in use", R("ret"));
            Check(audio.Clips.Count == 1, "one clip per file per mod", audio.Clips.Count.ToString());
            audio.ReadyAll();
            rig.Run(0.05);
            Check(
                audio.Plays.Count == 12
                    && audio.Plays.All(p => p.EndsWith(" mod:ls"))
                    && audio.Plays[0].Contains("@1 ")
                    && audio.Plays[1].Contains("@0.5"),
                "waiting one-shots play once loaded, in the mod's own voice group, a clamped volume at 1",
                string.Join("|", audio.Plays)
            );
            Check(audio.Loops.Count == 4, "loops start once loaded", audio.Loops.Count.ToString());
            rig.Host.Call(rig["ls"], "Stop", lua.Global("Stop"));
            Check(audio.Loops.Count == 3, "SoundStop stops one loop");
            audio.Plays.Clear();
            rig.Run(2);
            Check(audio.Plays.Count == 0, "a one-shot is not repeated");
            rig.Host.Reload("ls");
            Check(audio.Loops.Count == 0, "unload stops every loop");
            rig.Run(SoundArea.Fade + 0.3);
            Check(audio.Unloaded.Contains("a.wav"), "and unloads the clips");
        }
    }

    static void CuePacks()
    {
        var cues = new FakeCues();
        var audio = new FakeAudio();
        using (var rig = AssetRig(new FakeArt(), audio, cues))
        {
            rig.Mod("cue_a", null);
            rig.WriteBytes("cue_a", "cues/fishing_bite.png", Files.Png(44, 12));
            rig.WriteBytes("cue_a", "cues/fishing_bite.wav", Files.Wav(0.3));
            rig.WriteBytes("cue_a", "cues/fishing_tug.wav", Files.Wav(0.3));
            rig.WriteBytes("cue_a", "cues/fishing_nibble.png", Files.Png(40, 40));
            rig.WriteBytes("cue_a", "cues/fishing_leave.wav", Files.Wav(6));
            rig.Write(
                "cue_a",
                "cues/cues.xml",
                "<Cues><Cue slot=\"fishing_bite\" frames=\"4\" scale=\"2\"/><Cue slot=\"fishing_nibble\" scale=\"2\" hide_native=\"1\"/><Cue slot=\"fishing_spook\"/><Cue slot=\"fishing_tug\" hide_native=\"1\"/></Cues>"
            );
            rig.Start();
            rig.Run(0.05);
            Check(!cues.Now.ContainsKey("fishing_bite"), "a cue waits for its sound to load");
            audio.ReadyAll();
            rig.Run(0.05);
            var bite = cues.Now["fishing_bite"];
            Check(
                bite.FrameW == 11 && bite.HideNative && bite.Clip >= 0,
                "bite: 4 frames of 11 px, replaces the native mark, with sound"
            );
            Check(
                !cues.Now["fishing_tug"].HideNative && cues.Now["fishing_tug"].ImageRel == null,
                "a sound-only cue accompanies"
            );
            Check(
                !cues.Now.ContainsKey("fishing_nibble") && !cues.Now.ContainsKey("fishing_leave"),
                "over the size cap or too long: not used"
            );
            var rep = rig.Host.AssetReport(rig["cue_a"]).Select(l => l.ToString()).ToList();
            Check(
                rep.Any(l => l.Contains("fishing_nibble.png: ignored") && l.Contains("80x80")),
                "size cap in the report",
                string.Join("|", rep)
            );
            Check(
                rep.Any(l => l.Contains("fishing_leave.wav: ignored") && l.Contains("at most 5 s")),
                "cue sound length cap"
            );
            Check(
                rep.Any(l => l.Contains("hide_native for fishing_tug does nothing")),
                "hide_native on a slot without a native mark"
            );
            Check(rep.Any(l => l.Contains("fishing_spook, but the mod has no")), "xml entry without files");

            rig.Mod("cue_b", null);
            rig.WriteBytes("cue_b", "cues/fishing_bite.png", Files.Png(10, 10));
            rig.Host.Reload("cue_b");
            rig.Run(0.05);
            Check(
                cues.Now["fishing_bite"].ModId == "cue_b" && cues.Now["fishing_bite"].Clip < 0,
                "the later mod takes the whole slot (its image, no sound)"
            );
            Check(
                rig.Host.AssetReport(rig["cue_a"]).Any(l => l.File == "cues/fishing_bite.wav" && l.Detail == "cue_b"),
                "the slot conflict reported per file"
            );
            Directory.Delete(Path.Combine(rig.Env.ModsDir, "cue_b"), true);
            rig.Host.Reload("cue_b");
            rig.Run(0.05);
            Check(cues.Now["fishing_bite"].ModId == "cue_a", "removing the winner restores the other mod's cue");
            Check(
                rig.Host.Api.Functions.All(f => !f.Name.ToLowerInvariant().Contains("cue"))
                    && rig.Host.Api.Callbacks.All(c => !c.Name.ToLowerInvariant().Contains("fish")),
                "no Lua function or callback for cues"
            );
        }
    }

    static void VoiceGroupDropped()
    {
        var audio = new FakeAudio();
        using var rig = AssetRig(new FakeArt(), audio, new FakeCues());
        rig.Mod("snd", "function Play() SoundPlay('files/a.wav') end");
        rig.WriteBytes("snd", "files/a.wav", Files.Wav(0.2));
        rig.Start();
        var mod = rig["snd"];
        rig.Host.Call(mod, "Play", mod.Lua.Global("Play"));
        audio.ReadyAll();
        rig.Run(0.2);
        Check(
            audio.Plays.Any(p => p.EndsWith(" mod:snd")),
            "the mod's sound plays in its group",
            string.Join(";", audio.Plays)
        );
        rig.Host.Reload("snd");
        Check(
            audio.Dropped.Contains("mod:snd"),
            "its voice group is dropped when it stops",
            string.Join(";", audio.Dropped)
        );
    }

    // /mods <id>: applied files counted per folder in one line; every other file on its own line, never cut; where
    // the data is.
    static void ModReportLines()
    {
        var art = new FakeArt();
        using var rig = AssetRig(art, new FakeAudio(), new FakeCues());
        rig.Mod("skin", null);
        for (int i = 0; i < 30; i++)
            rig.WriteBytes("skin", "art/textures/plate_with_a_long_name_" + i + ".png", Files.Png(8, 8));
        rig.WriteBytes("skin", "art/items/notanid.png", Files.Png(8, 8));
        rig.WriteBytes("skin", "art/textures/" + new string('w', 120) + ".png", new byte[] { 1, 2, 3 });
        rig.Start();
        rig.Run(0.1);
        var lines = CommandsArea.ModReport(rig.Host, rig["skin"]);
        var applied = lines.Where(l => l.Contains("applied")).ToList();
        Check(
            applied.Count == 1 && applied[0].Contains("30 files") && applied[0].Contains("art/textures/ 30"),
            "applied files are one counted line",
            string.Join(" / ", applied)
        );
        Check(
            lines.Any(l => l.Contains("art/items/notanid.png: ignored"))
                && lines.Any(l => l.Contains(new string('w', 120) + ".png: ignored") && !l.EndsWith("...")),
            "every ignored file has its full line",
            string.Join(" / ", lines)
        );
        Check(lines.Any(l => l.Contains(rig["skin"].DataDir)), "the report says where the mod's data and exports are");
    }

    static void IconTextureNotes()
    {
        var names = new NamesCatalog { GameVersion = "1" };
        names.Seen("hoe", 24, 24);
        names.Seen("pots", 48, 24);
        names.Seen("backpack_1", 32, 32);
        names.Items[100] = ("Hoe", 24, 24);
        names.Items[7] = ("Pot", 24, 24);
        names.Items[8] = ("Big pot", 24, 24);
        names.IconFrom("hoe", 100);
        names.IconFrom("pots", 7);
        names.IconFrom("pots", 8);
        string text = names.ToText();
        Check(
            text.Contains("\nhoe\t24x24\tthe icon of item 100 (Hoe): use art/items/100.png\n")
                && text.Contains("\npots\t48x24\tthe icons of items 7, 8: use art/items/<id>.png\n")
                && text.Contains("\nbackpack_1\t32x32\n"),
            "a texture an item icon is cut from says which item",
            text
        );
        var again = new NamesCatalog { GameVersion = "1" };
        again.Merge(text);
        Check(
            again.KnowsTexture("hoe")
                && again.KnowsTexture("pots")
                && again.IconTextures["pots"].SetEquals(new[] { 7, 8 }),
            "the noted textures and their items are read back from the file"
        );
    }

    // SoundLoop names a file in the mod folder as dofile does: a path that leaves the folder, or names no sound file, is
    // refused with a notice before anything is opened.
    static void SoundLoopPaths()
    {
        var audio = new FakeAudio();
        var spy = new SpyArea();
        using var rig = new Rig(new SoundArea(audio), spy);
        rig.WriteBytes("l", "sounds/ok.wav", Files.Wav(1));
        rig.WriteBytes("other", "sounds/x.wav", Files.Wav(1));
        rig.Mod(
            "l",
            @"
local bad = { '../other/sounds/x.wav', 'sounds/../../other/sounds/x.wav', '/x.wav', 'C:/x.wav', 'C:x.wav',
  '\\\\server\\share\\x.wav', 'sounds/ok.txt', 'mods/other/sounds/x.wav', 'sounds/ok.wav/../../../other/sounds/x.wav',
  '', 'sounds/./ok.wav', 'sounds/ok.wav\0.txt', 'sounds/ok.wav:stream', 'sounds/ok' }
for i, p in ipairs(bad) do Out('bad', SoundLoop('b' .. i, p)) end
Out('ok', SoundLoop('a', 'sounds/ok.wav'), SoundLoop('n', 'mods/l/sounds\\OK.WAV'))
"
        );
        rig.Start();
        var o = spy.Out;
        Check(
            o.Count(l => l == "bad\tfalse") == 14 && o.Contains("ok\ttrue\ttrue"),
            "every path out of the folder or to no sound file is refused; the mod's own file plays",
            string.Join("|", o)
        );
        Check(
            audio.Clips.Values.All(c =>
                c.Path.EndsWith(Path.Combine("l", "sounds", "ok.wav"), StringComparison.OrdinalIgnoreCase)
            )
                && audio.Clips.Count >= 1,
            "only the mod's own file is opened",
            string.Join("|", audio.Clips.Values.Select(c => c.Path))
        );
        Check(
            rig.Env.ChatRead.Contains("[l] SoundLoop: \"../other/sounds/x.wav\" must stay inside the mod folder"),
            "the first refusal is told",
            rig.Env.ChatText
        );
    }
}
