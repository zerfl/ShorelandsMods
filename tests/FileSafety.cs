using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;

// Files the host opens for a mod: hostile PNG, WAV and OGG files made here, real junctions in a temp
// folder, reserved folder names, size caps.
static partial class Program
{
    // ---- PNG ----

    static void PngChecks()
    {
        string ok(byte[] png, int maxSide = 256)
        {
            var data = FileHeaders.PngData(TempFile("p.png", png), maxSide, out int w, out int h, out string why);
            return data != null ? "ok " + w + "x" + h : why;
        }
        Check(ok(Png(Chunks(24, 24))) == "ok 24x24", "a plain RGBA PNG passes", ok(Png(Chunks(24, 24))));
        Check(ok(Png(Chunks(16, 16, interlace: true))) == "ok 16x16", "an interlaced PNG passes");
        Check(ok(Png(Chunks(24, 24, depth: 16))) == "ok 24x24", "a 16-bit PNG passes");
        Check(ok(Png(Chunks(24, 24, depth: 4, type: 3))) == "ok 24x24", "a palette PNG passes");
        Check(ok(Png(Chunks(24, 24, type: 0, depth: 1))) == "ok 24x24", "a 1-bit grey PNG passes");
        var text = Chunks(24, 24);
        text.Insert(1, ("tEXt", Encoding.ASCII.GetBytes("Comment\0made by a paint program")));
        Check(ok(Png(text)) == "ok 24x24", "text chunks are allowed");

        var noPalette = Chunks(24, 24, depth: 8, type: 3);
        noPalette.RemoveAll(c => c.Type == "PLTE");
        Check(ok(Png(noPalette)).Contains("palette"), "a palette PNG without PLTE is refused", ok(Png(noPalette)));

        var huge = Chunks(8192, 8192);
        huge[1] = ("IDAT", Random(65000));
        Check(ok(Png(huge)).Contains("8192x8192"), "huge declared size with little data: refused by the header");
        Check(
            !FileHeaders.Png(TempFile("h.png", Png(huge)), out _, out _, out string why1, 256)
                && why1.Contains("256x256"),
            "the header check alone refuses it for an icon",
            why1
        );

        var padded = Chunks(24, 24);
        padded.Insert(1, ("zzZz", new byte[2 << 20]));
        Check(
            ok(Png(padded)).Contains("can need"),
            "a small image in a big file is refused before reading",
            ok(Png(padded))
        );

        var cut = Png(Chunks(24, 24));
        cut = cut.Take(cut.Length - 20).ToArray();
        Check(ok(cut).Contains("cut off"), "a PNG cut inside its chunks is refused", ok(cut));

        var garbage = Png(Chunks(24, 24)).Take(33).Concat(Random(3000)).ToArray();
        Check(
            ok(garbage) != null && !ok(garbage).StartsWith("ok"),
            "a correct header followed by garbage is refused",
            ok(garbage)
        );

        var badCrc = Png(Chunks(24, 24));
        badCrc[45] ^= 0x40;
        Check(ok(badCrc).Contains("checksum"), "image data with a wrong checksum is refused", ok(badCrc));

        var unknown = Chunks(24, 24);
        unknown.Insert(1, ("ABCD", new byte[4]));
        Check(ok(Png(unknown)).Contains("ABCD"), "an unknown critical chunk is refused");

        var apng = Chunks(24, 24);
        apng.Insert(1, ("acTL", new byte[8]));
        Check(ok(Png(apng)).Contains("animated"), "an APNG is refused with a sentence");

        Check(
            ok(Png(Chunks(24, 24)).Concat(new byte[] { 1, 2, 3 }).ToArray()).Contains("after its end"),
            "bytes after IEND are refused"
        );

        var split = Chunks(24, 24);
        byte[] idat = split[1].Data;
        split[1] = ("IDAT", idat.Take(10).ToArray());
        split.Insert(2, ("tEXt", Encoding.ASCII.GetBytes("a\0b")));
        split.Insert(3, ("IDAT", idat.Skip(10).ToArray()));
        Check(ok(Png(split)).Contains("split"), "image data split by another chunk is refused");

        var bloated = Chunks(4, 4);
        bloated[1] = ("IDAT", Random(5000));
        Check(
            ok(Png(bloated)).Contains("more image data"),
            "more image data than the size can need is refused",
            ok(Png(bloated))
        );
    }

