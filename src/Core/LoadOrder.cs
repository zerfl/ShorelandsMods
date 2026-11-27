using System;
using System.Collections.Generic;
using System.Linq;

namespace ShorelandsMods.Core
{
    internal static class LoadOrder
    {
        // The saved order first (ids no longer present dropped), then every other mod alphabetically.
        public static List<string> Arrange(IEnumerable<string> present, IEnumerable<string> saved)
        {
            var have = new HashSet<string>(present, StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var id in saved)
                if (have.Remove(id))
                    order.Add(id);
            order.AddRange(have.OrderBy(id => id, StringComparer.Ordinal));
            return order;
        }

        public static string[] Split(string list) =>
            (list ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    }
}
