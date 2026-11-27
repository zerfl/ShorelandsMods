using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Lua;

namespace ShorelandsMods.Core
{
    // What the player sees when a mod fails: file, line and one plain sentence. Raw keeps Lua's own text for the log.
    internal sealed class ModError
    {
        public string File;
        public int Line;
        public string Sentence,
            Raw;

        public string Where =>
            File == null ? ""
            : Line > 0 ? File + " line " + Line
            : File;

        public override string ToString() => (File == null ? "" : Where + ": ") + Sentence;
    }

    internal static class ErrorText
    {
        static readonly Regex Located = new Regex(
            @"^(?:Lua-CSharp: )?(?<file>[^:\n]+\.lua):(?<line>\d+): (?<msg>.*)$",
            RegexOptions.Singleline
        );
        static readonly Regex CallGlobal = new Regex(@"^attempt to call a nil value \(global '(?<n>[^']+)'\)$");
        static readonly Regex CallOther = new Regex(
            @"^attempt to call an? (?<t>\w+) value(?: \((?<k>\w+) '(?<n>[^']+)'\))?$"
        );
        static readonly Regex Index = new Regex(
            @"^attempt to index an? (?<t>\w+) value(?: \((?<k>\w+) '(?<n>[^']+)'\))?$"
        );
        static readonly Regex Arith = new Regex(
            @"^attempt to (?<op>add|sub|mul|div|mod|pow|unm|concatenate|concat|perform arithmetic on) an? (?<t1>\w+) value(?: \((?<k1>\w+) '(?<n1>[^']+)'\))?(?: with an? (?<t2>\w+) value(?: \((?<k2>\w+) '(?<n2>[^']+)'\))?)?$"
        );
        static readonly Regex Compare = new Regex(
            @"^attempt to compare (?:two (?<t1>\w+) values|an? (?<t1>\w+) value with an? (?<t2>\w+) value|(?<t1>\w+) with (?<t2>\w+))$"
        );
        static readonly Regex BadArg = new Regex(@"^bad argument #(?<i>\d+) to '(?<f>[^']+)' \((?<why>.*)\)$");
        static readonly Regex Expected = new Regex(
            @"^'(?<w>[^']+)' expected(?: \(to close '(?<o>[^']+)' at line (?<l>\d+)\))? near (?<near>.+)$"
        );
        static readonly Regex Near = new Regex(@"^(?<what>.+?) near (?<near>.+)$");

        // dir: the mod's folder, to read the line of a syntax error for a hint.
        public static ModError FromException(Exception e, Budget budget, IEnumerable<string> known, string dir = null)
        {
            // A file loaded by dofile that does not compile arrives inside a runtime error.
            for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
                if (inner is LuaCompileException || inner is LuaParseException)
                    return FromException(inner, budget, known, dir);
            var err = new ModError { Raw = e.GetType().Name + ": " + e.Message };
            switch (e)
            {
                case ModProblem p:
                    err.Sentence = p.Message;
                    return err;
                case LuaCompileException c:
                    err.File = ChunkFile(c.ChunkName);
                    err.Line = c.Position.Line;
                    err.Sentence = TranslateSyntax(c.MessageWithNearToken, SourceLines(dir, err.File), err.Line);
                    return err;
                case LuaParseException pe:
                    err.File = ChunkFile(pe.ChunkName);
                    err.Line = pe.Position.Line;
                    err.Sentence = TranslateSyntax(TextAfterLocation(pe.Message), SourceLines(dir, err.File), err.Line);
                    return err;
                case BudgetExceeded b:
                    err.Sentence = BudgetSentence(b.What, budget);
                    if (b.Data["ms"] is double ms)
                        err.Raw += " after " + LuaText.Num(ms) + " ms";
                    LocateFromTraceback(b.InnerException as LuaCanceledException, err);
                    return err;
                case LuaRuntimeException r:
                    string msg = SafeMessage(r);
                    var m = Located.Match(msg);
                    if (m.Success)
                    {
                        err.File = m.Groups["file"].Value;
                        err.Line = int.Parse(m.Groups["line"].Value);
                        msg = m.Groups["msg"].Value;
                    }
                    err.Sentence = Translate(msg, known);
                    err.Raw = "LuaRuntimeException: " + SafeMessage(r);
                    return err;
                default:
                    err.Sentence = "the host failed (" + e.GetType().Name + "); the log has details";
                    err.Raw = e.ToString();
                    return err;
            }
        }

