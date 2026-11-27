using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ShorelandsMods.Core
{
    // Which mod folders changed. A FileSystemWatcher on the mods folder alone (on this Mono it polls, and below the top
    // it would follow links) and a background scan of every mod folder that does not enter links both report changes;
    // a mod is due for reload once its last change is a debounce old. The scan compares each folder with its state
    // when the host last (re)loaded it, so a change the watcher already reported does not cause a second reload.
    internal sealed class ChangeWatch : IDisposable
    {
        readonly string root;
        readonly Func<double> now;
        readonly ConcurrentQueue<string> changed = new ConcurrentQueue<string>();
        readonly ConcurrentDictionary<string, string> loaded = new ConcurrentDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        readonly Dictionary<string, double> pending = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> due = new List<string>();
        FileSystemWatcher watcher;
        volatile bool stopped;

        public int WatcherEvents,
            ScanEvents;

        public ChangeWatch(string root, Func<double> now)
        {
            this.root = Path.GetFullPath(root);
            this.now = now;
        }

        public void Start()
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = false,
                NotifyFilter =
                    NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            };
            FileSystemEventHandler h = (s, e) => FromWatcher(e.FullPath);
            watcher.Changed += h;
            watcher.Created += h;
            watcher.Deleted += h;
            watcher.Renamed += (s, e) =>
            {
                FromWatcher(e.OldFullPath);
                FromWatcher(e.FullPath);
            };
            watcher.EnableRaisingEvents = true;
            new Thread(ScanLoop) { IsBackground = true, Name = "ShorelandsMods scan" }.Start();
        }

        void FromWatcher(string fullPath)
        {
            string id = ModIdOf(root, fullPath);
            if (id == null)
                return;
            Interlocked.Increment(ref WatcherEvents);
            changed.Enqueue(id);
        }

        // Main thread: the host (re)loaded this folder; later scans compare with its state now.
        public void Loaded(string id) => loaded[id] = Fingerprint(Path.Combine(root, id));

        public static string ModIdOf(string root, string fullPath)
        {
            string rel = fullPath.Length > root.Length ? fullPath.Substring(root.Length).TrimStart('\\', '/') : "";
            int sep = rel.IndexOfAny(new[] { '\\', '/' });
            if (sep >= 0)
                rel = rel.Substring(0, sep);
            else if (rel.Contains("."))
                return null; // _api.txt and other files beside the mods
            return rel.Length == 0 || ModPaths.IsHostName(rel) ? null : rel;
        }

        // Main thread, every frame: the ids whose last change is older than the debounce and that differ from what
        // the host loaded.
        public List<string> Due()
        {
            double t = now();
            while (changed.TryDequeue(out var id))
                pending[id] = t;
            due.Clear();
            foreach (var kv in pending)
                if (t - kv.Value >= Limits.ReloadDebounceSeconds)
                    due.Add(kv.Key);
            foreach (var id in due)
                pending.Remove(id);
            // A late report of a change the host already loaded is not a new change.
            if (due.Count > 0)
                due.RemoveAll(id =>
                    loaded.TryGetValue(id, out var atLoad) && atLoad == Fingerprint(Path.Combine(root, id))
                );
            return due;
        }

        void ScanLoop()
        {
            var reported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!stopped)
            {
                Thread.Sleep(Limits.ScanIntervalMs);
                Scan(reported);
            }
        }

        // One round of the scan thread. File errors never end the thread: an unreadable folder has a fingerprint of
        // its own, so it is reported once, not every round.
        internal void Scan(Dictionary<string, string> reported)
        {
            var ids = new HashSet<string>(loaded.Keys, StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
                    if (!ModPaths.IsHostName(dir.Name))
                        ids.Add(dir.Name);
            }
            catch (Exception e) when (ModFiles.IsFileError(e)) { }
            foreach (var id in ids)
            {
                string print = Fingerprint(Path.Combine(root, id));
                loaded.TryGetValue(id, out var atLoad);
                reported.TryGetValue(id, out var told);
                if (print != (atLoad ?? "") && print != told)
                    Report(id, reported, print);
            }
        }

        void Report(string id, Dictionary<string, string> reported, string print)
        {
            reported[id] = print;
            Interlocked.Increment(ref ScanEvents);
            changed.Enqueue(id);
        }

        // Entry count, total size and newest write time of a mod folder, not entering links (the mod folder itself may
        // be one); "" when it does not exist.
        internal static string Fingerprint(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                    return "";
                long size = 0,
                    newest = 0;
                int count = 0;
                foreach (var e in ModFiles.Walk(dir))
                {
                    count++;
                    if (e is FileInfo f && !ModFiles.IsLink(e))
                        size += f.Length;
                    newest = Math.Max(newest, e.LastWriteTimeUtc.Ticks);
                }
                return count + "/" + size + "/" + newest;
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                return "unreadable: " + e.GetType().Name;
            }
        }

        public void Dispose()
        {
            stopped = true;
            watcher?.Dispose();
        }
    }
}
