using System;
using System.Diagnostics;

namespace ShorelandsMods.Core
{
    // What the time budgets read: Stopwatch ticks and the collection count (Watchdog forgives a collection's pause).
    // The offline tests swap in a clock they move, so the budget rules run without depending on the machine's speed.
    internal static class HostClock
    {
        public static Func<long> Ticks = Stopwatch.GetTimestamp;
        public static Func<int> Collections = () => GC.CollectionCount(0);

        public static long Now => Ticks();

        public static double MsSince(long ticks) => (Ticks() - ticks) * 1000.0 / Stopwatch.Frequency;

        public static long FromMs(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    }
}
