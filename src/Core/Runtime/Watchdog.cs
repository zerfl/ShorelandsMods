using System;
using System.Threading;

namespace ShorelandsMods.Core
{
    // One background thread cancels the running call's token once the call is over its time budget. Calls never
    // overlap (all Lua runs on the main thread), so one slot is enough.
    internal static class Watchdog
    {
        static readonly object Gate = new object();
        static Thread thread;
        static CancellationTokenSource target;
        static long start,
            budgetTicks;
        static int gcCount,
            graceLeft;
        static long creditLeft;
        static bool inHost;

        public static int Fired { get; private set; }

        public static void Start()
        {
            lock (Gate)
            {
                if (thread != null)
                    return;
                thread = new Thread(Loop) { IsBackground = true, Name = "ShorelandsMods watchdog" };
                thread.Start();
            }
        }

        public static void Arm(CancellationTokenSource cts, double ms)
        {
            lock (Gate)
            {
                target = cts;
                start = HostClock.Now;
                budgetTicks = HostClock.FromMs(ms);
                gcCount = HostClock.Collections();
                graceLeft = Limits.GcGraceRounds;
                creditLeft = HostClock.FromMs(Limits.HostCreditMs);
                inHost = false;
            }
        }

        // Time inside host functions (a 2,000-row list, the koi table) is the host's work, not the script's: it is
        // not charged, up to HostCreditMs per call, so a loop of host calls still stops.
        public static void HostEnter()
        {
            lock (Gate)
                inHost = true;
        }

        public static void HostLeave(long elapsedTicks)
        {
            lock (Gate)
            {
                inHost = false;
                long credit = Math.Min(elapsedTicks, creditLeft);
                creditLeft -= credit;
                start += credit;
            }
        }

        // The time charged to the call: without host credit or a collection pause the watchdog forgave.
        public static double Disarm()
        {
            lock (Gate)
            {
                target = null;
                return HostClock.MsSince(start);
            }
        }

        static void Loop()
        {
            while (true)
            {
                Thread.Sleep(1);
                Poll();
            }
        }

        // One look at the armed call: cancels it once it is over its budget.
        internal static void Poll()
        {
            lock (Gate)
            {
                if (target == null || HostClock.Now - start <= budgetTicks)
                    return;
                if (inHost && HostClock.Now - start <= budgetTicks + creditLeft)
                    return;
                // A collection stops every thread; do not charge the pause to the script.
                int gc = HostClock.Collections();
                if (gc != gcCount && graceLeft > 0)
                {
                    gcCount = gc;
                    graceLeft--;
                    start = HostClock.Now;
                    return;
                }
                try
                {
                    target.Cancel();
                }
                catch (ObjectDisposedException) { }
                target = null;
                Fired++;
            }
        }
    }
}
