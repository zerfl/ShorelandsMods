using System;
using System.Collections.Generic;

namespace ShorelandsMods.Core
{
    // Everything a mod made through the host (a timer, a command, a widget), each with the
    // action that removes it. Unload, reload and switch-off dispose all of them, newest first.
    internal sealed class ResourceRegistry
    {
        readonly List<Entry> entries = new List<Entry>();

        sealed class Entry
        {
            public string Kind,
                Key;
            public Action Dispose;
        }

        // Replaces (and disposes) an earlier resource of the same kind and key.
        public void Add(string kind, string key, Action dispose)
        {
            Remove(kind, key);
            entries.Add(
                new Entry
                {
                    Kind = kind,
                    Key = key,
                    Dispose = dispose,
                }
            );
        }

        public int CountOf(string kind)
        {
            int n = 0;
            foreach (var e in entries)
                if (e.Kind == kind)
                    n++;
            return n;
        }

        // Disposes and forgets one resource; false if there was none.
        public bool Remove(string kind, string key)
        {
            int i = IndexOf(kind, key);
            if (i < 0)
                return false;
            var e = entries[i];
            entries.RemoveAt(i);
            e.Dispose();
            return true;
        }

        // Forgets one resource without disposing it (it is already gone, e.g. a timer that fired).
        public void Forget(string kind, string key)
        {
            int i = IndexOf(kind, key);
            if (i >= 0)
                entries.RemoveAt(i);
        }

        public IEnumerable<string> Describe()
        {
            foreach (var e in entries)
                yield return e.Kind + ":" + e.Key;
        }

        internal void DisposeAll(Action<string, Exception> onError)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                entries.RemoveAt(i);
                try
                {
                    e.Dispose();
                }
                catch (Exception ex)
                {
                    onError(e.Kind + ":" + e.Key, ex);
                }
            }
        }

        int IndexOf(string kind, string key)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Kind == kind && entries[i].Key == key)
                    return i;
            return -1;
        }
    }
}
