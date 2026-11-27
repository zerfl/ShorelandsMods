using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core;

// The Lua sandbox: globals, escapes, dofile confinement, the 'Lua is running' guard.
static partial class Program
{
    // ---- the Lua sandbox ----

    static void Sandbox()
    {
        using var rig = new Rig(new SpyArea());
        var spy = (SpyArea)null;
        rig.Mod(
            "box",
            @"
local names = {}
for k in pairs(_G) do names[#names + 1] = k end
table.sort(names)
Out(table.concat(names, ' '))
local function try(name, f)
  local ok, err = pcall(f)
  Out(name .. ' ' .. (ok and 'OK' or 'ERR'))
end
try('io', function() return io.open('C:/Windows/win.ini') end)
try('os.execute', function() return os.execute('calc') end)
try('os.getenv', function() return os.getenv('PATH') end)
try('os.remove', function() return os.remove('C:/x') end)
try('require', function() return require('os') end)
try('load', function() return load('return 1') end)
try('loadstring', function() return loadstring('return 1') end)
try('loadfile', function() return loadfile('C:/Windows/win.ini') end)
try('dofile abs', function() return dofile('C:/Windows/win.ini') end)
try('dofile up', function() return dofile('../other/init.lua') end)
try('string.dump', function() return string.dump(function() end) end)
try('method dump', function() return (''):dump() end)
try('debug', function() return debug.getregistry() end)
try('coroutine', function() return coroutine.create(function() end) end)
try('collectgarbage', function() return collectgarbage('collect') end)
try('getfenv', function() return getfenv(1) end)
try('os.exit', function() return os.exit(1) end)
Out('os', os.time() > 0, type(os.date('%Y')), os.clock() >= 0)
local osn = {} for k in pairs(os) do osn[#osn+1] = k end table.sort(osn)
Out('osnames', table.concat(osn, ' '))
Out('aliases', unpack == table.unpack, bit == bit32)
getmetatable('').__index = { upper = function() return 'pwned' end }
setmetatable(_G, { __index = function(t, k) return 'dflt:' .. k end })
Out('mt', type(__lua_mathematics_library_random_instance))
"
        );
        rig.Mod("other", "Out('other upper', ('x'):upper(), type(nothing_here))");
        rig.Start();
        spy = rig.Host.Area<SpyArea>();
        var o = spy.Out;
        Check(rig["box"].Status == ModStatus.Loaded, "sandbox mod loads", rig["box"].Reason);
        string globals = o.FirstOrDefault() ?? "";
        string[] expected =
        {
            "_G",
            "_VERSION",
            "assert",
            "bit",
            "bit32",
            "dofile",
            "dofile_once",
            "error",
            "getmetatable",
            "ipairs",
            "math",
            "next",
            "os",
            "pairs",
            "pcall",
            "rawequal",
            "rawget",
            "rawlen",
            "rawset",
            "select",
            "setmetatable",
            "string",
            "table",
            "tonumber",
            "tostring",
            "type",
            "unpack",
            "xpcall",
        };
        var apiNames = rig.Host.Api.Functions.Select(f => f.Name);
        var got = globals.Split(' ').Where(n => n.Length > 0).ToHashSet();
        var want = expected.Concat(apiNames).Append("__lua_mathematics_library_random_instance").ToHashSet();
        Check(
            got.SetEquals(want),
            "exact global set",
            "extra: " + string.Join(" ", got.Except(want)) + " missing: " + string.Join(" ", want.Except(got))
        );
        foreach (var line in o.Where(l => l.EndsWith(" OK") || l.EndsWith(" ERR")))
            Check(line.EndsWith(" ERR"), "escape attempt fails: " + line);
        Check(o.Count(l => l.EndsWith(" ERR")) == 17, "all escape attempts ran", string.Join("|", o));
        Check(o.Contains("os\ttrue\tstring\ttrue"), "os.time/date/clock work", string.Join("|", o));
        Check(o.Contains("osnames\tclock date time"), "os has only clock, date, time", string.Join("|", o));
        Check(o.Contains("aliases\ttrue\ttrue"), "unpack and bit aliases");
        Check(o.Contains("mt\tuserdata"), "math's random state is opaque userdata");
        Check(
            o.Contains("other upper\tX\tnil"),
            "another mod's string metatable and _G are untouched",
            string.Join("|", o)
        );

        // Compiled chunks are refused, both as init.lua and through dofile.
        using var rig2 = new Rig(new SpyArea());
        rig2.Mod("bin", "\u001bLuaR\0\u0001\u0004");
        rig2.Mod("bin2", "local ok, e = pcall(dofile, 'files/b.lua') Out(ok, e)");
        rig2.Write("bin2", "files/b.lua", "\u001bLuaR\0\u0001");
        rig2.Start();
        Check(
            rig2["bin"].Status == ModStatus.Failed && rig2["bin"].Reason.Contains("compiled"),
            "binary init.lua refused",
            rig2["bin"].Reason
        );
        var o2 = rig2.Host.Area<SpyArea>().Out;
        Check(
            o2.Count == 1 && o2[0].StartsWith("false") && o2[0].Contains("compiled"),
            "binary dofile refused",
            string.Join("|", o2)
        );
    }

    static void DoFile()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "lib",
            @"
local u = dofile('files/util.lua')
Out('util', u.add(2, 3), loads)
dofile_once('files/util.lua') dofile_once('mods/lib/files/util.lua')
Out('once', loads)
dofile('files/util.lua')
Out('again', loads)
local ok, e = pcall(dofile, 'files/missing.lua') Out('missing', ok, e)
ok, e = pcall(dofile, '../evil/init.lua') Out('escape', ok, e)
ok, e = pcall(dofile, 'files/data.txt') Out('txt', ok, e)
ok, e = pcall(dofile, 'files/broken.lua') Out('broken', ok, e)
"
        );
        rig.Write("lib", "files/util.lua", "loads = (loads or 0) + 1 return { add = function(a, b) return a + b end }");
        rig.Write("lib", "files/data.txt", "x");
        rig.Write("lib", "files/broken.lua", "x = = 1");
        rig.Write("evil", "init.lua", "return 1");
        rig.Start();
        var o = rig.Host.Area<SpyArea>().Out;
        Check(o.Contains("util\t5\t1"), "dofile returns the file's values", string.Join("|", o));
        Check(
            o.Contains("once\t2"),
            "dofile_once runs a file once per path, Noita prefix included",
            string.Join("|", o)
        );
        Check(o.Contains("again\t3"), "dofile runs every time");
        Check(
            o.Any(l => l.StartsWith("missing\tfalse") && l.Contains("no file files/missing.lua")),
            "missing file is a Lua error"
        );
        Check(
            o.Any(l => l.StartsWith("escape\tfalse") && l.Contains("inside the mod folder")),
            "dofile cannot leave the folder"
        );
        Check(o.Any(l => l.StartsWith("txt\tfalse") && l.Contains("not a .lua file")), "dofile reads only .lua");
        Check(
            o.Any(l => l.StartsWith("broken\tfalse") && l.Contains("files/broken.lua:1")),
            "syntax error in a dofile names its file"
        );
    }

