using System;
using System.Globalization;
using System.Text;
using Lua;

namespace ShorelandsMods.Core
{
    // One call of an API function from Lua: reads and checks the arguments (errors name the function and the
    // argument), and returns values. Arguments are numbered from 1 as the author counts them.
    internal readonly struct ApiCall
    {
        public readonly Mod Mod;
        public readonly ApiFunction Function;
        readonly LuaFunctionExecutionContext ctx;

        public ApiCall(Mod mod, ApiFunction function, LuaFunctionExecutionContext ctx)
        {
            Mod = mod;
            Function = function;
            this.ctx = ctx;
        }

        public int Count => ctx.ArgumentCount;

        public LuaValue Arg(int n) => n <= ctx.ArgumentCount ? ctx.Arguments[n - 1] : LuaValue.Nil;

        public bool Has(int n) => Arg(n).Type != LuaValueType.Nil;

        public LuaRuntimeException Error(string sentence) =>
            new LuaRuntimeException(ctx.State, Function.Name + ": " + sentence);

        public LuaRuntimeException Wrong(int n, string name, string expected) =>
            Error("argument " + n + " (" + name + ") must be " + expected + ", but it is " + LuaText.Describe(Arg(n)));

        public string String(int n, string name)
        {
            if (Arg(n).TryRead<string>(out var s))
                return s;
            throw Wrong(n, name, "text");
        }

        // Text, or a number or boolean written as text (what print would show).
        public string Text(int n, string name)
        {
            var v = Arg(n);
            if (v.TryRead<string>(out var s))
                return s;
            if (v.Type == LuaValueType.Number || v.Type == LuaValueType.Boolean)
                return v.ToString();
            throw Wrong(n, name, "text");
        }

        public double Number(int n, string name)
        {
            if (Arg(n).TryRead<double>(out var d) && !double.IsNaN(d))
                return d;
            throw Wrong(n, name, "a number");
        }

        // A value that does not fit (out of range, unknown name, too long) is not the author's mistake but usually the
        // player's (a text setting), so it never stops the mod: the function skips it or clamps it and returns false
        // or nil, and the player gets one chat notice per mod, function and argument while the mod runs.
        public bool Refuse(int n, string sentence) => Refuse(n.ToString(CultureInfo.InvariantCulture), sentence);

        // what: the argument or option the notice is about (one notice each).
        public bool Refuse(string what, string sentence)
        {
            Mod.CallRefused = true;
            if (Mod.Get<Refused>().Add(Function.Name + "#" + what))
            {
                Mod.Host.Log(Mod, LogLevel.Warning, Function.Name + ": " + sentence);
                Mod.Host.Notice(Mod, Function.Name + ": " + sentence + ".");
            }
            return false;
        }

        // Whether the player was told about what already (Refuse says it once while the mod runs).
        public bool Told(string what) => Mod.Get<Refused>().Contains(Function.Name + "#" + what);

        sealed class Refused : System.Collections.Generic.HashSet<string> { }

        // A number clamped into min..max; outside it counts as refused.
        public double Clamp(int n, string name, double min, double max)
        {
            double d = Number(n, name);
            if (d >= min && d <= max)
                return d;
            double c = d < min ? min : max;
            Refuse(
                n,
                name
                    + " must be from "
                    + LuaText.Num(min)
                    + " to "
                    + LuaText.Num(max)
                    + "; "
                    + LuaText.Num(d)
                    + " was changed to "
                    + LuaText.Num(c)
            );
            return c;
        }

        public double OptClamp(int n, string name, double fallback, double min, double max) =>
            Has(n) ? Clamp(n, name, min, max) : fallback;

        // An id or count that must be whole; null for any other number (no such id).
        public int? Whole(int n, string name)
        {
            double d = Number(n, name);
            return d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : (int?)null;
        }

        public bool Boolean(int n, string name)
        {
            if (Arg(n).TryRead<bool>(out var b))
                return b;
            throw Wrong(n, name, "true or false");
        }

        public LuaFunction Func(int n, string name)
        {
            if (Arg(n).TryRead<LuaFunction>(out var f))
                return f;
            throw Wrong(n, name, "a function");
        }

        public LuaTable Table(int n, string name)
        {
            if (Arg(n).TryRead<LuaTable>(out var t))
                return t;
            throw Wrong(n, name, "a table");
        }

        // "#rrggbb", "#rrggbbaa" or a table { r, g, b [, a] } with 0..1 numbers (what ColorFromHex returns). Null
        // (refused) for a text that is not such a colour.
        public Rgba? Color(int n, string name)
        {
            var v = Arg(n);
            if (v.TryRead<string>(out var hex))
            {
                if (Rgba.TryParseHex(hex, out var c))
                    return c;
                Refuse(n, name + " must be a colour like \"#ffcc00\", not \"" + TextEscape.Clip(hex, 40) + "\"");
                return null;
            }
            if (Rgba.TryRead(v, out var t))
                return t;
            throw Wrong(n, name, "a colour (\"#rrggbb\" or a table from ColorFromHex)");
        }

        // true, or false when a value was refused or clamped in this call.
        public int ReturnOk() => ctx.Return(!Mod.CallRefused);

        public int Return() => ctx.Return();

        public int Return(LuaValue v) => ctx.Return(v);

        public int Return(LuaValue a, LuaValue b) => ctx.Return(a, b);
    }

