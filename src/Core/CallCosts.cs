using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShorelandsMods.Core
{
    // The most of its budget one call of each kind used (time the watchdog charged, or steps, whichever is the larger
    // share), from the figures the runtime keeps for its limits. For the budget notice and the slowest-calls line.
    internal sealed class CallCosts
    {
        public const double NoticeShare = 0.5;

        // Cheaper calls are not looked up: under 0.1 ms neither limit can be half used.
        public const double FloorMs = 0.1;
        public const int Shown = 3;

        sealed class Entry
        {
            public string What;
            public double Ms,
                Share;
            public long Steps;
            public Budget Budget;
            public bool Noticed;
        }

        readonly Dictionary<string, Entry> byWhat = new Dictionary<string, Entry>();

        public bool Any => byWhat.Count > 0;

        // A call that finished. The notice for the first call of its kind over half its budget, else null.
        public string Add(string what, double ms, long steps, Budget budget)
        {
            if (ms < FloorMs)
                return null;
            double share = Math.Max(ms / budget.Ms, (double)steps / budget.Instructions);
            if (!byWhat.TryGetValue(what, out var e))
                byWhat[what] = e = new Entry { What = what };
            if (share > e.Share)
            {
                e.Share = share;
                e.Ms = ms;
                e.Steps = steps;
                e.Budget = budget;
            }
            if (share <= NoticeShare || e.Noticed)
                return null;
            e.Noticed = true;
            return what + " used " + Used(ms, steps, budget) + "; a call over its budget switches the mod off.";
        }

        // "Slowest calls: OnInventoryChanged 12 of 20 ms, ...", the largest shares first; null for a mod without Lua.
        public static string Line(Mod mod) => mod.Lua != null || mod.Costs.Any ? mod.Costs.Line() : null;

        public string Line()
        {
            if (byWhat.Count == 0)
                return "Slowest calls: all under " + Num(FloorMs) + " ms.";
            return "Slowest calls: "
                + string.Join(
                    ", ",
                    byWhat
                        .Values.OrderByDescending(e => e.Share)
                        .Take(Shown)
                        .Select(e => e.What + " " + Used(e.Ms, e.Steps, e.Budget))
                )
                + ".";
        }

        static string Used(double ms, long steps, Budget b) =>
            (double)steps / b.Instructions > ms / b.Ms
                ? steps.ToString("N0", CultureInfo.InvariantCulture)
                    + " of "
                    + b.Instructions.ToString("N0", CultureInfo.InvariantCulture)
                    + " steps"
                : Num(ms) + " of " + Num(b.Ms) + " ms";

        internal static string Num(double ms) => ms.ToString(ms < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
    }
}
