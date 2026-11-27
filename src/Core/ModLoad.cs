using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShorelandsMods.Core
{
    // What one mod costs over time: its time in this frame and in the last Limits.WindowSeconds, by what it was
    // doing, and the Lua data it keeps (LuaData). The host's rules on it are in ModHost (Judge, Measure).
    internal sealed class ModLoad
    {
        const int Slots = 20;
        const double SlotSeconds = Limits.WindowSeconds / Slots;

        readonly double[] slotMs = new double[Slots];
        readonly long[] slotIds = new long[Slots];
        long frame = -1;
        double frameMs;

        sealed class Share
        {
            public double Ms,
                At;
        }

        // Decayed over the window, to name what the mod was doing most.
        readonly Dictionary<string, Share> shares = new Dictionary<string, Share>();

        public bool TimeNoticed,
            KeptNoticed;

        // The most it used in one window since it loaded.
        public double PeakMs { get; private set; }

        public long Kept { get; private set; } = -1;
        public string KeptMostly { get; private set; }
        double lastMeasure = double.NegativeInfinity;
        long grownSinceMeasure;
        bool ranSinceMeasure,
            collectedSinceMeasure;

        // group: "its timers", a callback's name, or the host's work for it ("drawing its HUD and windows").
        public void Add(string group, double ms, double now, long frameNo)
        {
            if (frameNo != frame)
            {
                frame = frameNo;
                frameMs = 0;
            }
            frameMs += ms;
            long id = (long)Math.Floor(now / SlotSeconds);
            int slot = (int)(((id % Slots) + Slots) % Slots);
            if (slotIds[slot] != id)
            {
                slotIds[slot] = id;
                slotMs[slot] = 0;
            }
            slotMs[slot] += ms;
            if (!shares.TryGetValue(group, out var s))
                shares[group] = s = new Share { At = now };
            s.Ms = s.Ms * Decay(now - s.At) + ms;
            s.At = now;
            double w = WindowMs(now);
            if (w > PeakMs)
                PeakMs = w;
        }

        public double FrameMs(long frameNo) => frameNo == frame ? frameMs : 0;

        public static double MsSince(long timestamp) => HostClock.MsSince(timestamp);

        public double WindowMs(double now)
        {
            long id = (long)Math.Floor(now / SlotSeconds);
            double sum = 0;
            for (int i = 0; i < Slots; i++)
                if (slotIds[i] > id - Slots && slotIds[i] <= id)
                    sum += slotMs[i];
            return sum;
        }

        static double Decay(double seconds) => Math.Exp(-Math.Max(0, seconds) / Limits.WindowSeconds);

        public string Mostly(double now)
        {
            string best = null;
            double most = -1;
            foreach (var kv in shares)
            {
                double ms = kv.Value.Ms * Decay(now - kv.Value.At);
                if (ms > most)
                {
                    most = ms;
                    best = kv.Key;
                }
            }
            return best;
        }

        // The calls' growth of the managed heap, and whether a collection ran in one (which hides growth).
        public void Grew(long bytes, bool collected)
        {
            ranSinceMeasure = true;
            if (bytes > 0)
                grownSinceMeasure += bytes;
            collectedSinceMeasure |= collected;
        }

        // Only Lua changes what it keeps: no call since the last measure, no new one.
        public bool MeasureDue(double now) =>
            ranSinceMeasure
            && (
                now - lastMeasure >= Limits.KeptCheckSeconds
                || grownSinceMeasure >= Limits.KeptGrowthBytes
                || (collectedSinceMeasure && now - lastMeasure >= Limits.KeptMinSeconds)
            );

        public void Measured(long bytes, string mostly, double now)
        {
            Kept = bytes;
            KeptMostly = mostly;
            lastMeasure = now;
            grownSinceMeasure = 0;
            ranSinceMeasure = collectedSinceMeasure = false;
        }

        // ---- what the player reads ----

        public string TimeNotice(double now) =>
            "used "
            + Ms(WindowMs(now))
            + " of "
            + Ms(Limits.WindowMs)
            + " ms in "
            + Seconds
            + ", most of it in "
            + Mostly(now)
            + "; a mod over that is switched off.";

        public string TimeStop(double now) =>
            "it used more than "
            + Ms(Limits.WindowMs)
            + " ms in "
            + Seconds
            + ", most of it in "
            + Mostly(now)
            + "; too much work too often?";

        public string KeptNotice() =>
            "keeps about "
            + Mb(Kept)
            + " of "
            + Mb(Limits.KeptBytes)
            + " MB of Lua data between calls, most of it in "
            + KeptMostly
            + "; a mod over that is switched off.";

        public string KeptStop() =>
            "it keeps more than "
            + Mb(Limits.KeptBytes)
            + " MB of Lua data between calls, most of it in "
            + KeptMostly
            + "; is a table growing without end?";

        // For /mods <id>: "Load: 0.4 of 1,500 ms in 5 seconds (the most since it loaded: 13); Lua data about 1.5 of 32 MB."
        public string Line(double now) =>
            "Load: "
            + Ms(WindowMs(now))
            + " of "
            + Ms(Limits.WindowMs)
            + " ms in "
            + Seconds
            + " (the most since it loaded: "
            + Ms(PeakMs)
            + ")"
            + (Kept < 0 ? "." : "; Lua data about " + Mb(Kept) + " of " + Mb(Limits.KeptBytes) + " MB.");

        static string Seconds => Limits.WindowSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds";

        static string Ms(double ms) => ms.ToString(ms < 10 ? "0.0" : "N0", CultureInfo.InvariantCulture);

        static string Mb(long bytes)
        {
            double mb = bytes / (double)(1 << 20);
            return mb.ToString(mb < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
        }
    }
}
