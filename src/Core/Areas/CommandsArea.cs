using System;
using System.Collections.Generic;
using System.Linq;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    // Where local slash commands live: the game's client command dispatcher in the game, a fake in tests.
    internal interface ICommandSink
    {
        bool Available { get; }

        // "game" or "server" if the game or the server's command tree has the name, else null. Names the host itself
        // added through Add are not reported.
        string Owner(string name);

        void Add(string name, Action<string> run);
        void Remove(string name);

        // Raised when the server sends its command tree (after login); names may have become taken.
        event Action ServerCommandsChanged;

        // Raised with the first word of a typed command that neither the game nor the server knows (the game then
        // said "Unknown or incomplete command", or sends the line before the server's tree arrived).
        event Action<string> Unknown;
    }

    internal static class CommandRules
    {
        public const int MaxLength = 24;

        public static string Problem(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxLength)
                return "a command name must be 1 to " + MaxLength + " characters";
            if (name[0] < 'a' || name[0] > 'z')
                return "a command name must start with a lower case letter";
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                    return "a command name may only use lower case letters, digits and _ (\"" + name + "\")";
            return null;
        }
    }

    internal sealed class CommandsArea : Area
    {
        public static readonly string[] HostCommands = new[] { "mods", "reloadmods" }
            .Concat(ToolsArea.Commands)
            .ToArray();

        readonly ICommandSink sink;
        readonly Dictionary<string, Mod> owners = new Dictionary<string, Mod>();
        GameHook hook;

        public CommandsArea(ICommandSink sink)
        {
            this.sink = sink;
            sink.ServerCommandsChanged += Recheck;
            sink.Unknown += word =>
            {
                string hint = UnknownHint(word, HostCommands.Concat(owners.Keys));
                if (hint != null)
                    Host.Env.Chat(ModHost.Color.Note, hint);
            };
        }

        // A typed command near a host or mod command: what the player meant, or null.
        public static string UnknownHint(string word, IEnumerable<string> known)
        {
            var names = known.ToList();
            string bare = word.TrimStart('/');
            if (bare.Length < word.Length && names.Contains(bare))
                return "Type /" + bare + " with one slash.";
            string near = null;
            int best = bare.Length <= 4 ? 1 : 2;
            foreach (var n in names)
            {
                int d = ErrorText.Distance(bare.ToLowerInvariant(), n);
                if (d <= best)
                {
                    best = d - 1;
                    near = n;
                }
            }
            return near != null ? "Did you mean /" + near + "?" : null;
        }

        public override string Name => "Commands";

        protected internal override void Register(AreaSetup s)
        {
            hook = s.Hook("CommandHandler.ClientDispatcher", "slash commands", () => sink.Available);
            s.Function(
                    "CommandRegister( name:string, description:string ) -> ok:boolean",
                    "adds the chat command /name for this player only (nothing is sent); typing it calls "
                        + "OnCommand_name( args ). name: a lower case letter, then lower case letters, digits, _ (at most "
                        + CommandRules.MaxLength
                        + "). false, with a chat notice, for another name or one the game, the server, the host ("
                        + string.Join(", ", HostCommands)
                        + ") or an earlier mod has, or past "
                        + Limits.CommandsPerMod
                        + " commands of the mod; false when the game changed",
                    Register
                )
                .Needs(hook, false);
            s.Callback(
                "OnCommand_<name>( args:string )",
                "the player typed /<name> for a command this mod registered; args is the rest of the line after one "
                    + "space (\"\" if none)"
            );
        }

        protected internal override void HostStarted()
        {
            if (!hook.Available)
                return;
            sink.Add("mods", args => ListMods((args ?? "").Trim()));
            sink.Add("reloadmods", _ => Host.Post(ReloadAll));
        }

        // A command of the host itself (its name must be in HostCommands, so no mod can take it).
        public void AddHostCommand(string name, Action<string> run)
        {
            if (hook.Available && Array.IndexOf(HostCommands, name) >= 0)
                sink.Add(name, args => run((args ?? "").Trim()));
        }

        void ListMods(string id)
        {
            if (id.Length == 0)
            {
                Host.Env.Chat(
                    ModHost.Color.Note,
                    "ShorelandsMods "
                        + Host.Env.HostVersion
                        + ". Mods ("
                        + Host.Mods.Count
                        + "), /mods <id> for one, /mods off <id> switches one off:"
                );
                foreach (var line in Host.StatusLines())
                    Host.Env.Chat(ModHost.Color.Note, line);
                return;
            }
            var words = id.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && (words[0] == "off" || words[0] == "on"))
            {
                Switch(words[1], words[0] == "off");
                return;
            }
            var mod = Host.Find(id);
            if (mod == null)
            {
                Host.Env.Chat(ModHost.Color.Note, "There is no mod \"" + id + "\".");
                return;
            }
            foreach (var line in ModReport(Host, mod))
                Host.Env.Chat(ModHost.Color.Note, line);
        }

        // /mods off <id>, /mods on <id>: the Mods page's switch from chat, the way out when a mod's windows, HUD,
        // notices or sounds are in the way. Off stops the mod, which removes all of them.
        void Switch(string id, bool off)
        {
            var mod = Host.Find(id);
            if (mod == null)
            {
                Host.Env.Chat(ModHost.Color.Note, "There is no mod \"" + id + "\". /mods lists them.");
                return;
            }
            Host.Post(() =>
            {
                Host.SwitchOff(mod.Id, off);
                Host.Env.Chat(
                    ModHost.Color.Note,
                    "[" + mod.Id + "] switched " + (off ? "off; /mods on " + mod.Id + " switches it on again." : "on.")
                );
            });
        }

        // The status line, where its data and exports are, then the asset files: the applied ones counted per folder
        // in one line, every other file (and an applied one with a note) on a line of its own; the Mods page lists
        // the applied ones by name.
        public static List<string> ModReport(ModHost host, Mod mod)
        {
            var lines = new List<string>
            {
                host.StatusLine(mod),
                "  data: " + mod.DataDir + " (saved data; ExportText files in its export folder)",
            };
            string costs = CallCosts.Line(mod);
            if (costs != null)
            {
                lines.Add("  " + costs);
                lines.Add("  " + mod.Load.Line(host.Env.Now));
            }
            var report = host.AssetReport(mod);
            var applied = report.Where(l => l.State == AssetState.Applied && l.Detail == null).ToList();
            if (applied.Count > 0)
                lines.Add(
                    "  applied: "
                        + applied.Count
                        + (applied.Count == 1 ? " file (" : " files (")
                        + string.Join(", ", applied.GroupBy(l => Folder(l.File)).Select(g => g.Key + " " + g.Count()))
                        + ")"
                );
            foreach (var l in report.Except(applied))
                lines.Add("  " + l);
            return lines;
        }

        static string Folder(string file) => file.Substring(0, file.LastIndexOf('/') + 1);

        void ReloadAll()
        {
            Host.ReloadAll();
            int loaded = Host.Mods.Count(m => m.Status == ModStatus.Loaded);
            Host.Env.Chat(ModHost.Color.Note, "Mods reloaded: " + loaded + " of " + Host.Mods.Count + " loaded.");
        }

        int Register(ApiCall c)
        {
            string name = c.String(1, "name");
            c.Text(2, "description");
            string why = CommandRules.Problem(name);
            if (why != null)
            {
                c.Refuse(1, why + ", so /" + TextEscape.Clip(name, 40) + " was not added");
                return c.ReturnOk();
            }
            if (owners.TryGetValue(name, out var owner))
            {
                if (owner == c.Mod)
                    return c.Return(true);
                return Refuse(c, name, "the mod " + owner.Id + " has it");
            }
            string other = Array.IndexOf(HostCommands, name) >= 0 ? "ShorelandsMods" : sink.Owner(name);
            if (other != null)
                return Refuse(c, name, "the " + other + " has a command with that name");
            if (c.Mod.Resources.CountOf("command") >= Limits.CommandsPerMod)
            {
                c.Refuse(
                    1,
                    "the mod already has " + Limits.CommandsPerMod + " commands, so /" + name + " was not added"
                );
                return c.ReturnOk();
            }
            var mod = c.Mod;
            sink.Add(name, args => Run(mod, name, args));
            owners[name] = mod;
            mod.Resources.Add("command", name, () =>
            {
                sink.Remove(name);
                owners.Remove(name);
            });
            return c.Return(true);
        }

        int Refuse(ApiCall c, string name, string why)
        {
            Host.Notice(c.Mod, "/" + name + " is not available to this mod: " + why + ".");
            return c.Return(false);
        }

        void Run(Mod mod, string name, string args)
        {
            string callback = "OnCommand_" + name;
            if (mod.Lua == null || !mod.Lua.HasFunction(callback))
            {
                Host.Notice(mod, "/" + name + " does nothing: the mod has no function " + callback + ".");
                return;
            }
            Host.CallByPlayer(mod, callback, mod.Lua.Global(callback), 1, new Args(args ?? ""));
        }

        void Recheck()
        {
            foreach (var name in HostCommands)
                if (sink.Owner(name) == "server")
                    Host.Env.Log(
                        LogLevel.Warning,
                        "the server now has /" + name + "; the host's /" + name + " hides it"
                    );
            foreach (var kv in owners.ToList())
            {
                string other = sink.Owner(kv.Key);
                if (other == null)
                    continue;
                kv.Value.Resources.Remove("command", kv.Key);
                Host.Notice(kv.Value, "/" + kv.Key + " was removed: the " + other + " has a command with that name.");
            }
        }
    }
}