    internal static class LuaText
    {
        public static string Describe(LuaValue v)
        {
            switch (v.Type)
            {
                case LuaValueType.Nil:
                    return "nil";
                case LuaValueType.String:
                    return "the text \"" + TextEscape.Clip(v.Read<string>(), 40) + "\"";
                case LuaValueType.Number:
                    return "the number " + Num(v.Read<double>());
                case LuaValueType.Boolean:
                    return v.ToString();
                default:
                    return "a " + v.TypeToString();
            }
        }

        public static LuaValue OrNil(string s) => s == null ? LuaValue.Nil : new LuaValue(s);

        // " (init.lua:264)": where a Lua function was written, for naming it to the author.
        public static string Where(LuaFunction f) =>
            f is Lua.Runtime.LuaClosure c
                ? " (" + c.Proto.ChunkName.TrimStart('@') + ":" + c.Proto.LineDefined + ")"
                : "";

        public static string Num(double d) =>
            double.IsNaN(d) ? "nan"
            : double.IsInfinity(d) ? (d > 0 ? "inf" : "-inf")
            : d.ToString("0.###", CultureInfo.InvariantCulture);

        public static string Join(ReadOnlySpan<LuaValue> values) => Join(values, int.MaxValue, out _);

        // Joined with tabs, but only the first max + 1 characters are built; length is what the whole would have.
        public static string Join(ReadOnlySpan<LuaValue> values, int max, out long length)
        {
            var sb = new StringBuilder();
            length = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    length++;
                    if (sb.Length <= max)
                        sb.Append('\t');
                }
                string s = values[i].ToString();
                length += s.Length;
                if (sb.Length <= max)
                    sb.Append(s, 0, (int)Math.Min(s.Length, max + 1L - sb.Length));
            }
            return sb.ToString();
        }
    }

    internal readonly struct Rgba
    {
        public readonly double R,
            G,
            B,
            A;

        public Rgba(double r, double g, double b, double a)
        {
            R = Clamp(r);
            G = Clamp(g);
            B = Clamp(b);
            A = Clamp(a);
        }

        public static bool TryRead(LuaValue v, out Rgba c)
        {
            c = default;
            if (
                !v.TryRead<LuaTable>(out var t)
                || !t["r"].TryRead<double>(out var r)
                || !t["g"].TryRead<double>(out var g)
                || !t["b"].TryRead<double>(out var b)
            )
                return false;
            c = new Rgba(r, g, b, t["a"].TryRead<double>(out var a) ? a : 1);
            return true;
        }

        static double Clamp(double v) =>
            v < 0 ? 0
            : v > 1 ? 1
            : v;

        public static bool TryParseHex(string s, out Rgba c)
        {
            c = default;
            if (s == null || !s.StartsWith("#") || (s.Length != 7 && s.Length != 9))
                return false;
            var bytes = new double[4] { 0, 0, 0, 255 };
            for (int i = 0; i < (s.Length - 1) / 2; i++)
            {
                int hi = Hex(s[1 + i * 2]),
                    lo = Hex(s[2 + i * 2]);
                if (hi < 0 || lo < 0)
                    return false;
                bytes[i] = hi * 16 + lo;
            }
            c = new Rgba(bytes[0] / 255, bytes[1] / 255, bytes[2] / 255, bytes[3] / 255);
            return true;
        }

        static int Hex(char ch) =>
            ch >= '0' && ch <= '9' ? ch - '0'
            : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10
            : ch >= 'A' && ch <= 'F' ? ch - 'A' + 10
            : -1;
    }
}
