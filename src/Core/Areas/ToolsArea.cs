using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lua;
using ShorelandsMods.Core.Ui;

namespace ShorelandsMods.Core.Areas
{
    // Files written by an export: the full paths, and what could not be written with the reason.
    internal sealed class ExportResult
    {
        public readonly List<string> Written = new List<string>();
        public readonly List<string> Problems = new List<string>();
    }

    // What the author tools do in the game (Game/Tools); a fake offline.
    internal interface IAuthorTools
    {
        bool InWorld { get; }
        bool Inspecting { get; }
        void SetInspect(bool on);

        // The game's own pixels, never a mod's: a texture by name (every size loaded now), an item or koi icon by id,
        // or everything loaded now.
        ExportResult ExportTexture(string name);
        ExportResult ExportItem(int id);
        ExportResult ExportKoi(int id);
        ExportResult ExportAll();

        // For /try: an item as OnItemGained gets it (null for an unknown id); currency and CITB (null outside the world).
        LuaTable ItemTable(int id, int count, int meta);
        double[] Wallet();
    }

    // The player's tools for making mods: inspect mode, exporting the game's original art, previewing a mod's sounds
    // and cues, trying a mod's callback. Local commands and the Mods page only: nothing here is registered for Lua, and
    // nothing another mod can read changes when a tool runs.
    internal sealed class ToolsArea : Area
    {
        public static readonly string[] Commands = { "inspect", "export", "preview", "try" };

        public readonly IAuthorTools Tools;

        public ToolsArea(IAuthorTools tools)
        {
            Tools = tools;
        }

        public override string Name => "Author tools";

        protected internal override void Register(AreaSetup s) { }

        protected internal override void HostStarted()
        {
            var commands = Host.Area<CommandsArea>();
            commands.AddHostCommand("inspect", _ => Say(ToggleInspect()));
            commands.AddHostCommand("export", args => Say(Export(args)));
            commands.AddHostCommand("preview", args => Say(Preview(args)));
            commands.AddHostCommand("try", args => Say(Try(args)));
        }

        void Say(string text) => Host.Env.Chat(ModHost.Color.Note, text);

        public string OriginalsDir => Path.Combine(Host.Env.ModsDir, Originals.Folder);

        public string ToggleInspect()
        {
            if (!Tools.InWorld)
                return "Inspect mode works once you are in the world.";
            Tools.SetInspect(!Tools.Inspecting);
            return Tools.Inspecting ? "Inspect mode is on." : "Inspect mode is off.";
        }

        // ---- /export ----

        public string Export(string args)
        {
            var (what, arg, problem) = ParseExport(args);
            if (problem != null)
                return problem;
            switch (what)
            {
                case "help":
                    return "/export <texture name>, /export item <id>, /export koi <id> or /export all saves the game's "
                        + "own art (never a mod's) to "
                        + OriginalsDir
                        + ", in the folders of a mod's art/ folder. Inspect mode (/inspect) shows the names.";
                case "all":
                    return Said(Tools.ExportAll(), "nothing is loaded that could be saved");
                case "item":
                    return Said(Tools.ExportItem(int.Parse(arg)), null);
                case "koi":
                    return Said(Tools.ExportKoi(int.Parse(arg)), null);
            }
            var names = Host.Area<ArtArea>().Names;
            if (!names.KnowsTexture(arg))
            {
                string near = ErrorText.Suggest(arg, names.TextureNames);
                return "There is no texture called \""
                    + arg
                    + "\" (names are case-sensitive)"
                    + (near != null ? "; did you mean " + near + "?" : ". _names.txt lists them.");
            }
            return Said(Tools.ExportTexture(arg), null);
        }

        // "all", "item 5", "koi 160", a texture name; problem is a sentence for a bad id.
        public static (string What, string Arg, string Problem) ParseExport(string args)
        {
            string a = (args ?? "").Trim();
            if (a.Length == 0 || a == "help")
                return ("help", null, null);
            if (a == "all")
                return ("all", null, null);
            int space = a.IndexOf(' ');
            string first = space < 0 ? a : a.Substring(0, space);
            if (first == "item" || first == "koi")
            {
                string id = space < 0 ? "" : a.Substring(space + 1).Trim();
                if (!int.TryParse(id, out int n) || n < 0 || n.ToString() != id)
                    return (
                        first,
                        id,
                        "/export "
                            + first
                            + " needs the "
                            + first
                            + "'s id, like /export "
                            + first
                            + (first == "item" ? " 101" : " 160")
                            + " (_names.txt lists them)."
                    );
                return (first, id, null);
            }
            return ("texture", a, null);
        }