    static void Guard()
    {
        using var rig = new Rig(new SpyArea());
        rig.Mod(
            "g",
            @"
Out('init', Depth())
TimerAfter(0.1, function() Out('timer', Depth()) end)
CommandRegister('gg', 'x')
function OnCommand_gg(a) Out('command', Depth(), a) end
function OnTick() Out('tick', Depth()) end
"
        );
        rig.Mod(
            "sender",
            "function OnTick() SendCommand('System.Void ReworkedCopium.Fishing.FishingManager::CmdRequestFishingSpots()') "
                + "Out('after send') end"
        );
        rig.Start();
        rig.Run(0.3);
        rig.Commands.Type("gg hello there");
        var o = rig.Host.Area<SpyArea>().Out;
        Check(
            o.Contains("init\t1") && o.Contains("timer\t1") && o.Contains("tick\t1"),
            "flag set in init, timers and callbacks",
            string.Join("|", o)
        );
        Check(o.Contains("command\t1\thello there"), "flag set in commands, args passed", string.Join("|", o));
        Check(LuaGuard.Depth == 0, "flag cleared after calls");
        Check(
            rig["sender"].Status == ModStatus.Failed && rig["sender"].Reason.Contains("send"),
            "a refused send switches the mod off",
            rig["sender"].Reason
        );
        Check(rig.Env.ChatText.Contains("CmdRequestFishingSpots"), "the refusal names what was sent");
        Check(o.Count(l => l == "after send") == 1, "the call finishes, then the mod is off");
    }

