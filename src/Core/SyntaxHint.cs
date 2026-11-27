using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ShorelandsMods.Core
{
    // What to write instead, for a syntax error that comes from another language's habit (Noita players know Lua,
    // others JavaScript or Python). Only when Lua's message and the line it points at both show the slip.
    internal static class SyntaxHint
    {
        static readonly Regex Near = new Regex(@" near (?<near>.+)$", RegexOptions.Singleline);
        static readonly Regex Closes = new Regex(@"^'end' expected(?: \(to close '\w+' at line (?<l>\d+)\))?");
        static readonly Regex Compound = new Regex(
            @"(?<lhs>[\w.\[\]""']+)\s*(?<op>\+|-|\*|/|\.\.)=(?!=)\s*(?<rhs>.*)$"
        );
        static readonly Regex Increment = new Regex(@"(?<lhs>[\w.\[\]""']+)\s*\+\+");
        static readonly Regex ElseIf = new Regex(@"\belse\s+if\b");
        static readonly Regex Elif = new Regex(@"^\s*elif\b");

        public static string For(string msg, IReadOnlyList<string> lines, int line)
        {
            var m = Near.Match(msg);
            string near = m.Success ? m.Groups["near"].Value.Trim('\'') : "";
            string code = line >= 1 && line <= lines.Count ? Code(lines[line - 1]) : "";
            switch (near)
            {
                case "#" when code.Contains("#"):
                    return "Comments start with --, not #.";
                case "/" when code.Contains("//"):
                    return "Comments start with --, not //.";
                case "*" when code.Contains("/*"):
                    return "Comments start with --, not /* (a long one is --[[ ... ]]).";
                case "!":
                    return code.Contains("!=") ? "Not equal is ~= in Lua, not !=." : "Lua writes not x, not !x.";
                case "&" when code.Contains("&&"):
                    return "Lua writes and, not &&.";
                case "|" when code.Contains("||"):
                    return "Lua writes or, not ||.";
                case "{" when code.TrimEnd().EndsWith("{"):
                case "}" when code.TrimStart().StartsWith("}"):
                    return "Lua has no { } around blocks: write if x then ... end, function f() ... end.";
            }
            if (near.StartsWith("=") && code.Contains("==="))
                return "Equal is == in Lua, not ===.";
            if (Elif.IsMatch(code))
                return "Lua writes elseif, not elif.";
            if ((m = Increment.Match(code)).Success && near.StartsWith("+"))
                return "Lua has no ++: write " + m.Groups["lhs"].Value + " = " + m.Groups["lhs"].Value + " + 1.";
            if ((m = Compound.Match(code)).Success && (near.StartsWith(m.Groups["op"].Value)))
            {
                string lhs = m.Groups["lhs"].Value,
                    op = m.Groups["op"].Value,
                    rhs = Regex.Split(m.Groups["rhs"].Value, "//|#")[0].Trim();
                if (rhs.Length == 0 || rhs.Length > 30)
                    rhs = "the value";
                return "Lua has no " + op + "=: write " + lhs + " = " + lhs + " " + op + " " + rhs + ".";
            }
            if ((m = Closes.Match(msg)).Success)
            {
                int from = m.Groups["l"].Success ? int.Parse(m.Groups["l"].Value) : line;
                for (int i = Math.Max(1, from); i <= Math.Min(line, lines.Count); i++)
                    if (ElseIf.IsMatch(Code(lines[i - 1])))
                        return "The else if on line "
                            + i
                            + " starts a new if that needs its own end; write elseif as one word.";
                return "Every function, if, for and while needs its own end.";
            }
            if (msg.StartsWith("'then' expected"))
                return "Write then after the condition: if x > 1 then ... end.";
            if (msg.StartsWith("'do' expected"))
                return "Write do after for ... and while ...: for i = 1, 3 do ... end.";
            return null;
        }

        // The line without a -- comment (a -- inside quotes is rare enough to ignore).
        static string Code(string line)
        {
            int c = line.IndexOf("--", StringComparison.Ordinal);
            return c >= 0 ? line.Substring(0, c) : line;
        }
    }
}