        string Said(ExportResult r, string none)
        {
            var parts = new List<string>();
            if (r.Written.Count == 1)
                parts.Add("Saved " + r.Written[0] + ".");
            else if (r.Written.Count > 1)
                parts.Add("Saved " + r.Written.Count + " files to " + OriginalsDir + ".");
            if (r.Problems.Count > 0)
                parts.Add(
                    (r.Written.Count > 0 ? "Not saved: " : "")
                        + string.Join("; ", r.Problems.Take(3))
                        + (r.Problems.Count > 3 ? " (and " + (r.Problems.Count - 3) + " more, in the log)" : "")
                        + "."
                );
            foreach (var p in r.Problems.Skip(3))
                Host.Env.Log(LogLevel.Info, "export: not saved: " + p);
            if (parts.Count == 0)
                parts.Add(char.ToUpperInvariant(none[0]) + none.Substring(1) + ".");
            return string.Join(" ", parts);
        }

        // Inspect mode's click: saves the marked thing, says the full path in chat, and returns the panel's line.
        public string ExportEntry(InspectEntry e)
        {
            var h = e.Hit;
            var r =
                h.ItemId >= 0 ? Tools.ExportItem(h.ItemId)
                : h.KoiId >= 0 ? Tools.ExportKoi(h.KoiId)
                : Tools.ExportTexture(h.Texture);
            Say(Said(r, "nothing was saved"));
            if (r.Written.Count == 0)
                return "Not saved; chat says why.";
            string first = r.Written[0].Substring(Host.Env.ModsDir.Length).Replace('\\', '/');
            return "Saved mods" + first + (r.Written.Count > 1 ? " and " + (r.Written.Count - 1) + " more" : "");
        }

        // ---- /preview ----

        public string Preview(string args)
        {
            var words = (args ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1 && words[0] == "stop")
            {
                Host.Area<SoundArea>().StopPreview();
                return "Preview stopped.";
            }
            if (words.Length != 2)
                return "/preview <mod> <sound or cue> plays a mod's file for that name now, like /preview sound_pack "
                    + "chat or /preview sound_pack fishing_bite (the cue's image shows at your character); /preview "
                    + "stop ends it. /mods <mod> lists its files.";
            var mod = Host.Find(words[0]);
            if (mod == null)
                return "There is no mod \"" + words[0] + "\".";
            string name = PreviewName(words[1]);
            bool inWorld = Tools.InWorld;
            string why = Preview(mod, name, inWorld);
            if (why != null)
                return why;
            var sound = Host.Area<SoundArea>();
            string muted = sound.Muted ? " " + SoundArea.MutedSentence : "";
            var cue = Host.Area<CueArea>().Of(mod, name);
            if (cue == null)
                return "Playing " + name + " of " + mod.Id + " (" + Secs(sound.PreviewSeconds) + ")." + muted;
            if (cue.ImageRel == null || !inWorld)
                return "Playing the sound of "
                    + mod.Id
                    + "'s "
                    + name
                    + " cue ("
                    + Secs(sound.PreviewSeconds)
                    + ")."
                    + (cue.ImageRel != null ? " Its image shows at your character once you are in the world." : "")
                    + muted;
            double each = CueArea.PreviewSeconds(cue);
            int cycles = CueArea.PreviewCycles(cue);
            string what = "Preview: " + mod.Id + "'s " + name + " cue, " + cycles + " times " + Secs(each) + " each";
            Host.Area<NoticeArea>()?.Queue.Add(mod.Id, what, CueRules.PreviewTotal(each, cycles), Host.Env.Now);
            return what
                + " at your character"
                + (cue.Clip >= 0 ? ", with its sound" : "")
                + (
                    cue.Options.Seconds == null && CueRules.HasNativeMark(name)
                        ? " (in the game the bite shows as long as the fish is on)"
                        : ""
                )
                + "."
                + (cue.Clip >= 0 ? muted : "");
        }

