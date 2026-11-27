using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    internal enum ClipStatus
    {
        Loading,
        Ready,
        Failed,
    }

    // What the game does with sound (Game/Assets/AudioOut.cs; a fake offline). Volumes are 0..1 before the game's
    // master volume, which applies to everything.
    internal interface IAudioOut
    {
        int Load(string fullPath);
        ClipStatus Status(int clip, out string problem);
        void Unload(int clip);

        // A one-shot in a voice group of at most `voices`; when all are busy the one that started first stops.
        void Play(int clip, double volume, string group, int voices);

        int StartLoop(int clip, double volume, double fadeSeconds);
        void StopLoop(int loop, double fadeSeconds);

        // Stops the one-shots of a voice group at once; whether one of them still plays.
        void Stop(string group);
        bool IsPlaying(string group);

        // Stops a voice group and removes its voices (a mod's group, when the mod stops).
        void Drop(string group);

        // The game's master volume (0..1), which every sound follows.
        double MasterVolume { get; }
    }

    // sounds/ (events by name, loops for rain and zone music) and the Lua functions SoundPlay, SoundLoop, SoundStop.
    internal sealed class SoundArea : Area
    {
        public const double Fade = 1.5;

        // The voice group of author previews (Mods page, /preview): one voice, a new preview stops the last.
        public const string PreviewGroup = "preview";

        sealed class PackSound
        {
            public string ModId,
                Key,
                Rel;
            public int Clip;
            public SoundOptions Options;
        }

        sealed class Clip
        {
            public int Handle;
            public long Bytes;
            public double Seconds;
            public bool Told;
        }

        sealed class Pending
        {
            public Clip Clip;
            public double Volume,
                Asked;
            public string LoopId;
        }

        sealed class State
        {
            public readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>(
                StringComparer.OrdinalIgnoreCase
            );
            public long Bytes;
            public readonly List<AssetLine> Ignored = new List<AssetLine>();
            public readonly List<Pending> Pending = new List<Pending>();
            public readonly Dictionary<string, int> Loops = new Dictionary<string, int>();
            public readonly Dictionary<string, Checked> Checked = new Dictionary<string, Checked>(
                StringComparer.OrdinalIgnoreCase
            );
            public long CheckedBytes;
        }

        struct Checked
        {
            public bool Ok;
            public double Seconds;
            public long Bytes;
            public string Problem;
        }

        // A loop the host plays for the pack (music, rain): which sound, which mod's file, which voice.
        sealed class PackLoop
        {
            public string Name;
            public PackSound Sound;
            public int Voice;
        }

        public readonly IAudioOut Audio;
        Overrides<PackSound> pack;
        readonly Dictionary<string, double> lastPlayed = new Dictionary<string, double>();
        readonly List<(int Clip, double At)> retired = new List<(int, double)>();
        PackLoop music,
            rain;
        string zone;
        bool night,
            raining;

        public SoundArea(IAudioOut audio)
        {
            Audio = audio;
        }

        public override string Name => "Sound";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "SoundPlay( file:string, [volume:number] ) -> ok:boolean",
                "plays a sound file of this mod once: .ogg (Vorbis) or .wav inside the mod folder (\"files/x.ogg\"), "
                    + "at most "
                    + Limits.SoundSecondsOnce
                    + " s; volume 0 to 1 (default 1), times the game's master volume. The first play of a file may start a "
                    + "few frames late (one still loading after 1 s is dropped); "
                    + Limits.SoundVoicesPerMod
                    + " voices per mod, a new one stops the oldest. false (nothing played) for a file that is missing, "
                    + "outside the mod, too long or not playable; false when volume was clamped",
                Play
            );
            s.Function(
                "SoundLoop( id:string, file:string, [volume:number] ) -> ok:boolean",
                "plays a sound file of this mod over and over until SoundStop( id ) or the mod stops; the same id "
                    + "again replaces it. Files as SoundPlay, at most "
                    + Limits.SoundSecondsLoop
                    + " s. false (nothing played) as SoundPlay, for an id over 40 characters, or a loop beyond the "
                    + Limits.SoundLoopsPerMod
                    + " a mod may play",
                Loop
            );
            s.Function(
                "SoundStop( id:string )",
                "stops the loop started with that id (a short fade); an unknown id is ignored",
                Stop
            );
        }

        protected internal override void HostStarted() => pack = new Overrides<PackSound>(Host.OrderOf);

        // ---- the pack: sounds/ ----

        protected internal override void ModStarted(Mod mod)
        {
            var scan = AssetScan.Of(mod);
            var st = mod.Get<State>();
            var opts = new Dictionary<string, SoundOptions>();
            if (scan.SoundsXml != null)
            {
                var problems = new List<string>();
                opts = AssetXml.Sounds(AssetXml.Read(scan.SoundsXml, problems), problems);
                foreach (var p in problems)
                    st.Ignored.Add(new AssetLine("sounds/sounds.xml", AssetState.Ignored, p));
            }
            var files = scan.OfKind(AssetKind.Sound).ToList();
            foreach (var name in opts.Keys)
                if (!files.Any(f => f.Key == name))
                    st.Ignored.Add(
                        new AssetLine(
                            "sounds/sounds.xml",
                            AssetState.Ignored,
                            "an entry for " + name + ", but the mod has no sounds/" + name + ".ogg or .wav"
                        )
                    );
            foreach (var f in files)
            {
                var clip = Open(
                    mod,
                    f.Full,
                    AssetNames.IsLoop(f.Key) ? Limits.SoundSecondsLoop : Limits.SoundSecondsOnce,
                    out string why
                );
                if (clip == null)
                {
                    st.Ignored.Add(new AssetLine(f.Rel, AssetState.Ignored, why));
                    continue;
                }
                pack.Add(
                    f.Key,
                    mod.Id,
                    new PackSound
                    {
                        ModId = mod.Id,
                        Key = f.Key,
                        Rel = f.Rel,
                        Clip = clip.Handle,
                        Options = opts.TryGetValue(f.Key, out var o) ? o : new SoundOptions(),
                    }
                );
            }
        }

        // A sound of the game happened ("wallet_gain", "ui_open", ...): the winning pack sound plays, if any.
        public void Event(string name)
        {
            if (pack == null)
                return;
            var s = pack.WinnerValue(name);
            if (s == null || Audio.Status(s.Clip, out _) != ClipStatus.Ready)
                return;
            double now = Host.Env.Now;
            if (lastPlayed.TryGetValue(name, out double at) && now - at < s.Options.Cooldown)
                return;
            lastPlayed[name] = now;
            Audio.Play(s.Clip, s.Options.Volume, "pack", Limits.SoundVoicesPack);
        }

        // Where the player is, for the music; null outside the world.
        public void SetZone(string z) => zone = z;

        public void SetNight(bool n) => night = n;

        public void SetRain(bool r) => raining = r;

        protected internal override void Update(double now)
        {
            if (pack.Dirty.Count > 0)
            {
                foreach (var key in pack.TakeDirty())
                    if (
                        pack.WinnerValue(key) is PackSound s
                        && Audio.Status(s.Clip, out string problem) == ClipStatus.Failed
                    )
                        RejectPack(key, s, problem);
            }
            foreach (var mod in Host.Mods)
                if (mod.Find<State>() is State st && st.Pending.Count > 0)
                    RunPending(mod, st, now);
            string m = AssetNames.Music(zone, night, n => Ready(pack.WinnerValue(n)));
            music = Follow(music, m);
            rain = Follow(rain, raining && zone != null && Ready(pack.WinnerValue("rain_loop")) ? "rain_loop" : null);
            for (int i = retired.Count - 1; i >= 0; i--)
                if (now >= retired[i].At)
                {
                    Audio.Unload(retired[i].Clip);
                    retired.RemoveAt(i);
                }
        }

        bool Ready(PackSound s)
        {
            if (s == null)
                return false;
            var status = Audio.Status(s.Clip, out string problem);
            if (status == ClipStatus.Failed)
                RejectPack(s.Key, s, problem);
            return status == ClipStatus.Ready;
        }

        void RejectPack(string key, PackSound s, string problem)
        {
            var mod = Host.Find(s.ModId);
            pack.Remove(key, s.ModId);
            if (mod?.Find<State>() is State st)
            {
                st.Ignored.Add(new AssetLine(s.Rel, AssetState.Ignored, "it does not play: " + problem));
                Host.Notice(mod, s.Rel + " does not play: " + problem + ".");
            }
        }

        // Keeps a pack loop on the wanted sound (and that sound's winning file): crossfades on a change.
        PackLoop Follow(PackLoop current, string want)
        {
            var sound = want != null ? pack.WinnerValue(want) : null;
            if (!Ready(sound))
                sound = null;
            if (current != null && current.Name == want && current.Sound == sound)
                return current;
            if (current != null)
                Audio.StopLoop(current.Voice, Fade);
            if (sound == null)
                return null;
            return new PackLoop
            {
                Name = want,
                Sound = sound,
                Voice = Audio.StartLoop(sound.Clip, sound.Options.Volume, Fade),
            };
        }

        protected internal override void ModStopping(Mod mod)
        {
            pack.RemoveMod(mod.Id);
            Audio.Drop(GroupOf(mod));
            // A loop playing this mod's file fades out (or over to the next mod's); its clips stay loaded until then.
            music = Follow(music, music?.Name);
            rain = Follow(rain, rain?.Name);
            var st = mod.Find<State>();
            if (st == null)
                return;
            foreach (var c in st.Clips.Values)
                Retire(c.Handle);
        }

        static string GroupOf(Mod mod) => "mod:" + mod.Id;

        public void Retire(int clip) => retired.Add((clip, Host.Env.Now + Fade + 0.1));

        protected internal override void Shutdown()
        {
            if (music != null)
                Audio.StopLoop(music.Voice, 0);
            if (rain != null)
                Audio.StopLoop(rain.Voice, 0);
            music = rain = null;
            foreach (var r in retired)
                Audio.Unload(r.Clip);
            retired.Clear();
        }

        protected internal override IEnumerable<AssetLine> AssetLines(Mod mod)
        {
            var lines = new List<AssetLine>();
            foreach (var key in pack.KeysOf(mod.Id))
            {
                var mine = pack.ValueOf(key, mod.Id);
                var win = pack.Winner(key);
                if (win.ModId != mod.Id)
                    lines.Add(new AssetLine(mine.Rel, AssetState.Overridden, win.ModId));
                else
                    lines.Add(
                        Audio.Status(mine.Clip, out _) == ClipStatus.Loading
                            ? new AssetLine(mine.Rel, AssetState.Waiting, "still loading")
                            : new AssetLine(mine.Rel, AssetState.Applied)
                    );
            }
            if (mod.Find<State>() is State st)
                lines.AddRange(st.Ignored);
            return lines;
        }

        // ---- previews (author tools; never reachable from Lua) ----

        public IEnumerable<string> PreviewNames(Mod mod) => pack.KeysOf(mod.Id);

        // Plays the mod's own file for a sound name once, even where another mod's file wins or no event is due; a
        // loop plays once too. Null when it plays, else why not.
        public string Preview(Mod mod, string name)
        {
            var s = pack.ValueOf(name, mod.Id);
            if (s == null)
                return "it has no sounds/" + name + ".ogg or .wav";
            return PlayPreview(s.Clip, s.Options.Volume, s.Rel);
        }

        public string PlayPreview(int clip, double volume, string rel)
        {
            switch (Audio.Status(clip, out string problem))
            {
                case ClipStatus.Loading:
                    return rel + " is still loading; try again in a moment";
                case ClipStatus.Failed:
                    return rel + " does not play: " + problem;
            }
            Audio.Play(clip, volume, PreviewGroup, 1);
            PreviewSeconds = clipSeconds.TryGetValue(clip, out double s) ? s : 0;
            return null;
        }

        // Every sound follows the game's master volume; at 0 a preview is silent.
        public bool Muted => Audio.MasterVolume < 0.005;

        public const string MutedSentence =
            "The game's volume is at 0, so you hear nothing: turn it up under Settings, Audio.";

        // The length of the last preview's file, from its header.
        public double PreviewSeconds { get; private set; }

        public void StopPreview() => Audio.Stop(PreviewGroup);

        public bool PreviewPlaying => Audio.IsPlaying(PreviewGroup);

        // A clip of the mod (sounds/, cues/, SoundPlay), loaded once per mod start and unloaded when it stops;
        // -1 with a sentence if the file cannot be used. Counts against the mod's sound limits.
        public int OpenClip(Mod mod, string full, double maxSeconds, out string why) =>
            Open(mod, full, maxSeconds, out why)?.Handle ?? -1;

        readonly Dictionary<int, double> clipSeconds = new Dictionary<int, double>();

        Clip Open(Mod mod, string full, double maxSeconds, out string why)
        {
            var st = mod.Get<State>();
            if (st.Clips.TryGetValue(full, out var c))
                return Fits(c, maxSeconds, out why) ? c : null;
            // Read once per mod start, refused or not; all files read for a mod together at most twice what it may load.
            if (!st.Checked.TryGetValue(full, out var file))
            {
                var info = new FileInfo(full);
                long length = info.Exists ? info.Length : 0;
                if (length <= Limits.SoundFileBytes && st.CheckedBytes + length > 2 * Limits.SoundBytesPerMod)
                {
                    why = "the host already read " + Mb(2 * Limits.SoundBytesPerMod) + " of the mod's sound files";
                    return null;
                }
                if (length <= Limits.SoundFileBytes)
                    st.CheckedBytes += length;
                file.Ok = FileHeaders.Audio(full, out file.Seconds, out file.Problem);
                file.Bytes = length;
                st.Checked[full] = file;
            }
            why = file.Problem;
            if (!file.Ok)
                return null;
            if (st.Clips.Count >= Limits.SoundClipsPerMod)
            {
                why = "the mod already has " + Limits.SoundClipsPerMod + " sound files loaded";
                return null;
            }
            if (st.Bytes + file.Bytes > Limits.SoundBytesPerMod)
            {
                why = "the mod's sounds would be over " + Mb(Limits.SoundBytesPerMod) + " together";
                return null;
            }
            c = new Clip { Seconds = file.Seconds, Bytes = file.Bytes };
            if (!Fits(c, maxSeconds, out why))
                return null;
            c.Handle = Audio.Load(full);
            clipSeconds[c.Handle] = file.Seconds;
            st.Clips[full] = c;
            st.Bytes += file.Bytes;
            return c;
        }

        static bool Fits(Clip c, double max, out string why)
        {
            why =
                c.Seconds > max
                    ? "it is " + LuaText.Num(Math.Round(c.Seconds, 1)) + " s long; it may be at most " + max + " s"
                    : null;
            return why == null;
        }

        static string Mb(long bytes) => LuaText.Num(Math.Round(bytes / 1048576.0, 1)) + " MB";

        // ---- Lua ----

        // Null (refused) for a file that cannot be played.
        Clip LuaClip(ApiCall c, int arg, double maxSeconds)
        {
            string path = c.String(arg, "file");
            string full = ModPaths.ResolveFile(
                c.Mod.Dir,
                c.Mod.Id,
                path,
                ModPaths.SoundExtensions,
                out var rel,
                out var error
            );
            if (full == null)
                c.Refuse(arg, error);
            else if (!File.Exists(full))
                c.Refuse(arg, "there is no file " + rel + " in the mod folder");
            else if (Open(c.Mod, full, maxSeconds, out string why) is Clip clip)
                return clip;
            else
                c.Refuse(arg, rel + " cannot be played: " + why);
            return null;
        }

        int Play(ApiCall c)
        {
            var clip = LuaClip(c, 1, Limits.SoundSecondsOnce);
            double volume = c.OptClamp(2, "volume", 1, 0, 1);
            if (clip == null)
                return c.ReturnOk();
            c.Mod.Get<State>()
                .Pending.Add(
                    new Pending
                    {
                        Clip = clip,
                        Volume = volume,
                        Asked = Host.Env.Now,
                    }
                );
            RunPending(c.Mod, c.Mod.Get<State>(), Host.Env.Now);
            return c.ReturnOk();
        }

        int Loop(ApiCall c)
        {
            string id = c.String(1, "id");
            if (id.Length == 0 || id.Length > 40)
            {
                c.Refuse(
                    1,
                    "a loop id must be 1 to 40 characters, so the loop \""
                        + TextEscape.Clip(id, 40)
                        + "\" was not started"
                );
                return c.ReturnOk();
            }
            var clip = LuaClip(c, 2, Limits.SoundSecondsLoop);
            double volume = c.OptClamp(3, "volume", 1, 0, 1);
            if (clip == null)
                return c.ReturnOk();
            var mod = c.Mod;
            var st = mod.Get<State>();
            if (
                !st.Loops.ContainsKey(id)
                && st.Pending.All(p => p.LoopId != id)
                && LoopCount(st) >= Limits.SoundLoopsPerMod
            )
            {
                c.Refuse(
                    0,
                    "the mod already plays "
                        + Limits.SoundLoopsPerMod
                        + " loops, so \""
                        + TextEscape.Clip(id, 40)
                        + "\" was not started"
                );
                return c.ReturnOk();
            }
            var pending = new Pending
            {
                Clip = clip,
                Volume = volume,
                Asked = Host.Env.Now,
                LoopId = id,
            };
            mod.Resources.Add("sound_loop", id, () =>
            {
                st.Pending.Remove(pending);
                if (st.Loops.TryGetValue(id, out int voice))
                {
                    st.Loops.Remove(id);
                    Audio.StopLoop(voice, 0.1);
                }
            });
            st.Pending.Add(pending);
            RunPending(mod, st, Host.Env.Now);
            return c.ReturnOk();
        }

        static int LoopCount(State st) => st.Loops.Count + st.Pending.Count(p => p.LoopId != null);

        int Stop(ApiCall c)
        {
            c.Mod.Resources.Remove("sound_loop", c.String(1, "id"));
            return c.Return();
        }

        // Plays what waited for its clip to load; a one-shot that waited too long is dropped.
        void RunPending(Mod mod, State st, double now)
        {
            for (int i = 0; i < st.Pending.Count; i++)
            {
                var p = st.Pending[i];
                var status = Audio.Status(p.Clip.Handle, out string problem);
                if (status == ClipStatus.Loading && (p.LoopId != null || now - p.Asked < Limits.SoundLateSeconds))
                    continue;
                st.Pending.RemoveAt(i--);
                if (status == ClipStatus.Failed)
                {
                    if (!p.Clip.Told)
                        Host.Notice(mod, "a sound file does not play: " + problem + ".");
                    p.Clip.Told = true;
                    if (p.LoopId != null)
                        mod.Resources.Forget("sound_loop", p.LoopId);
                }
                else if (status == ClipStatus.Ready)
                {
                    if (p.LoopId == null)
                        Audio.Play(p.Clip.Handle, p.Volume, GroupOf(mod), Limits.SoundVoicesPerMod);
                    else
                        st.Loops[p.LoopId] = Audio.StartLoop(p.Clip.Handle, p.Volume, 0.1);
                }
            }
        }
    }
}