    // HudIcon / list icons: an 8192x8192 icon is refused at the call, and each file is checked once per
    // mod start, refused or not.
    static void IconFiles()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "big",
            @"
Out('b', HudIcon('b', 'files/b.png', { anchor = 'top' }))
Out('g', HudIcon('g', 'files/g.png'))
Out('rows', WindowCreate('w', 'W', 100, 100) and WindowSetList('w', { { text = 'x', icon = 'files/b.png' } }))
function OnTick() n = (n or 0) + 1 if n == 3 then Out('again', HudIcon('g', 'files/g.png')) end end
"
        );
        var huge = Chunks(8192, 8192);
        huge[1] = ("IDAT", Random(65000));
        rig.WriteBytes("big", "files/b.png", Png(huge));
        rig.WriteBytes("big", "files/g.png", Png(Chunks(24, 24)));
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        Check(
            o.Contains("b\tfalse") && o.Contains("g\ttrue"),
            "an 8192x8192 icon file is refused, a 24x24 one taken",
            string.Join("|", o)
        );
        Check(
            rig.Env.ChatRead.Contains("8192x8192 pixels; at most 256x256"),
            "the refusal names the size",
            rig.Env.ChatRead
        );
        Check(o.Contains("rows\tfalse"), "a list row with that icon is refused too", string.Join("|", o));
        Check(
            rig.Hud.Widgets.Single(w => w.Id == "g").Icon.Stamp != 0 && !rig.Hud.Widgets.Any(w => w.Id == "b"),
            "only the good icon makes a widget"
        );
        File.Delete(Path.Combine(rig.Env.ModsDir, "big", "files", "g.png"));
        rig.Run(1);
        Check(
            o.Contains("again\ttrue"),
            "an icon file is checked once per mod start, not on every call",
            string.Join("|", o)
        );
    }

    // art/: what a mod's images hold together is bounded before any of them is decoded.
    static void ArtBudget()
    {
        using var rig = new Rig(new ArtArea(new FakeArt()));
        rig.Mod("pack", null);
        rig.WriteBytes("pack", "art/textures/big_a.png", Files.Png(4000, 3000));
        rig.WriteBytes("pack", "art/textures/big_b.png", Files.Png(4000, 3000));
        rig.Start();
        var lines = rig.Host.AssetReport(rig["pack"]);
        Check(
            lines.Any(l => l.File == "art/textures/big_b.png" && l.Detail.Contains("16 million pixels"))
                && lines.Any(l => l.File == "art/textures/big_a.png" && l.State != AssetState.Ignored),
            "a second 12-million-pixel image is over the mod's art budget",
            string.Join("|", lines.Select(l => l.File + " " + l.State + " " + l.Detail))
        );
    }

    // ---- sounds ----

    static void SoundLengths()
    {
        var tone = OggPackets(File.ReadAllBytes(Files.Fixture("tone.ogg")));
        var headers = tone.Take(3).ToList();
        byte longMode = tone[5][0];
        var forged = headers.Concat(Enumerable.Repeat(new[] { longMode }, 2000)).ToList();
        Check(
            FileHeaders.Audio(TempFile("short.ogg", Ogg(forged, 44100)), out double s1, out _)
                && Math.Abs(s1 - 1999 * 1024 / 44100.0) < 0.01,
            "an OGG whose last page claims 1 s is as long as its 2,000 long blocks play",
            s1.ToString()
        );
        var hidden = forged.Take(forged.Count - 1).ToList();
        hidden.Add(
            new[] { longMode }
                .Concat(Encoding.ASCII.GetBytes("OggS\0\0"))
                .Concat(BitConverter.GetBytes(100L))
                .Concat(new byte[20])
                .ToArray()
        );
        Check(
            FileHeaders.Audio(TempFile("hidden.ogg", Ogg(hidden, 1999 * 1024)), out double s2, out _) && s2 > 46,
            "OggS bytes inside the sound do not pass for its last page",
            s2.ToString()
        );
        var big = Ogg(headers.Concat(new[] { new[] { longMode } }).ToList(), long.MaxValue / 2);
        Check(
            FileHeaders.Audio(TempFile("big.ogg", big), out double s3, out _) && s3 > 1e9,
            "a huge sample count is a huge length",
            s3.ToString()
        );

        byte[] real = File.ReadAllBytes(Files.Fixture("tone.ogg"));
        var chained = real.Concat(Ogg(forged, 44100, serial: 7)).ToArray();
        Check(
            !FileHeaders.Audio(TempFile("chain.ogg", chained), out _, out string p1)
                && p1.Contains("more than one stream"),
            "a second stream after the first is refused",
            p1
        );
        var damaged = (byte[])real.Clone();
        damaged[damaged.Length - 100] ^= 1;
        Check(
            !FileHeaders.Audio(TempFile("crc.ogg", damaged), out _, out string p2) && p2.Contains("checksum"),
            "a page with a wrong checksum is refused",
            p2
        );
        Check(
            !FileHeaders.Audio(TempFile("tail.ogg", real.Concat(Random(100)).ToArray()), out _, out string p3),
            "bytes after the last page are refused",
            p3
        );

        var open = Files.Wav(40);
        Array.Clear(open, 40, 4);
        Check(
            FileHeaders.Audio(TempFile("open.wav", open), out double s4, out _) && Math.Abs(s4 - 40) < 0.01,
            "a WAV whose data size says 0 is as long as its data",
            s4.ToString()
        );

        var audio = new FakeAudio();
        using var rig = new Rig(new SpyArea(), new SoundArea(audio));
        rig.Mod("snd", "Out(SoundPlay('files/short.ogg')) Out(SoundPlay('files/short.ogg'))");
        rig.WriteBytes("snd", "files/short.ogg", Ogg(forged, 44100));
        rig.Start();
        Check(
            rig.Host.Area<SpyArea>().Out.SequenceEqual(new[] { "false", "false" })
                && rig.Env.ChatRead.Contains("46.4 s long"),
            "SoundPlay refuses the forged OGG by its real length",
            rig.Env.ChatRead
        );
        Check(audio.Clips.Count == 0, "nothing reaches the decoder");
    }

    // ---- links ----

    static void LinksInModFolders()
    {
        var audio = new FakeAudio();
        using var rig = new Rig(
            new SpyArea(),
            new SoundArea(audio),
            new ArtArea(new FakeArt()),
            new CueArea(new FakeCues())
        );
        try
        {
            string outside = Path.Combine(rig.Env.Root, "outside");
            Directory.CreateDirectory(Path.Combine(outside, "textures"));
            File.WriteAllText(Path.Combine(outside, "secret.lua"), "return 'secret'");
            File.WriteAllBytes(Path.Combine(outside, "icon.png"), Png(Chunks(24, 24)));
            File.WriteAllBytes(Path.Combine(outside, "s.wav"), Files.Wav(0.2));
            File.WriteAllBytes(Path.Combine(outside, "textures", "hoe.png"), Files.Png(24, 24));
            rig.Mod(
                "m",
                @"
    local ok, e = pcall(dofile, 'files/secret.lua') Out('dofile', ok, e)
    Out('icon', HudIcon('i', 'files/icon.png'))
    Out('sound', SoundPlay('files/s.wav'))
    "
            );
            rig.Mod("n", null);
            Directory.CreateDirectory(Path.Combine(rig.Env.ModsDir, "n", "art"));
            string elsewhere = Path.Combine(rig.Env.Root, "elsewhere", "linked");
            Directory.CreateDirectory(Path.Combine(elsewhere, "files"));
            File.WriteAllText(Path.Combine(elsewhere, "mod.xml"), "<Mod name=\"linked\" />");
            File.WriteAllText(Path.Combine(elsewhere, "init.lua"), "Out('linked', dofile('files/a.lua'))");
            File.WriteAllText(Path.Combine(elsewhere, "files", "a.lua"), "return 'works'");
            if (
                !Junction(Path.Combine(rig.Env.ModsDir, "m", "files"), outside)
                || !Junction(Path.Combine(rig.Env.ModsDir, "m", "art"), outside)
                || !Junction(Path.Combine(rig.Env.ModsDir, "n", "art", "textures"), Path.Combine(outside, "textures"))
                || !Junction(Path.Combine(rig.Env.ModsDir, "linked"), elsewhere)
            )
            {
                Console.WriteLine("skipped: LinksInModFolders (this account cannot create junctions)");
                return;
            }
            rig.Start();
            var o = rig.Host.Area<SpyArea>().Out;
            Check(
                o.Any(l => l.StartsWith("dofile\tfalse") && l.Contains("files/ is a link"))
                    && !o.Any(l => l.Contains("secret")),
                "dofile does not read through a junction in the mod folder",
                string.Join("|", o)
            );
            Check(
                o.Contains("icon\tfalse") && o.Contains("sound\tfalse"),
                "icons and sounds are not read through it",
                string.Join("|", o)
            );
            Check(audio.Clips.Count == 0, "no sound reached the decoder");
            var m = AssetScan.Of(rig["m"]);
            Check(
                m.Files.Count == 0 && m.Ignored.Any(l => l.File == "art/" && l.Detail.Contains("link")),
                "an art folder that is a junction is not scanned, and says why",
                string.Join("|", m.Ignored.Select(l => l.File + " " + l.Detail))
            );
            var n = AssetScan.Of(rig["n"]);
            Check(
                n.Files.Count == 0 && n.Ignored.Any(l => l.File == "art/textures/" && l.Detail.Contains("link")),
                "a junction deeper in art/ is not followed",
                string.Join("|", n.Ignored.Select(l => l.File + " " + l.Detail))
            );
            Check(
                o.Contains("linked\tworks"),
                "a mod folder that is itself a junction loads (the player's choice)",
                string.Join("|", o)
            );

            string symlink = Path.Combine(rig.Env.ModsDir, "n", "files", "s.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(symlink));
            if (Mklink("\"" + symlink + "\" \"" + Path.Combine(outside, "secret.lua") + "\""))
                Check(
                    ModPaths.ResolveLua(Path.Combine(rig.Env.ModsDir, "n"), "n", "files/s.lua", out _, out string why)
                        == null
                        && why.Contains("files/s.lua is a link"),
                    "a file symbolic link is refused",
                    why
                );
            else
                Console.WriteLine("skipped: file symbolic link (needs Developer Mode or admin)");
        }
        finally
        {
            Unlink();
        }
    }

    static void LinksInHostFolders()
    {
        using var rig = new Rig(new SpyArea());
        try
        {
            string outside = Path.Combine(rig.Env.Root, "outside");
            Directory.CreateDirectory(outside);
            rig.Mod("e", "Out(ExportText('x', 'hello'))");
            rig.Mod("d", "DataSave('k', 1)");
            Directory.CreateDirectory(Path.Combine(rig.Env.DataRoot, "e"));
            if (
                !Junction(Path.Combine(rig.Env.DataRoot, "e", "export"), outside)
                || !Junction(Path.Combine(rig.Env.DataRoot, "d"), outside)
                || !Junction(Path.Combine(rig.Env.ModsDir, "_originals"), outside)
            )
            {
                Console.WriteLine("skipped: LinksInHostFolders (this account cannot create junctions)");
                return;
            }
            rig.Start();
            rig.Run(3);
            Check(
                Directory.GetFileSystemEntries(outside).Length == 0,
                "ExportText and saved data write nothing through a junction in mods_data",
                string.Join("|", Directory.GetFileSystemEntries(outside))
            );
            Check(
                rig.Env.LogLines.Any(l => l.Contains("export x.txt failed") && l.Contains("link"))
                    && rig.Env.LogLines.Any(l => l.Contains("data.json failed") && l.Contains("link")),
                "the refused writes are logged",
                string.Join("|", rig.Env.LogLines)
            );
            string original = Path.Combine(rig.Env.ModsDir, "_originals", "textures", "hoe.png");
            Check(
                Throws<IOException>(() => ModFiles.CheckWrite(rig.Env.ModsDir, original)),
                "the original-art export refuses a junction at _originals"
            );
        }
        finally
        {
            Unlink();
        }
    }

    // ---- the scan thread ----

    static void ScanThread()
    {
        using var rig = new Rig();
        try
        {
            rig.Mod("a", "x = 1");
            rig.Mod("locked", "x = 1");
            string outside = Path.Combine(rig.Env.Root, "outside");
            Directory.CreateDirectory(outside);
            bool linked = Junction(Path.Combine(rig.Env.ModsDir, "a", "files"), outside);
            string locked = Path.Combine(rig.Env.ModsDir, "locked");
            bool denied = Icacls("\"" + locked + "\" /deny *S-1-1-0:(OI)(CI)(RD)");
            try
            {
                var watch = new ChangeWatch(rig.Env.ModsDir, () => 0);
                watch.Loaded("a");
                watch.Loaded("locked");
                var reported = new Dictionary<string, string>();
                if (linked)
                {
                    File.WriteAllText(Path.Combine(outside, "big.lua"), new string('x', 5000));
                    watch.Scan(reported);
                    Check(!reported.ContainsKey("a"), "a change behind a junction does not reload the mod");
                }
                else
                    Console.WriteLine("skipped: junction part of ScanThread");
                if (!denied)
                    Console.WriteLine("skipped: unreadable folder part of ScanThread (icacls refused)");
                rig.Write("a", "init.lua", "x = 2 -- changed");
                watch.Scan(reported);
                Check(reported.ContainsKey("a"), "a real change is seen while another mod folder cannot be read");
                Check(!reported.ContainsKey("locked"), "an unreadable folder is not reported again every round");
                Unlink();
                Directory.Delete(Path.Combine(rig.Env.ModsDir, "a"), true);
                watch.Scan(reported);
                Check(reported["a"] == "", "a folder that vanished is a change, not an error");
            }
            finally
            {
                if (denied)
                    Icacls("\"" + locked + "\" /remove:d *S-1-1-0");
            }
        }
        finally
        {
            Unlink();
        }
    }

    // ---- folder names Windows reserves ----

    static void ReservedModIds()
    {
        using var rig = new Rig();
        var ids = new[] { "com1", "nul", "aux", "lpt9", "con", "abc.", "abc " };
        string Long(string id, string file = null) =>
            @"\\?\" + Path.Combine(Path.GetFullPath(rig.Env.ModsDir), id) + (file != null ? "\\" + file : "");
        try
        {
            foreach (var id in ids)
            {
                Directory.CreateDirectory(Long(id));
                File.WriteAllText(Long(id, "mod.xml"), "<Mod name=\"x\" />");
            }
            rig.Start();
            foreach (var id in ids)
            {
                var mod = rig.Host.Mods.FirstOrDefault(m => m.Id == id);
                Check(
                    mod != null && mod.Status == ModStatus.Skipped && mod.Reason.Length > 20,
                    "a mod folder named \"" + id + "\" is skipped with a reason",
                    mod?.Status + " " + mod?.Reason
                );
            }
            Check(
                rig.Host.Mods.Single(m => m.Id == "com1").Reason.Contains("reserves"),
                "the reason for com1 says Windows reserves the name"
            );
        }
        finally
        {
            foreach (var id in ids)
                if (Directory.Exists(Long(id)))
                    Directory.Delete(Long(id), true);
        }
    }

    // ---- size caps before reading ----

    static void SizeCaps()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod("biglua", "-- " + new string('x', 1100 * 1024));
        rig.Mod("lib", "local ok, e = pcall(dofile, 'files/big.lua') Out(ok, e)");
        rig.Write("lib", "files/big.lua", "-- " + new string('x', 1100 * 1024));
        rig.Start();
        Check(
            rig["biglua"].Status == ModStatus.Failed
                && (rig["biglua"].Reason + rig.Env.ChatRead).Contains("init.lua is 1.1 MB; it may be at most 1 MB"),
            "an init.lua over 1 MB is not run",
            rig["biglua"].Status + " " + rig["biglua"].Reason
        );
        Check(
            rig.Host.Area<SpyArea>().Out.Any(l => l.StartsWith("false") && l.Contains("at most 1 MB")),
            "dofile refuses a file over 1 MB",
            string.Join("|", rig.Host.Area<SpyArea>().Out)
        );
    }

    // ---- helpers ----

    static bool Junction(string link, string target) => Mklink("/J \"" + link + "\" \"" + target + "\"");

    // Links made by a test; removed by Unlink before the rig deletes its folder (a recursive delete of a folder with a
    // junction in it is refused without admin rights).
    static readonly List<string> links = new List<string>();

    static bool Mklink(string args)
    {
        string link = args.Split('"')[1];
        if (!Shell("cmd.exe", "/c mklink " + args) || (File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
            return false;
        links.Add(link);
        return true;
    }

    static void Unlink()
    {
        foreach (var link in links)
            if (Directory.Exists(link))
                Directory.Delete(link);
            else
                File.Delete(link);
        links.Clear();
    }

    static bool Icacls(string args) => Shell("icacls.exe", args);

    static bool Shell(string exe, string args)
    {
        try
        {
            var p = Process.Start(
                new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            );
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is IOException)
        {
            return false;
        }
    }

    static bool Throws<T>(Action a)
        where T : Exception
    {
        try
        {
            a();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    static byte[] Random(int n)
    {
        var b = new byte[n];
        new Random(n).NextBytes(b);
        return b;
    }

    // A real PNG of zero pixels: IHDR, PLTE for palette images, IDAT (zlib of the filtered rows, Adam7 when
    // interlaced), IEND.
    static List<(string Type, byte[] Data)> Chunks(int w, int h, int depth = 8, int type = 6, bool interlace = false)
    {
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, w);
        BigEndian(ihdr, 4, h);
        ihdr[8] = (byte)depth;
        ihdr[9] = (byte)type;
        ihdr[12] = (byte)(interlace ? 1 : 0);
        var chunks = new List<(string, byte[])> { ("IHDR", ihdr) };
        if (type == 3)
            chunks.Add(("PLTE", new byte[3 * 16]));
        int channels =
            type == 2 ? 3
            : type == 4 ? 2
            : type == 6 ? 4
            : 1;
        int bits = channels * depth;
        var raw = new MemoryStream();
        var passes = interlace
            ? new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) }
            : new[] { (0, 0, 1, 1) };
        foreach (var (x0, y0, dx, dy) in passes)
        {
            int pw = (w - x0 + dx - 1) / dx,
                ph = (h - y0 + dy - 1) / dy;
            if (pw > 0 && ph > 0)
                raw.Write(new byte[ph * (1 + (pw * bits + 7) / 8)]);
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true))
            zs.Write(raw.ToArray());
        chunks.Add(("IDAT", z.ToArray()));
        chunks.Add(("IEND", new byte[0]));
        return chunks;
    }

    static byte[] Png(List<(string Type, byte[] Data)> chunks)
    {
        var o = new MemoryStream();
        o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        foreach (var (type, data) in chunks)
        {
            var head = new byte[8];
            BigEndian(head, 0, data.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(head, 4);
            o.Write(head);
            o.Write(data);
            var crc = new byte[4];
            BigEndian(crc, 0, (int)Crc32(head.Skip(4).Concat(data)));
            o.Write(crc);
        }
        return o.ToArray();
    }

    static void BigEndian(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    static uint Crc32(IEnumerable<byte> bytes)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in bytes)
        {
            c ^= x;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }

    // The packets of an Ogg file, read with no checks.
    static List<byte[]> OggPackets(byte[] b)
    {
        var packets = new List<byte[]>();
        var cur = new List<byte>();
        for (int at = 0; at < b.Length; )
        {
            int segs = b[at + 26],
                body = at + 27 + segs;
            for (int i = 0; i < segs; i++)
            {
                int len = b[at + 27 + i];
                cur.AddRange(b.Skip(body).Take(len));
                body += len;
                if (len < 255)
                {
                    packets.Add(cur.ToArray());
                    cur.Clear();
                }
            }
            at = body;
        }
        return packets;
    }

    // One Ogg stream: the first packet alone on the first page, the next two on the second, then 50 packets a page;
    // the last page carries the granule position given and the end-of-stream flag.
    static byte[] Ogg(List<byte[]> packets, long lastGranule, uint serial = 1)
    {
        var pages = new List<List<byte[]>> { packets.Take(1).ToList(), packets.Skip(1).Take(2).ToList() };
        for (int i = 3; i < packets.Count; i += 50)
            pages.Add(packets.Skip(i).Take(50).ToList());
        var o = new MemoryStream();
        for (int p = 0; p < pages.Count; p++)
        {
            var lacing = new List<byte>();
            var body = new MemoryStream();
            foreach (var packet in pages[p])
            {
                for (int n = packet.Length; ; n -= 255)
                {
                    lacing.Add((byte)Math.Min(n, 255));
                    if (n < 255)
                        break;
                }
                body.Write(packet);
            }
            var page = new MemoryStream();
            page.Write(Encoding.ASCII.GetBytes("OggS"));
            page.WriteByte(0);
            bool last = p == pages.Count - 1;
            page.WriteByte((byte)((p == 0 ? 2 : 0) | (last ? 4 : 0)));
            page.Write(BitConverter.GetBytes(last ? lastGranule : 0L));
            page.Write(BitConverter.GetBytes(serial));
            page.Write(BitConverter.GetBytes(p));
            page.Write(new byte[4]);
            page.WriteByte((byte)lacing.Count);
            page.Write(lacing.ToArray());
            page.Write(body.ToArray());
            var bytes = page.ToArray();
            BitConverter.GetBytes(OggCrc32(bytes)).CopyTo(bytes, 22);
            o.Write(bytes);
        }
        return o.ToArray();
    }

    static uint OggCrc32(byte[] b)
    {
        uint c = 0;
        foreach (byte x in b)
        {
            c ^= (uint)x << 24;
            for (int k = 0; k < 8; k++)
                c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : c << 1;
        }
        return c;
    }
}
