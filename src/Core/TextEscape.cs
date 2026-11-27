using System;
using System.Text;

namespace ShorelandsMods.Core
{
    internal static class TextEscape
    {
        // Text for rich text (chat, tooltips): what TMP parses is replaced as the game's StringExtensions.EscapeTMPText
        // (0.69i) does: '<' '>' by look-alikes, a zero-width space after every '\'. Unlike the game, ' " & stay (TMP
        // reads them only inside a tag, and the fonts lack most look-alikes; a test compares both), and every line break,
        // including the line and paragraph separators TMP breaks at, becomes a space.
        public static string Tmp(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var sb = new StringBuilder(input.Length + 50);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                switch (c)
                {
                    case '<':
                        sb.Append('˂');
                        break;
                    case '>':
                        sb.Append('˃');
                        break;
                    case '\\':
                        sb.Append(c);
                        if (i + 1 >= input.Length || input[i + 1] != '​')
                            sb.Append('​');
                        break;
                    default:
                        sb.Append(Breaks(c) ? ' ' : c);
                        break;
                }
            }
            return sb.ToString();
        }

        static bool Breaks(char c) => char.IsControl(c) || c == '\u2028' || c == '\u2029';

        // Chat turns ":@N:" into an emote after escaping; a zero-width space after every ':' before '@' stops it.
        public static string Chat(string input)
        {
            string s = Tmp(input);
            return string.IsNullOrEmpty(s) ? s : s.Replace(":@", ":​@");
        }

        // Text for host labels, which draw with rich text and escape sequences off: only line breaks and control
        // characters go (to spaces), so every character shows as typed (Tmp's look-alikes are not in the game's font).
        public static string Plain(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
                sb.Append(Breaks(c) ? ' ' : c);
            return sb.ToString();
        }

        // One line of the BepInEx log: tabs kept, every other line break or control character a space, cut at max.
        // total: the length of the text input was cut from, if it was.
        public static string LogLine(string input, int max, long total = -1)
        {
            if (total < 0)
                total = input?.Length ?? 0;
            if (string.IsNullOrEmpty(input))
                return input;
            var sb = new StringBuilder(Math.Min(input.Length, max) + 40);
            for (int i = 0; i < input.Length && i < max; i++)
                sb.Append(input[i] == '\t' || !Breaks(input[i]) ? input[i] : ' ');
            if (total > max)
                sb.Append(" ... (cut: ").Append(total - max).Append(" more characters)");
            return sb.ToString();
        }

        public static string PlainLines(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var lines = input.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = Plain(lines[i]);
            return string.Join("\n", lines);
        }

        // The game's fonts have no "…".
        public static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }
}
