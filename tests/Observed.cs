using System;
using System.Collections.Generic;
using System.Linq;
using ShorelandsMods.Core;
using ShorelandsMods.Core.Ui;

// What the host observes for mods: wallet changes, offering rounds, item gains.
static partial class Program
{
    static void WalletChanges()
    {
        var w = new WalletChange();
        w.Baseline(0, 0);
        Check(w.Next(54380, 3, false) == (0, 0), "an update before the player is in the world moves the baseline only");
        Check(w.Next(48380, 3, true) == (-6000, 0), "a purchase: the currency change");
        Check(w.Next(48380, 4, true) == (0, 1), "CITB on its own");
        w.Reset();
        Check(w.Next(70000, 4, true) == (0, 0), "after a relogin without a baseline, the first value is not a gain");
        w.Baseline(70000, 4);
        Check(w.Next(70500, 4, true) == (500, 0), "a gain after the baseline");
    }

    static void OfferingRoundStarts()
    {
        var r = new OfferingRounds();
        Check(!r.Next(0, true, 250), "the round running on arrival is not new");
        bool any = false;
        for (double t = 0.1; t < 250; t += 0.1)
            any |= r.Next(t, true, 250 - t + Math.Sin(t) * 0.4);
        Check(!any, "one round read every tick with jitter: no start");
        Check(!r.Next(250.1, true, null) && !r.Next(250.4, true, null), "between rounds: nothing");
        Check(r.Next(250.5, true, 599.9), "the countdown jumps up: a new round");
        Check(!r.Next(250.6, true, 599.8), "and only once");
        Check(!r.Next(300, false, null), "leaving the world");
        Check(!r.Next(900, true, 400), "a round that started while away is not reported on arrival");
    }

    static void ItemGainCounting()
    {
        var g = new ItemGains();
        var items = new Dictionary<(int Id, int Meta), int> { [(1, 0)] = 5, [(9999, 69)] = 1 };
        Dictionary<(int Id, int Meta), int> Count() => new Dictionary<(int, int), int>(items);
        Check(g.Update(1, true, Count) == null, "what the player has on arrival is the baseline");

        items[(9999, 160)] = 1;
        g.Changed(2);
        Check(g.Update(2.1, true, Count) == null, "nothing before the items settled");
        var got = g.Update(2.5, true, Count);
        Check(
            got != null && got.Single() == (9999, 160, 1),
            "a bought koi is a gain",
            got == null ? "null" : got.Count.ToString()
        );

        items.Remove((9999, 69));
        g.Changed(3);
        items[(9999, 69)] = 1;
        g.Changed(3.07);
        Check(
            g.Update(3.3, true, Count) == null && g.Update(3.6, true, Count) == null,
            "a koi out of the statue into a bag is no gain"
        );

        items[(1, 0)] = 8;
        items[(2, 0)] = 1;
        items.Remove((9999, 160));
        g.Changed(4);
        got = g.Update(4.5, true, Count);
        Check(
            got != null && got.Count == 2 && got[0] == (1, 0, 3) && got[1] == (2, 0, 1),
            "a stack that grew counts the difference; a loss is no gain",
            got == null ? "null" : string.Join(",", got)
        );
        Check(g.Update(5, true, Count) == null, "each gain once");

        g.Update(6, false, Count);
        items[(3, 0)] = 2;
        Check(g.Update(7, true, Count) == null, "after a relogin the items are the baseline again");
    }
}