        public static string BudgetSentence(string what, Budget budget)
        {
            switch (what)
            {
                case "memory":
                    return "it used more than " + (Limits.MemoryGrowthBytes >> 20) + " MB of memory in one call";
                case "instructions":
                    return "it ran too long (more than "
                        + budget.Instructions.ToString("N0", CultureInfo.InvariantCulture)
                        + " steps in one call); an endless loop?";
                default:
                    return "it ran longer than "
                        + LuaText.Num(budget.Ms)
                        + " ms in one call; an endless loop or too much work at once?";
            }
        }

        // Lua's error text without the location, turned into a plain sentence where a pattern is known.
        public static string Translate(string msg, IEnumerable<string> known)
        {
            Match m;
            if ((m = CallGlobal.Match(msg)).Success)
            {
                string name = m.Groups["n"].Value;
                string near = Suggest(name, known);
                return "there is no function called " + name + (near != null ? ". Did you mean " + near + "?" : "");
            }
            if ((m = CallOther.Match(msg)).Success)
                return (m.Groups["n"].Success ? m.Groups["n"].Value : "the value")
                    + " is "
                    + Article(m.Groups["t"].Value)
                    + ", not a function";
            if ((m = Index.Match(msg)).Success)
                return m.Groups["n"].Success
                    ? m.Groups["n"].Value
                        + " is "
                        + Article(m.Groups["t"].Value)
                        + ", so it has no fields to read (is it set yet?)"
                    : "tried to read a field of " + Article(m.Groups["t"].Value);
            if ((m = Arith.Match(msg)).Success)
            {
                string op = m.Groups["op"].Value;
                bool join = op.StartsWith("concat");
                string culprit = Culprit(m, join ? "string" : "number");
                return (join ? "tried to join text, but " : "tried to " + Verb(op) + " numbers, but ") + culprit;
            }
            if ((m = Compare.Match(msg)).Success)
                return "tried to compare "
                    + Article(m.Groups["t1"].Value)
                    + " with "
                    + Article(m.Groups["t2"].Success ? m.Groups["t2"].Value : m.Groups["t1"].Value)
                    + " (< and > need two numbers or two texts)";
            if ((m = BadArg.Match(msg)).Success)
                return "argument "
                    + m.Groups["i"].Value
                    + " of "
                    + m.Groups["f"].Value
                    + " is wrong: "
                    + m.Groups["why"].Value;
            if (msg.Contains("stack overflow"))
                return "a function kept calling itself without end (stack overflow)";
            return msg;
        }

        // lines: the file, to add a hint for a slip from another language when the line shows it.
        public static string TranslateSyntax(string msg, IReadOnlyList<string> lines = null, int line = 0)
        {
            string sentence = SyntaxSentence(msg);
            string hint = lines == null ? null : SyntaxHint.For(msg, lines, line);
            return hint == null ? sentence : sentence + ". " + hint;
        }

        static string SyntaxSentence(string msg)
        {
            Match m;
            if (msg == "<eof> expected near 'end'")
                return "this end has nothing to close (one end too many?)";
            if ((m = Expected.Match(msg)).Success)
            {
                string w = m.Groups["w"].Value;
                return m.Groups["o"].Success
                    ? "'"
                        + w
                        + "' is missing (to close the '"
                        + m.Groups["o"].Value
                        + "' on line "
                        + m.Groups["l"].Value
                        + ")"
                    : "'" + w + "' is missing before " + NearText(m.Groups["near"].Value);
            }
            if (msg.StartsWith("unfinished string"))
                return "a text in quotes is not closed";
            if (msg.StartsWith("unfinished long string") || msg.StartsWith("unfinished long comment"))
                return "a [[ ]] block is not closed";
            if ((m = Near.Match(msg)).Success)
                return "this is not valid Lua: " + m.Groups["what"].Value + " near " + NearText(m.Groups["near"].Value);
            return "this is not valid Lua: " + msg;
        }

