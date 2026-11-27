using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Areas;
using ShorelandsMods.Core.Ui;
using ShorelandsMods.Game.Areas;
using ShorelandsMods.Game.Assets;
using ShorelandsMods.Game.Tools;
using ShorelandsMods.Game.Ui;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShorelandsMods.Game
{
    // Loads casual mods (Lua and assets) from <game>/mods.
    [BepInPlugin("com.zerfl.shorelands.mods", "ShorelandsMods", BuildInfo.Version)]
    [DefaultExecutionOrder(-30000)]
    public class ModsPlugin : BaseUnityPlugin
    {
        internal ModHost Host;
        internal AudioOut Audio;
        internal CueStage Cues;
        internal ArtStage Art;
        internal HudStage HudStage;
        internal WindowStage WindowStage;
        internal double LastUiMs;
        internal ModsMenu ModsMenu;
        HudArea hud;
        WindowArea windows;
        NoticeArea notices;
        internal GameEnv Env => env;
        GameEnv env;
        GameCommands commands;
        FrameSteps steps;
        readonly EscapePresses escapes = new EscapePresses();
        Action hostUpdate,
            pollCommands,
            flushChat,
            tickAudio,
            drawCues;
        (string Name, Action Run)[] uiSteps;
        double now;
        bool icons;

        void Awake()
        {
            steps = new FrameSteps(text => Logger.LogError(text));
            var harmony = new Harmony("com.zerfl.shorelands.mods");
            env = new GameEnv(Logger, Config, Paths.GameRootPath, Paths.ConfigPath);
            commands = new GameCommands(harmony, text => Logger.LogError(text));
            var areas = CoreAreas.Create(commands);
            areas.Add(new ChatArea(env, harmony));
            areas.Add(new PlayerArea());
            areas.Add(new WalletArea());
            areas.Add(new InventoryArea(harmony));
            areas.Add(new DataTablesArea());
            areas.Add(new WorldArea());
            areas.Add(new OfferingArea(harmony));
            var root = new GameObject("ShorelandsMods assets").transform;
            DontDestroyOnLoad(root.gameObject);
            Audio = new AudioOut(this, root);
            areas.Add(new SoundArea(Audio));
            Art = new ArtStage(harmony);
            areas.Add(Art);
            areas.Add(new ArtArea(Art));
            Cues = new CueStage(harmony, Audio, root);
            areas.Add(Cues);
            areas.Add(new CueArea(Cues));
            System.Action<string> uiLog = text => Logger.LogWarning(text);
            HudStage = new HudStage(uiLog);
            WindowStage = new WindowStage(uiLog);
            areas.Add(hud = new HudArea(HudStage));
            areas.Add(windows = new WindowArea(WindowStage));
            var noticeStage = new NoticeStage(uiLog);
            areas.Add(notices = new NoticeArea(noticeStage));
            areas.Add(new ChatDecorArea());
            areas.Add(new TooltipArea());
            areas.Add(new KeyArea(new KeyInput(uiLog)));
            areas.Add(new ChatDisplayHooks(harmony));
            areas.Add(new ChatLinkClicks(harmony));
            areas.Add(new TooltipHooks(harmony));
            var tools = new ToolsArea(new AuthorTools(Art, () => Host.Area<ArtArea>(), env.ModsDir));
            areas.Add(tools);
            var inspectKey = Config.Bind(
                "Author tools",
                "Inspect key",
                Key.F2,
                "Turns inspect mode on and off (names the art under the pointer; also /inspect and the Mods page)."
            );
            Inspector.Init(tools, Art, () => inspectKey.Value);
            HudStage.Area = hud;
            noticeStage.TopFrom = () =>
            {
                int bottom = Inspector.BannerBottom;
                foreach (var w in hud.Widgets)
                    if (w.Visible && w.Anchor == Anchor.Top && w.Rect.Y < 120)
                        bottom = System.Math.Max(bottom, w.Rect.Bottom);
                return bottom;
            };
            WindowStage.Area = windows;
            Host = new ModHost(env, areas);
            ModsMenu = new ModsMenu(Host, text => Logger.LogInfo(text));
            Watchdog.Start();
            if (!SendGuard.Install(harmony, Logger))
                Host.BlockLua("the send guard could not be installed, so Lua mods are off (details in the log)");
            Host.Start(true);
            hostUpdate = () => Host.Update(Time.unscaledDeltaTime);
            pollCommands = commands.Poll;
            flushChat = env.FlushChat;
            tickAudio = () => Audio.Tick(Time.unscaledDeltaTime);
            drawCues = Cues.Draw;
            uiSteps = new (string, Action)[]
            {
                ("icons", PumpIcons),
                ("hud", DrawHud),
                ("escape", CheckEscapes),
                ("windows", DrawWindows),
                ("notices", DrawNotices),
                ("inspector", Inspector.Tick),
                ("input mute", InputMute.Check),
                ("mods menu", ModsMenu.Tick),
            };
        }

        void PumpIcons() => icons = Icons.Pump(2);

        void DrawHud()
        {
            if (icons)
                HudStage.IconsArrived();
            hud.Flush(now);
        }

        // Quick Escapes while a mod fought the player over a window: the settings menu on the Mods page.
        void CheckEscapes()
        {
            if (
                UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true
                && escapes.Press(now) is double since
                && windows.Panic(since) is string fought
            )
                ModsMenu.OpenOn(fought);
        }

        void DrawWindows()
        {
            windows.Flush();
            WindowStage.Tick(icons);
        }

        void DrawNotices() => notices.Flush(now);

        // Earliest script phase of every frame (DefaultExecutionOrder); Host.Update first clears a stuck "Lua is
        // running" flag.
        void Update()
        {
            if (steps == null)
                return;
            steps.Run("host", hostUpdate);
            steps.Run("server commands", pollCommands);
            steps.Run("chat", flushChat);
        }

        // Fades, cues that animate while the game's fishing effect is idle, then the host's UI after this frame's Lua.
        void LateUpdate()
        {
            if (uiSteps == null)
                return;
            steps.Run("audio", tickAudio);
            steps.Run("cues", drawCues);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            now = env.Now;
            icons = false;
            foreach (var (name, run) in uiSteps)
                steps.Run(name, run);
            LastUiMs =
                (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        void OnApplicationQuit() => Host.Shutdown();
    }

    // One step of a frame each: its first error is logged and the other steps still run.
    sealed class FrameSteps
    {
        readonly HashSet<string> failed = new HashSet<string>();
        readonly Action<string> logError;

        public FrameSteps(Action<string> logError) => this.logError = logError;

        public void Run(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                if (failed.Add(name))
                    logError("host step '" + name + "' failed (logged once): " + e);
            }
        }
    }
}
