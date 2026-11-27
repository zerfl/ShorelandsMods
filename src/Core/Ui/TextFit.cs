using System.Collections.Generic;
using System.Text;

namespace ShorelandsMods.Core.Ui
{
    // Line breaking for host labels, so every label is laid out from widths measured in the font in use: text breaks
    // at "\n" and at spaces, a word wider than the line breaks between characters, and text past the last allowed
    // line ends the last line with "…". Lines are drawn without the label's own wrapping.
    internal static class TextFit
    {
        // Three full stops: the game's pixel font has no "…".
        public const string Ellipsis = "...";

        public static List<string> Wrap(string text, int maxWidth, int maxLines, Measure measure)
        {
            var lines = new List<string>();
            bool cut = false;
            foreach (var para in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (lines.Count >= maxLines)
                {
                    cut = true;
                    break;
                }
                if (!WrapParagraph(para, maxWidth, maxLines, measure, lines))
                {
                    cut = true;
                    break;
                }
            }
            if (cut && lines.Count > 0)
                lines[lines.Count - 1] = Clip(lines[lines.Count - 1] + " " + Ellipsis, maxWidth, measure, true);
            return lines;
        }

        // False if the paragraph did not fit in maxLines.
        static bool WrapParagraph(string para, int maxWidth, int maxLines, Measure measure, List<string> lines)
        {
            if (measure(para) <= maxWidth)
            {
                if (lines.Count >= maxLines)
                    return false;
                lines.Add(para);
                return true;
            }
            var line = new StringBuilder();
            foreach (var word in para.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (measure(candidate) <= maxWidth)
                {
                    line.Clear().Append(candidate);
                    continue;
                }
                if (line.Length > 0)
                {
                    if (lines.Count >= maxLines)
                        return false;
                    lines.Add(line.ToString());
                    line.Clear();
                }
                string rest = word;
                while (measure(rest) > maxWidth)
                {
                    int n = FitChars(rest, maxWidth, measure);
                    if (lines.Count >= maxLines)
                        return false;
                    lines.Add(rest.Substring(0, n));
                    rest = rest.Substring(n);
                }
                line.Append(rest);
            }
            if (lines.Count >= maxLines)
                return false;
            lines.Add(line.ToString());
            return true;
        }

        static int FitChars(string s, int maxWidth, Measure measure)
        {
            int lo = 1,
                hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (measure(s.Substring(0, mid)) <= maxWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        // One line: the text if it fits, else as much as fits followed by "…".
        public static string Clip(string text, int maxWidth, Measure measure, bool forceEllipsis = false)
        {
            text = text ?? "";
            if (!forceEllipsis && measure(text) <= maxWidth)
                return text;
            string body =
                forceEllipsis && text.EndsWith(" " + Ellipsis)
                    ? text.Substring(0, text.Length - Ellipsis.Length - 1)
                    : text;
            if (measure(body + Ellipsis) <= maxWidth)
                return body + Ellipsis;
            int lo = 0,
                hi = body.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (measure(body.Substring(0, mid).TrimEnd() + Ellipsis) <= maxWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return body.Substring(0, lo).TrimEnd() + Ellipsis;
        }

        public static int Widest(IList<string> lines, Measure measure)
        {
            int w = 0;
            foreach (var l in lines)
                w = System.Math.Max(w, measure(l));
            return w;
        }
    }
}
