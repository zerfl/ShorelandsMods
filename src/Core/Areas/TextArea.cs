using System;
using System.Globalization;
using Lua;

namespace ShorelandsMods.Core.Areas
{
    internal static class TextFormat
    {
        // 1234567.891 -> "1,234,567.89" with decimals 2; rounds half away from zero.
        public static string Number(double n, int decimals)
        {
            if (double.IsNaN(n) || double.IsInfinity(n))
                return n.ToString(CultureInfo.InvariantCulture);
            n = Math.Round(n, decimals, MidpointRounding.AwayFromZero);
            return n.ToString(
                "#,0" + (decimals > 0 ? "." + new string('0', decimals) : ""),
                CultureInfo.InvariantCulture
            );
        }

        // "clock": 252 -> "4:12", 4000 -> "1:06:40"; "long": 45 -> "45 s", 252 -> "4 min", 4000 -> "1 h 06 min".
        // Whole seconds, rounded down; a negative duration gets a minus sign.
        public static string Duration(double seconds, bool clock)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                return seconds.ToString(CultureInfo.InvariantCulture);
            string sign = seconds < 0 ? "-" : "";
            long s = (long)Math.Floor(Math.Abs(seconds));
            long h = s / 3600,
                m = s / 60 % 60,
                sec = s % 60;
            if (clock)
                return sign
                    + (
                        h > 0
                            ? h
                                + ":"
                                + m.ToString("00", CultureInfo.InvariantCulture)
                                + ":"
                                + sec.ToString("00", CultureInfo.InvariantCulture)
                            : m + ":" + sec.ToString("00", CultureInfo.InvariantCulture)
                    );
            return sign
                + (
                    h > 0 ? h + " h " + m.ToString("00", CultureInfo.InvariantCulture) + " min"
                    : m > 0 ? m + " min"
                    : sec + " s"
                );
        }
    }

    internal sealed class TextArea : Area
    {
        public override string Name => "Text";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "TextNumber( number:number, [decimals:number] ) -> text:string",
                "the number with commas between thousands: 1234567 -> \"1,234,567\"; decimals 0 to 6 (default 0) "
                    + "digits after the point, rounded half away from zero",
                c =>
                {
                    double n = c.Number(1, "number");
                    int decimals = (int)c.OptClamp(2, "decimals", 0, 0, 6);
                    return c.Return(TextFormat.Number(n, decimals));
                }
            );
            s.Function(
                "TextDuration( seconds:number, [style:string] ) -> text:string",
                "a duration in whole seconds: style \"clock\" (default) 252 -> \"4:12\", 4000 -> \"1:06:40\"; \"long\" "
                    + "45 -> \"45 s\", 252 -> \"4 min\", 4000 -> \"1 h 06 min\". Another style gives \"clock\"",
                c =>
                {
                    double sec = c.Number(1, "seconds");
                    string style = c.Has(2) ? c.String(2, "style") : "clock";
                    if (style != "clock" && style != "long")
                        c.Refuse(
                            2,
                            "the style \"" + TextEscape.Clip(style, 40) + "\" is not known; \"clock\" was used"
                        );
                    return c.Return(TextFormat.Duration(sec, style != "long"));
                }
            );
        }
    }
}