    // A host function reaching another mod's Lua: Current is the inner mod inside and the outer one again after. The
    // flag is down after a call that errors and after one the watchdog stopped.
    static void GuardNesting()
    {
        using var clock = new ManualClock();
        var spy = new SpyArea();
        using var rig = new Rig(spy);
        rig.Mod("a", "function outer() Out('a', Who()) CallMod('b', 'inner') Out('a', Who()) end");
        rig.Mod("b", "function inner() Out('b', Who()) end");
        rig.Mod("e", "function bad() error('x') end");
        rig.Mod("s", "function slow() Spend(25) end");
        rig.Start();
        rig.GiveSpend();
        rig.Host.Call(rig["a"], "outer", rig["a"].Lua.Global("outer"));
        Check(
            string.Join("|", spy.Out) == "a\ta 1|b\tb 2|a\ta 1",
            "inside the inner call the inner mod runs, after it the outer one again",
            string.Join("|", spy.Out)
        );
        Check(LuaGuard.Depth == 0 && LuaGuard.Current == null, "down after the calls");
        rig.Host.Call(rig["e"], "bad", rig["e"].Lua.Global("bad"));
        Check(
            rig["e"].Status == ModStatus.Failed && LuaGuard.Depth == 0 && LuaGuard.Current == null,
            "down after a call that errors",
            rig["e"].Reason
        );
        rig.Host.Call(rig["s"], "slow", rig["s"].Lua.Global("slow"));
        Check(
            rig["s"].Reason.Contains("longer than 20 ms") && LuaGuard.Depth == 0 && LuaGuard.Current == null,
            "down after a call the watchdog stopped",
            rig["s"].Reason
        );
    }

    // An unwinding that skipped the reset leaves the flag up: the next frame takes it down and logs it once.
    static void GuardReset()
    {
        using var rig = new Rig();
        rig.Mod("m", "function OnTick() end");
        rig.Start();
        rig.Run(0.1);
        Check(!rig.Env.LogLines.Any(l => l.Contains("still set")), "a normal frame finds the flag down");
        LuaGuard.Enter(rig["m"].Lua);
        rig.Run(0.05);
        Check(
            LuaGuard.Depth == 0
                && LuaGuard.Current == null
                && rig.Env.LogLines.Count(l => l.Contains("'Lua is running' flag was still set")) == 1,
            "a flag left up is cleared at the next frame, with a warning",
            string.Join("|", rig.Env.LogLines)
        );
    }

    // The send guard's rule (SendRules, which Game/SendGuard.cs asks for every Command and message): everything goes
    // while no Lua runs; while it runs, Commands and messages to the server are refused and named, those to clients go.
    static void SendGuardRules()
    {
        const string cmd = "System.Void ReworkedCopium.Fishing.FishingManager::CmdRequestFishingSpots(System.Int32)";
        var msg = new byte[] { 9, 0x34, 0x12, 7 };
        Check(
            SendRules.Command(cmd) == null && SendRules.Message(true, msg, 1, 3) == -1,
            "no Lua running: everything is sent"
        );
        var previous = LuaGuard.Enter(null);
        try
        {
            Check(
                SendRules.Command(cmd) == "Command FishingManager.CmdRequestFishingSpots",
                "a Command while Lua runs is refused, named by class and method",
                SendRules.Command(cmd)
            );
            Check(SendRules.Message(true, msg, 1, 3) == 0x1234, "a message to the server is refused, named by its id");
            Check(SendRules.Message(true, msg, 3, 1) == 0, "one too short for an id is refused too");
            Check(SendRules.Message(false, msg, 1, 3) == -1, "a message to a client goes");
        }
        finally
        {
            LuaGuard.Exit(previous);
        }
    }
}