        static string Secs(double s) => s.ToString(s < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " s";

        // A sound or cue of the mod by name or file ("chat", "sounds/chat.ogg", "fishing_bite"); null when it plays,
        // else why not.
        public string Preview(Mod mod, string nameOrFile, bool inWorld)
        {
            string name = PreviewName(nameOrFile);
            var sound = Host.Area<SoundArea>();
            var cues = Host.Area<CueArea>();
            var known = sound.PreviewNames(mod).Concat(cues.PreviewNames(mod)).OrderBy(n => n).ToList();
            if (!known.Contains(name))
                return mod.Id
                    + " has no sound or cue called \""
                    + name
                    + "\""
                    + (known.Count == 0 ? "." : "; it has " + string.Join(", ", known) + ".");
            return AssetNames.CueSet.Contains(name) ? cues.Preview(mod, name, inWorld) : sound.Preview(mod, name);
        }

        public static string PreviewName(string nameOrFile)
        {
            string n = nameOrFile.Replace('\\', '/');
            n = n.Substring(n.LastIndexOf('/') + 1);
            int dot = n.LastIndexOf('.');
            return dot > 0 ? n.Substring(0, dot) : n;
        }

        // ---- /try ----

        // The mod whose callback /try runs now: its GamePrint lines are marked as a test.
        public Mod Trying { get; private set; }

        public string Try(string args)
        {
            string a = (args ?? "").Trim();
            int sp = a.IndexOf(' ');
            string id = sp < 0 ? a : a.Substring(0, sp);
            if (id.Length == 0)
                return "/try <mod> lists what you can try on a mod; /try <mod> <what> calls its function for that event "
                    + "with made-up values, like /try town_counter town or /try chat_helper whisper Friend hello. Only "
                    + "that mod hears it; nothing happens in the game.";
            var mod = Host.Find(id);
            if (mod == null)
            {
                string near = ErrorText.Suggest(id, Host.Mods.Select(m => m.Id));
                return "There is no mod \"" + id + "\"" + (near != null ? "; did you mean " + near + "?" : ".");
            }
            if (mod.Status != ModStatus.Loaded || mod.Lua == null)
                return "[test] " + mod.Id + " is not running (" + ModHost.StatusWord(mod) + "), so it cannot be tried.";
            Func<string, bool> has = name => mod.Lua.HasFunction(name);
            var facts = FactsOf(mod);
            if (sp < 0)
                return Listing(mod, TryCommand.Shortcuts(has, facts));
            var (plan, problem) = TryCommand.Parse(a.Substring(sp + 1), has, facts);
            if (plan == null)
                return "[test] Not tried: " + problem + ". /try " + mod.Id + " lists what it has.";
            return "[test] " + mod.Id + " " + plan.Shown + " " + Run(mod, plan);
        }

        public ITryFacts FactsOf(Mod mod) => new ModFacts(mod, Tools);

        static string Listing(Mod mod, List<(string Callback, string Words)> shortcuts)
        {
            string how = " Or name the function and its values: /try " + mod.Id + " OnTick 0.1.";
            if (shortcuts.Count == 0)
                return "[test] /try " + mod.Id + " <what>: it has none of the common event functions." + how;
            return "[test] /try "
                + mod.Id
                + " <what> calls its function for an event with made-up values (only this mod hears it): "
                + string.Join("; ", shortcuts.Select(s => s.Words + " (" + s.Callback + ")"))
                + "."
                + how;
        }

        // Runs the plan in the mod's Lua as the event would; the sentence after the call's line.
        string Run(Mod mod, TryPlan plan)
        {
            var fn = mod.Lua.Global(plan.Callback);
            var a = plan.Args;
            LuaValue V(int i) => i < a.Length ? a[i] : LuaValue.Nil;
            LuaValue result = LuaValue.Nil;
            Trying = mod;
            try
            {
                if (plan.Setup)
                    Host.FireOne(mod, plan.Callback, Limits.Init);
                else if (plan.Ask)
                    result = Host.Ask(mod, plan.Callback, a[0]);
                else
                    Host.Call(mod, plan.Callback, fn, a.Length, new Args(V(0), V(1), V(2), V(3)));
            }
            finally
            {
                Trying = null;
            }
            if (mod.Status != ModStatus.Loaded)
                return "stopped the mod; the line above says why.";
            var budget = plan.Setup ? Limits.Init : Limits.Callback;
            string ran = "ran (" + CallCosts.Num(mod.Lua.LastMs) + " of " + CallCosts.Num(budget.Ms) + " ms)";
            if (!plan.Ask)
                return ran + ".";
            return ran
                + (
                    result.Type == LuaValueType.Nil
                        ? ", gave nil: no tooltip line."
                        : ", gave " + TryCommand.Show(result) + "."
                );
        }

        sealed class ModFacts : ITryFacts
        {
            readonly Mod mod;
            readonly IAuthorTools tools;
            readonly List<SettingDef> defs;

            public ModFacts(Mod mod, IAuthorTools tools)
            {
                this.mod = mod;
                this.tools = tools;
                defs = mod.Find<SettingsArea.State>()?.Defs ?? new List<SettingDef>();
            }

            public IReadOnlyList<string> SettingIds => defs.Select(d => d.Id).ToList();

            public IReadOnlyList<string> KeyIds =>
                defs.Where(d => d.Type == SettingType.Key).Select(d => d.Id).ToList();

            public LuaValue Setting(string id)
            {
                var def = defs.FirstOrDefault(d => d.Id == id);
                return def == null
                    ? LuaValue.Nil
                    : SettingsSchema.ToLua(SettingsArea.Value(mod.Find<SettingsArea.State>(), def));
            }

            public LuaTable Item(int id, int count, int meta) => tools.ItemTable(id, count, meta);

            public double[] Wallet() => tools.Wallet();
        }
    }
}
