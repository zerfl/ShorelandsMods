using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ReworkedCopium.Fishing;
using ReworkedCopium.Player;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShorelandsMods.Game.Assets
{
    // Draws and plays cue slots from the game's own fishing effects. Fairness: this class never calls into the host, a
    // mod or the log while a cue plays; what it does is visible only on screen and in the speakers. Plays counts cues.
    internal sealed class CueStage : Area, ICueStage
    {
        const float Ppu = 32f;

        static CueStage instance;
        readonly Harmony harmony;
        readonly AudioOut audio;
        readonly Transform root;
        GameHook fx;

        sealed class Look
        {
            public CueDef Def;
            public Texture2D Strip;
            public Sprite[] Frames = new Sprite[0];
            public bool Temporary;
        }

        sealed class Showing
        {
            public Look Look;
            public FishingLineFX Fx;
            public float Start;
            public SpriteRenderer Renderer;

            // A preview's own time per showing and how many (the game's state does not end it), CueRules.PreviewAt.
            public double? Seconds;
            public int Cycles,
                Cycle;
        }

        readonly Dictionary<string, Look> looks = new Dictionary<string, Look>();
        readonly Dictionary<string, Showing> showing = new Dictionary<string, Showing>();
        float lastMarkStart = float.NaN;

        public static int Plays;
        public static double LastDrawMs;

        public CueStage(Harmony harmony, AudioOut audio, Transform root)
        {
            this.harmony = harmony;
            this.audio = audio;
            this.root = root;
            instance = this;
        }

        public override string Name => "Cues (game)";

        protected internal override void Register(AreaSetup s)
        {
            fx = s.Hook(
                "FishingLineFX.UpdateMark, OnFishingEvent, mark/markStart/body/bobber/state/model",
                "cues (fishing)",
                Patch
            );
        }

        public void Set(string slot, CueDef def)
        {
            End(slot);
            if (looks.TryGetValue(slot, out var old))
            {
                Free(old);
                looks.Remove(slot);
            }
            if (def == null)
                return;
            var look = new Look { Def = def };
            if (def.ImageFull != null && !LoadStrip(look) && Host.Find(def.ModId) is Mod mod)
                Host.Notice(mod, def.ImageRel + " is not a readable PNG; the cue has no image.");
            looks[slot] = look;
        }

        // A cue strip's pixels, decoded once per CueDef (a mod start) for the stage and the Mods page; null if refused.
        static readonly ConditionalWeakTable<CueDef, Color32[]> strips = new ConditionalWeakTable<CueDef, Color32[]>();

        internal static Color32[] StripPixels(CueDef d)
        {
            if (!strips.TryGetValue(d, out var px))
            {
                px = ArtStage.Decode(d.ImageFull, d.ImageW, d.ImageH, out _) ?? new Color32[0];
                strips.Add(d, px);
            }
            return px.Length > 0 ? px : null;
        }

        static bool LoadStrip(Look look)
        {
            var d = look.Def;
            var px = StripPixels(d);
            if (px == null)
                return false;
            look.Strip = new Texture2D(d.ImageW, d.ImageH, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
            {
                name = "mods-cue:" + d.Slot,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            look.Strip.SetPixels32(px);
            look.Strip.Apply(false, true);
            int n = d.Options.Frames;
            look.Frames = new Sprite[n];
            for (int i = 0; i < n; i++)
                look.Frames[i] = Sprite.Create(
                    look.Strip,
                    new Rect(i * d.FrameW, 0, d.FrameW, d.ImageH),
                    new Vector2(0.5f, 0f),
                    Ppu,
                    0,
                    SpriteMeshType.FullRect
                );
            return true;
        }

        // The author's preview: the slot's image at the local player, cycles times for the cue's time (the bite as
        // long as a short bite); the caller plays the first sound, this the others.
        public bool Preview(CueDef def, int cycles)
        {
            var f = PlayerModel.LocalPlayer != null ? PlayerModel.LocalPlayer.GetComponent<FishingLineFX>() : null;
            if (f == null || def.ImageFull == null)
                return false;
            // The effect makes its renderers (the "!" whose layer a cue takes) on the first fishing of a session.
            G.Build?.Invoke(f, null);
            if (!looks.TryGetValue(def.Slot, out var look) || look.Def != def)
            {
                // Another mod's cue wins this slot: the previewed one gets a look of its own until it ends.
                look = new Look { Def = def, Temporary = true };
                if (!LoadStrip(look))
                    return false;
            }
            Begin(def.Slot, f, look, false);
            showing[def.Slot].Seconds = CueArea.PreviewSeconds(def);
            showing[def.Slot].Cycles = cycles;
            return true;
        }

        void Begin(string slot, FishingLineFX f)
        {
            if (looks.TryGetValue(slot, out var look))
                Begin(slot, f, look, true);
        }

        void Begin(string slot, FishingLineFX f, Look look, bool sound)
        {
            End(slot);
            Plays++;
            var s = new Showing
            {
                Look = look,
                Fx = f,
                Start = Time.time,
            };
            showing[slot] = s;
            if (sound && look.Def.Clip >= 0)
                audio.Play(look.Def.Clip, look.Def.Options.Volume, "cue", 2);
            if (look.Frames.Length > 0)
            {
                var o = new GameObject("ModCue " + slot);
                o.transform.SetParent(root, false);
                s.Renderer = o.AddComponent<SpriteRenderer>();
                s.Renderer.enabled = false;
            }
        }

        void End(string slot)
        {
            if (!showing.TryGetValue(slot, out var s))
                return;
            showing.Remove(slot);
            if (s.Renderer != null)
                UnityEngine.Object.Destroy(s.Renderer.gameObject);
            if (s.Look.Temporary)
                Free(s.Look);
        }

        static void Free(Look look)
        {
            foreach (var f in look.Frames)
                UnityEngine.Object.Destroy(f);
            UnityEngine.Object.Destroy(look.Strip);
        }

        // Every frame (the plugin's LateUpdate and the mark postfix): positions, frames, ends.
        public void Draw()
        {
            if (showing.Count == 0)
                return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            List<string> over = null;
            foreach (var kv in showing)
            {
                var s = kv.Value;
                var o = s.Look.Def.Options;
                float t = Time.time - s.Start;
                bool drawn = true;
                double at = t;
                int cycle = s.Seconds.HasValue
                    ? CueRules.PreviewAt(t, s.Seconds.Value, s.Cycles, out drawn, out at)
                    : 0;
                bool done =
                    s.Fx == null
                    || (
                        s.Seconds.HasValue ? cycle < 0
                        : o.Seconds.HasValue ? t >= o.Seconds.Value
                        : CueRules.HasNativeMark(kv.Key) ? G.State(s.Fx) != PLAYERSTATE.ACTION_FISHING_BITE
                        : t >= CueRules.DefaultSeconds(kv.Key)
                    );
                if (!done && cycle > s.Cycle)
                {
                    s.Cycle = cycle;
                    if (s.Look.Def.Clip >= 0)
                        audio.Play(s.Look.Def.Clip, o.Volume, SoundArea.PreviewGroup, 1);
                }
                if (done)
                {
                    (over = over ?? new List<string>()).Add(kv.Key);
                    continue;
                }
                if (s.Renderer == null)
                    continue;
                var mark = G.Mark(s.Fx);
                if (mark != null)
                {
                    s.Renderer.sortingLayerID = mark.sortingLayerID;
                    s.Renderer.sortingOrder = mark.sortingOrder;
                    s.Renderer.sharedMaterial = mark.sharedMaterial;
                }
                var frames = s.Look.Frames;
                s.Renderer.sprite = frames[Mathf.FloorToInt((float)(at * o.Fps)) % frames.Length];
                s.Renderer.transform.position =
                    Anchor(s.Fx, o.Anchor, kv.Key) + new Vector2(o.OffsetX, o.OffsetY) / Ppu;
                s.Renderer.transform.localScale = new Vector3(o.Scale, o.Scale, 1);
                s.Renderer.enabled = drawn;
            }
            if (over != null)
                foreach (var slot in over)
                    End(slot);
            LastDrawMs =
                (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        // mark: where the game draws its "!" (with its hop for the bite), player: the body's pivot, bobber: the bobber
        // while it shows.
        static Vector2 Anchor(FishingLineFX f, string anchor, string slot)
        {
            Vector2 body = G.Body(f).position;
            if (anchor == "player")
                return body;
            if (anchor == "bobber")
            {
                var b = G.Bobber(f);
                if (b != null && b.enabled)
                    return b.transform.position;
            }
            float hop = 0;
            if (CueRules.HasNativeMark(slot))
            {
                float t = Time.time - G.MarkStart(f);
                hop =
                    t < 0.06f ? 0
                    : t < 0.12f ? 4
                    : t < 0.18f ? 3
                    : 2;
            }
            return body + new Vector2(1f / 64f, 1.2f + hop / Ppu);
        }

        // ---- the game's fishing effects ----

        static class G
        {
            public static AccessTools.FieldRef<FishingLineFX, SpriteRenderer> MarkRef,
                BobberRef;
            public static AccessTools.FieldRef<FishingLineFX, float> MarkStartRef;
            public static AccessTools.FieldRef<FishingLineFX, Transform> BodyRef;
            public static AccessTools.FieldRef<FishingLineFX, PLAYERSTATE> StateRef;
            public static AccessTools.FieldRef<FishingLineFX, PlayerModel> ModelRef;
            public static System.Reflection.MethodInfo Build;

            public static SpriteRenderer Mark(FishingLineFX f) => MarkRef(f);

            public static SpriteRenderer Bobber(FishingLineFX f) => BobberRef(f);

            public static float MarkStart(FishingLineFX f) => MarkStartRef(f);

            public static Transform Body(FishingLineFX f) => BodyRef(f);

            public static PLAYERSTATE State(FishingLineFX f) => StateRef(f);

            public static bool Local(FishingLineFX f) => ModelRef(f) != null && ModelRef(f) == PlayerModel.LocalPlayer;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Patch()
        {
            G.MarkRef = AccessTools.FieldRefAccess<FishingLineFX, SpriteRenderer>("mark");
            G.BobberRef = AccessTools.FieldRefAccess<FishingLineFX, SpriteRenderer>("bobber");
            G.MarkStartRef = AccessTools.FieldRefAccess<FishingLineFX, float>("markStart");
            G.BodyRef = AccessTools.FieldRefAccess<FishingLineFX, Transform>("body");
            G.StateRef = AccessTools.FieldRefAccess<FishingLineFX, PLAYERSTATE>("state");
            G.ModelRef = AccessTools.FieldRefAccess<FishingLineFX, PlayerModel>("model");
            G.Build = AccessTools.Method(typeof(FishingLineFX), "Build");
            var mark = AccessTools.Method(typeof(FishingLineFX), "UpdateMark");
            var ev = AccessTools.Method(typeof(FishingLineFX), nameof(FishingLineFX.OnFishingEvent));
            if (mark == null || ev == null)
                return false;
            Patches.All(
                harmony,
                (mark, null, new HarmonyMethod(typeof(CueStage), nameof(MarkPostfix))),
                (ev, null, new HarmonyMethod(typeof(CueStage), nameof(EventPostfix)))
            );
            return true;
        }

        // The bite: starts when the game starts its mark, hides the mark if the cue replaces it.
        static void MarkPostfix(FishingLineFX __instance)
        {
            try
            {
                var p = instance;
                if (p == null || p.looks.Count == 0 || !G.Local(__instance))
                    return;
                if (G.State(__instance) == PLAYERSTATE.ACTION_FISHING_BITE)
                {
                    float start = G.MarkStart(__instance);
                    if (start != p.lastMarkStart)
                    {
                        p.lastMarkStart = start;
                        p.Begin("fishing_bite", __instance);
                    }
                    if (
                        p.looks.TryGetValue("fishing_bite", out var look)
                        && look.Def.HideNative
                        && look.Frames.Length > 0
                    )
                        G.Mark(__instance).enabled = false;
                }
                p.Draw();
            }
            catch (Exception) { }
        }

        static void EventPostfix(FishingLineFX __instance, FishingEvent fishingEvent)
        {
            try
            {
                var p = instance;
                if (p == null || p.looks.Count == 0 || !G.Local(__instance))
                    return;
                string slot = SlotOf(fishingEvent);
                if (slot != null)
                    p.Begin(slot, __instance);
            }
            catch (Exception) { }
        }

        static string SlotOf(FishingEvent e)
        {
            switch (e)
            {
                case FishingEvent.Nibble:
                    return "fishing_nibble";
                case FishingEvent.Tug:
                    return "fishing_tug";
                case FishingEvent.Spook:
                    return "fishing_spook";
                case FishingEvent.Leave:
                    return "fishing_leave";
                default:
                    return null;
            }
        }

        // ---- measurements ----

        public static SpriteRenderer NativeMark(FishingLineFX f) => G.Mark(f);

        public IEnumerable<(string Slot, bool Visible, Vector3 Position, string Sprite)> Shown()
        {
            foreach (var kv in showing)
                yield return (
                    kv.Key,
                    kv.Value.Renderer != null && kv.Value.Renderer.enabled,
                    kv.Value.Renderer != null ? kv.Value.Renderer.transform.position : Vector3.zero,
                    kv.Value.Renderer != null && kv.Value.Renderer.sprite != null
                        ? kv.Value.Renderer.sprite.rect.ToString() + " x" + kv.Value.Renderer.transform.localScale.x
                        : null
                );
        }
    }
}
