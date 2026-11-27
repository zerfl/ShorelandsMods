using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShorelandsMods.Core.Areas
{
    // One mod's look and sound for a cue slot.
    internal sealed class CueDef
    {
        public string ModId,
            Slot,
            ImageFull,
            ImageRel,
            SoundRel;
        public int ImageW,
            ImageH,
            FrameW;

        // -1: no sound.
        public int Clip = -1;
        public bool HideNative;
        public CueOptions Options;
    }

    // What the game does with a cue (Game/Assets/CueStage.cs). It plays cues by itself from the game's fishing
    // effects; nothing reaches the mods.
    internal interface ICueStage
    {
        // The cue now used for a slot, or null for none. Called in Update, once per changed slot.
        void Set(string slot, CueDef cue);

        // Shows a cue's image at the player as the game moment would, cycles times with its sound each time after the
        // first (author preview, CueRules.PreviewAt); false outside the world.
        bool Preview(CueDef cue, int cycles);
    }

    // cues/: presentation slots for the fishing moments (never Lua events, so no mod can react to them). This area
    // only decides which mod's files a slot uses; the stage draws and plays them without telling anyone.
    internal sealed class CueArea : Area
    {
        sealed class State
        {
            public readonly List<AssetLine> Ignored = new List<AssetLine>();
        }

        readonly ICueStage stage;
        Overrides<CueDef> slots;

        public CueArea(ICueStage stage)
        {
            this.stage = stage;
        }

        public override string Name => "Cues";

        protected internal override void Register(AreaSetup s) { }

        protected internal override void HostStarted() => slots = new Overrides<CueDef>(Host.OrderOf);

        protected internal override void ModStarted(Mod mod)
        {
            var scan = AssetScan.Of(mod);
            var st = mod.Get<State>();
            var opts = new Dictionary<string, CueOptions>();
            if (scan.CuesXml != null)
            {
                var problems = new List<string>();
                opts = AssetXml.Cues(AssetXml.Read(scan.CuesXml, problems), problems);
                foreach (var p in problems)
                    st.Ignored.Add(new AssetLine("cues/cues.xml", AssetState.Ignored, p));
            }
            var sound = Host.Area<SoundArea>();
            foreach (var slot in AssetNames.CueSlots)
            {
                var image = scan.Files.FirstOrDefault(f => f.Kind == AssetKind.CueImage && f.Key == slot);
                var audio = scan.Files.FirstOrDefault(f => f.Kind == AssetKind.CueSound && f.Key == slot);
                bool hasOpts = opts.TryGetValue(slot, out var o);
                if (image == null && audio == null)
                {
                    if (hasOpts)
                        st.Ignored.Add(
                            new AssetLine(
                                "cues/cues.xml",
                                AssetState.Ignored,
                                "an entry for " + slot + ", but the mod has no cues/" + slot + ".png, .ogg or .wav"
                            )
                        );
                    continue;
                }
                var def = new CueDef
                {
                    ModId = mod.Id,
                    Slot = slot,
                    Options = o ?? new CueOptions(),
                };
                if (image != null)
                {
                    string why = null;
                    if (FileHeaders.Png(image.Full, out int w, out int h, out why))
                        why = CueRules.Problem(w, h, def.Options.Frames, def.Options.Scale, out def.FrameW);
                    if (why != null)
                        st.Ignored.Add(new AssetLine(image.Rel, AssetState.Ignored, why));
                    else
                    {
                        def.ImageFull = image.Full;
                        def.ImageRel = image.Rel;
                        def.ImageW = w;
                        def.ImageH = h;
                    }
                }
                if (audio != null)
                {
                    def.Clip = sound.OpenClip(mod, audio.Full, Limits.SoundSecondsCue, out string why);
                    if (def.Clip < 0)
                        st.Ignored.Add(new AssetLine(audio.Rel, AssetState.Ignored, why));
                    else
                        def.SoundRel = audio.Rel;
                }
                if (def.ImageRel == null && def.SoundRel == null)
                    continue;
                if (def.Options.HideNative == true && !CueRules.HasNativeMark(slot))
                    st.Ignored.Add(
                        new AssetLine(
                            "cues/cues.xml",
                            AssetState.Ignored,
                            "hide_native for " + slot + " does nothing: only fishing_bite has a mark the game draws"
                        )
                    );
                def.HideNative =
                    CueRules.HasNativeMark(slot)
                    && (def.Options.HideNative ?? def.ImageRel != null)
                    && def.ImageRel != null;
                slots.Add(slot, mod.Id, def);
            }
        }

        protected internal override void ModStopping(Mod mod) => slots.RemoveMod(mod.Id);

        // ---- previews (author tools; never reachable from Lua) ----

        public IEnumerable<string> PreviewNames(Mod mod) => slots.KeysOf(mod.Id);

        public CueDef Of(Mod mod, string slot) => slots.ValueOf(slot, mod.Id);

        // The mod's own cue for a slot: its sound, and its image at the player when inWorld. Null when it plays,
        // else why not.
        public string Preview(Mod mod, string slot, bool inWorld)
        {
            var def = slots.ValueOf(slot, mod.Id);
            if (def == null)
                return "it has no cue for " + slot;
            string why =
                def.Clip >= 0 ? Host.Area<SoundArea>().PlayPreview(def.Clip, def.Options.Volume, def.SoundRel) : null;
            if (why != null)
                return why;
            if (inWorld && def.ImageRel != null && !stage.Preview(def, PreviewCycles(def)))
                return "the image shows at your character once you are in the world";
            return null;
        }

        public static double PreviewSeconds(CueDef def) => CueRules.PreviewSeconds(def.Slot, def.Options.Seconds);

        public static int PreviewCycles(CueDef def) => CueRules.PreviewCycles(PreviewSeconds(def));

        protected internal override void Update(double now)
        {
            if (slots.Dirty.Count == 0)
                return;
            var sound = Host.Area<SoundArea>();
            var retry = new List<string>();
            foreach (var slot in slots.TakeDirty())
            {
                var def = slots.WinnerValue(slot);
                // Wait for the sound to load, so the first cue is not silent.
                if (
                    def != null
                    && def.Clip >= 0
                    && sound.Audio.Status(def.Clip, out string problem) != ClipStatus.Ready
                )
                {
                    if (sound.Audio.Status(def.Clip, out problem) == ClipStatus.Loading)
                    {
                        retry.Add(slot);
                        continue;
                    }
                    var mod = Host.Find(def.ModId);
                    mod?.Get<State>()
                        .Ignored.Add(new AssetLine(def.SoundRel, AssetState.Ignored, "it does not play: " + problem));
                    if (mod != null)
                        Host.Notice(mod, def.SoundRel + " does not play: " + problem + ".");
                    def.Clip = -1;
                    def.SoundRel = null;
                    if (def.ImageRel == null)
                    {
                        slots.Remove(slot, def.ModId);
                        continue;
                    }
                }
                stage.Set(slot, def);
            }
            foreach (var slot in retry)
                slots.Retry(slot);
        }

        protected internal override void Shutdown()
        {
            foreach (var slot in slots.TakeDirty())
                stage.Set(slot, slots.WinnerValue(slot));
        }

        protected internal override IEnumerable<AssetLine> AssetLines(Mod mod)
        {
            var lines = new List<AssetLine>();
            foreach (var slot in slots.KeysOf(mod.Id))
            {
                var mine = slots.ValueOf(slot, mod.Id);
                var win = slots.Winner(slot);
                foreach (var rel in new[] { mine.ImageRel, mine.SoundRel })
                    if (rel != null)
                        lines.Add(
                            win.ModId == mod.Id
                                ? new AssetLine(rel, AssetState.Applied)
                                : new AssetLine(rel, AssetState.Overridden, win.ModId)
                        );
            }
            if (mod.Find<State>() is State st)
                lines.AddRange(st.Ignored);
            return lines;
        }
    }
}