        // The closest known name, when it is close enough to be a typo.
        // Longer than any name a suggestion could be for.
        const int MaxSuggestChars = 100;

        public static string Suggest(string name, IEnumerable<string> known)
        {
            string best = null;
            int bestD = int.MaxValue;
            int allowed = Math.Max(2, name.Length / 4);
            if (name.Length > MaxSuggestChars)
                return null;
            string lower = name.ToLowerInvariant();
            foreach (var k in known)
            {
                // The distance is at least the difference in length, and computing it costs the product.
                if (Math.Abs(k.Length - name.Length) > allowed)
                    continue;
                int d = Distance(lower, k.ToLowerInvariant());
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            return bestD <= allowed ? best : null;
        }

        internal static int Distance(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(
                        Math.Min(cur[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)
                    );
                var t = prev;
                prev = cur;
                cur = t;
            }
            return prev[b.Length];
        }

        // "start is nil (is it set yet?)" for the operand that is not a number (or text, for joining).
        static string Culprit(Match m, string fine)
        {
            for (int i = 1; i <= 2; i++)
            {
                var t = m.Groups["t" + i];
                if (!t.Success || t.Value == fine || (fine == "string" && t.Value == "number"))
                    continue;
                string what = Article(t.Value) + (t.Value == "nil" ? " (is it set yet?)" : "");
                return m.Groups["n" + i].Success ? m.Groups["n" + i].Value + " is " + what : "one of them is " + what;
            }
            return "one of them has the wrong type";
        }

        static string Verb(string op)
        {
            switch (op)
            {
                case "add":
                    return "add";
                case "sub":
                    return "subtract";
                case "mul":
                    return "multiply";
                case "div":
                    return "divide";
                case "mod":
                    return "take the remainder";
                case "pow":
                    return "raise to a power";
                case "unm":
                    return "negate";
                default:
                    return "do arithmetic";
            }
        }

        static string Article(string type) =>
            type == "nil" ? "nil" : (type.StartsWith("a") || type.StartsWith("u") ? "an " : "a ") + type;

        static string NearText(string near) => near == "<eof>" ? "the end of the file" : near;

        static string ChunkFile(string chunk) => string.IsNullOrEmpty(chunk) ? null : chunk.TrimStart('@');

        static string[] SourceLines(string dir, string file)
        {
            if (dir == null || file == null)
                return null;
            string full = ModPaths.ResolveLua(dir, "", file, out _, out _);
            string text = full != null ? ModFiles.ReadText(dir, full, Limits.LuaFileBytes, out _) : null;
            return text?.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        static string TextAfterLocation(string msg)
        {
            var m = Located.Match(msg);
            return m.Success ? m.Groups["msg"].Value : msg;
        }

        // Lua-CSharp builds the message from its traceback, which can throw for a cancelled call.
        static string SafeMessage(LuaRuntimeException r)
        {
            try
            {
                return r.Message;
            }
            catch (Exception)
            {
                return r.ErrorObject.ToString();
            }
        }

        static void LocateFromTraceback(LuaCanceledException c, ModError err)
        {
            if (c == null)
                return;
            try
            {
                var tb = c.LuaTraceback?.ToString();
                if (tb == null)
                    return;
                var m = Regex.Match(tb, @"\t(?<file>[^:\n]+\.lua):(?<line>\d+):");
                if (m.Success)
                {
                    err.File = m.Groups["file"].Value;
                    err.Line = int.Parse(m.Groups["line"].Value);
                }
            }
            catch (Exception) { }
        }
    }
}
