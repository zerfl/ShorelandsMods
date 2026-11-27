using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ShorelandsMods.Core.Ui
{
    // A timestamp format in os.date's notation: %H %M %S %I %p %d %m %y %Y and %%; between the codes only Literal
    // characters, printed as they are. The stamp goes into the chat's rich text, so nothing else may reach it.
    internal sealed class TimestampFormat
    {
        public const int MaxLength = 24;
        public const string Default = "%H:%M";
        public const string Punctuation = " :.-/,()[]|";
        readonly string format;

        TimestampFormat(string format)
        {
            this.format = format;
        }

        public string Source => format;

        // Null and a sentence for the author when the format cannot be used.
        public static TimestampFormat Parse(string format, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(format))
            {
                problem = "is empty; use nil to switch timestamps off, or a format like \"" + Default + "\"";
                return null;
            }
            if (format.Length > MaxLength)
            {
                problem = "is longer than " + MaxLength + " characters";
                return null;
            }
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    if (!Literal(c))
                    {
                        problem =
                            "uses "
                            + (char.IsControl(c) ? "a control character" : "\"" + c + "\"")
                            + "; between the codes it may use letters, digits, spaces and "
                            + Punctuation.Trim();
                        return null;
                    }
                    continue;
                }
                if (i + 1 >= format.Length)
                {
                    problem = "ends with a single %; write %% for a percent sign";
                    return null;
                }
                char k = format[++i];
                if ("HMSIpdmyY%".IndexOf(k) < 0)
                {
                    problem =
                        "uses %"
                        + k
                        + ", which is not supported; use %H (hour), %M (minute), %S (second), %I and %p (12 hour clock), %d %m %y %Y (date)";
                    return null;
                }
            }
            return new TimestampFormat(format);
        }

        static bool Literal(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || Punctuation.IndexOf(c) >= 0;

        public string Format(DateTime t)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    sb.Append(c);
                    continue;
                }
                switch (format[++i])
                {
                    case 'H':
                        sb.Append(t.Hour.ToString("00"));
                        break;
                    case 'M':
                        sb.Append(t.Minute.ToString("00"));
                        break;
                    case 'S':
                        sb.Append(t.Second.ToString("00"));
                        break;
                    case 'I':
                        sb.Append((t.Hour % 12 == 0 ? 12 : t.Hour % 12).ToString("00"));
                        break;
                    case 'p':
                        sb.Append(t.Hour < 12 ? "AM" : "PM");
                        break;
                    case 'd':
                        sb.Append(t.Day.ToString("00"));
                        break;
                    case 'm':
                        sb.Append(t.Month.ToString("00"));
                        break;
                    case 'y':
                        sb.Append((t.Year % 100).ToString("00"));
                        break;
                    case 'Y':
                        sb.Append(t.Year.ToString(CultureInfo.InvariantCulture));
                        break;
                    default:
                        sb.Append('%');
                        break;
                }
            }
            return sb.ToString();
        }
    }

    internal readonly struct Highlight
    {
        // As drawn: escaped the way chat text is, so it matches what the line shows.
        public readonly string Word;
        public readonly string Hex;

        public Highlight(string word, string hex)
        {
            Word = word;
            Hex = hex;
        }
    }

    // Decorates one formatted chat line (TMP rich text as the game builds it): a timestamp before it and colour around
    // highlighted words. Only text outside tags is matched, and not the host's own lines (its colours), so no mod
    // recolours a mod's line or a host note; a word matches whole (letters, digits and _ around it end it) and in any
    // case. The result is always valid rich text when the input was: every added tag is closed inside the same run of
    // text.
    internal static class ChatDecorator
    {
        public const string TimestampColor = "#A0A0A0";

        static readonly string[] HostSpans =
        {
            "<color=" + ModHost.Color.Note + ">",
            "<color=" + ModHost.Color.Warn + ">",
            "<color=" + ModHost.Color.Error + ">",
            "<color=" + ModHost.Color.Mod + ">",
        };

        public static string Decorate(string line, string stamp, IReadOnlyList<Highlight> highlights)
        {
            if (line == null)
                return null;
            string body = highlights == null || highlights.Count == 0 ? line : Mark(line, highlights);
            return stamp == null ? body : "<color=" + TimestampColor + ">" + stamp + "</color> " + body;
        }

        static string Mark(string line, IReadOnlyList<Highlight> highlights)
        {
            StringBuilder sb = null;
            int copied = 0;
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '<')
                {
                    int close = line.IndexOf('>', i + 1);
                    if (close < 0)
                        break;
                    if (Array.IndexOf(HostSpans, line.Substring(i, close + 1 - i)) >= 0)
                    {
                        // Host text is escaped, so its span holds no </color> before its own (only GamePrintLink's links).
                        int end = line.IndexOf("</color>", close, StringComparison.Ordinal);
                        if (end < 0)
                            break;
                        close = end + "</color>".Length - 1;
                    }
                    i = close + 1;
                    continue;
                }
                if (i > 0 && IsWordChar(line[i - 1]) && IsWordChar(line[i]))
                {
                    i++;
                    continue;
                }
                int best = -1,
                    bestLen = 0;
                for (int h = 0; h < highlights.Count; h++)
                {
                    string w = highlights[h].Word;
                    if (w.Length > bestLen && MatchesAt(line, i, w))
                    {
                        best = h;
                        bestLen = w.Length;
                    }
                }
                if (best < 0)
                {
                    i++;
                    continue;
                }
                sb = sb ?? new StringBuilder(line.Length + 32);
                sb.Append(line, copied, i - copied)
                    .Append("<color=")
                    .Append(highlights[best].Hex)
                    .Append('>')
                    .Append(line, i, bestLen)
                    .Append("</color>");
                i += bestLen;
                copied = i;
            }
            if (sb == null)
                return line;
            sb.Append(line, copied, line.Length - copied);
            return sb.ToString();
        }

        // Whole word at i, in any case, not running into a tag.
        static bool MatchesAt(string line, int i, string w)
        {
            if (i + w.Length > line.Length)
                return false;
            for (int k = 0; k < w.Length; k++)
            {
                char c = line[i + k];
                if (c == '<' || char.ToLowerInvariant(c) != char.ToLowerInvariant(w[k]))
                    return false;
            }
            int end = i + w.Length;
            return end >= line.Length || !IsWordChar(line[end]) || !IsWordChar(w[w.Length - 1]);
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
