using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lua;
using Lua.Runtime;

namespace ShorelandsMods.Core
{
    // An estimate of the Lua data a mod keeps between calls: everything reachable from its globals, the functions the
    // host holds for it (timers, buttons, searches), the registry, the string metatable and dofile_once's results,
    // with the .NET sizes Lua-CSharp gives these objects (a string 2 bytes a character, a table its array capacity
    // and hash entries, a function its upvalues and code). Each object counts once, under the first root reaching it.
    // Not seen: what the host copied out of Lua (window rows, HUD texts, saved data; capped by count instead) and
    // garbage, which is not kept.
    internal static class LuaData
    {
        const int StringBytes = 24,
            CharBytes = 2,
            TableBytes = 64,
            SlotBytes = 24,
            EntryBytes = 56,
            FunctionBytes = 48,
            UpvalueBytes = 40,
            CodeBytes = 4;

        public const string HeldRoot = "functions the host holds for it (timers, buttons, searches)",
            OtherRoot = "the string metatable or the registry";

        // Stops counting once over stopAbove, so a walk costs at most that much.
        public static long Measure(
            LuaTable globals,
            IEnumerable<LuaFunction> held,
            IEnumerable<LuaValue> otherRoots,
            long stopAbove,
            out string mostly
        )
        {
            var walk = new Walk(stopAbove);
            walk.Own(globals);
            long most = -1;
            mostly = OtherRoot;
            foreach (var kv in globals)
            {
                long before = walk.Bytes;
                walk.Value(kv.Key);
                walk.Value(kv.Value);
                walk.Drain();
                if (walk.Bytes - before > most)
                {
                    most = walk.Bytes - before;
                    mostly = "the global " + (kv.Key.TryRead<string>(out var name) ? "\"" + name + "\"" : "table");
                }
                if (walk.Over)
                    return walk.Bytes;
            }
            long start = walk.Bytes;
            foreach (var f in held)
                walk.Value(f);
            walk.Drain();
            if (walk.Bytes - start > most)
            {
                most = walk.Bytes - start;
                mostly = HeldRoot;
            }
            start = walk.Bytes;
            foreach (var v in otherRoots)
                walk.Value(v);
            walk.Drain();
            if (walk.Bytes - start > most)
                mostly = OtherRoot;
            return walk.Bytes;
        }

        sealed class Walk
        {
            readonly HashSet<object> seen = new HashSet<object>(Identity.Instance);
            readonly Stack<object> pending = new Stack<object>();
            readonly long stopAbove;
            public long Bytes;

            public Walk(long stopAbove)
            {
                this.stopAbove = stopAbove;
            }

            public bool Over => Bytes > stopAbove;

            // The globals table itself, so that _G does not walk all of it under one name.
            public void Own(LuaTable t)
            {
                seen.Add(t);
                Bytes += Size(t);
            }

            static long Size(LuaTable t) =>
                TableBytes + SlotBytes * (long)t.GetArraySpan().Length + EntryBytes * (long)t.HashMapCount;

            public void Value(LuaValue v)
            {
                switch (v.Type)
                {
                    case LuaValueType.String:
                        string s = v.Read<string>();
                        if (seen.Add(s))
                            Bytes += StringBytes + CharBytes * (long)s.Length;
                        break;
                    case LuaValueType.Table:
                        Push(v.Read<LuaTable>());
                        break;
                    case LuaValueType.Function:
                        Push(v.Read<LuaFunction>());
                        break;
                    case LuaValueType.UserData:
                        if (v.TryRead<ILuaUserData>(out var u))
                            Push(u);
                        break;
                }
            }

            void Push(object o)
            {
                if (o != null && seen.Add(o))
                    pending.Push(o);
            }

            public void Drain()
            {
                while (pending.Count > 0 && !Over)
                {
                    switch (pending.Pop())
                    {
                        case LuaTable t:
                            Bytes += Size(t);
                            Push(t.Metatable);
                            foreach (var kv in t)
                            {
                                Value(kv.Key);
                                Value(kv.Value);
                            }
                            break;
                        case LuaClosure c:
                            var ups = c.UpValues;
                            Bytes += FunctionBytes + UpvalueBytes * (long)ups.Length;
                            foreach (var up in ups)
                                Value(up.GetValue());
                            Push(c.Proto);
                            break;
                        case CSharpClosure c:
                            Bytes += FunctionBytes + SlotBytes * (long)c.UpValues.Length;
                            foreach (var v in c.UpValues)
                                Value(v);
                            break;
                        case Prototype p:
                            Bytes +=
                                TableBytes + CodeBytes * (long)p.Code.Length + SlotBytes * (long)p.Constants.Length;
                            foreach (var v in p.Constants)
                                Value(v);
                            foreach (var child in p.ChildPrototypes)
                                Push(child);
                            break;
                        case ILuaUserData u:
                            Bytes += TableBytes;
                            Push(u.Metatable);
                            foreach (var v in u.UserValues)
                                Value(v);
                            break;
                        case LuaFunction _:
                            Bytes += FunctionBytes;
                            break;
                    }
                }
            }
        }

        sealed class Identity : IEqualityComparer<object>
        {
            public static readonly Identity Instance = new Identity();

            public new bool Equals(object a, object b) => ReferenceEquals(a, b);

            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
    }
}
