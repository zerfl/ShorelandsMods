using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShorelandsMods.Core.Areas
{
    // Text files for other programs (OBS): mods_data/<id>/export/<name>.txt. A file is written at most every
    // Limits.ExportMinInterval seconds, and all of a mod's files together within an allowance of
    // Limits.ExportWritesPerSecond writes and Limits.ExportBytesPerSecond bytes (refilled continuously, a second's
    // worth at most). A text that may not be written yet waits, replaced by a newer one, and is written when due.
    internal sealed class ExportLimiter
    {
        readonly Dictionary<string, double> lastWrite = new Dictionary<string, double>();
        readonly Dictionary<string, string> waiting = new Dictionary<string, string>();
        double writes = Limits.ExportWritesPerSecond,
            bytes = Limits.ExportBytesPerSecond,
            filledAt;

        public int Files => lastWrite.Count;

        public bool Knows(string name) => lastWrite.ContainsKey(name);

        // True if the text may be written now; otherwise it waits.
        public bool Offer(string name, string text, double now)
        {
            bool known = lastWrite.TryGetValue(name, out double last);
            if ((known && now - last < Limits.ExportMinInterval) || !Take(text, now))
            {
                if (!known)
                    lastWrite[name] = double.NegativeInfinity;
                waiting[name] = text;
                return false;
            }
            lastWrite[name] = now;
            waiting.Remove(name);
            return true;
        }

        bool Take(string text, double now)
        {
            double refill = Math.Max(0, now - filledAt);
            filledAt = now;
            writes = Math.Min(Limits.ExportWritesPerSecond, writes + refill * Limits.ExportWritesPerSecond);
            bytes = Math.Min(Limits.ExportBytesPerSecond, bytes + refill * Limits.ExportBytesPerSecond);
            int size = Encoding.UTF8.GetByteCount(text);
            if (writes < 1 || bytes < size)
                return false;
            writes--;
            bytes -= size;
            return true;
        }

        // Waiting texts whose time has come, as (name, text), taken from the allowance.
        public List<KeyValuePair<string, string>> Due(double now)
        {
            var r = new List<KeyValuePair<string, string>>();
            foreach (var kv in waiting)
                if (now - lastWrite[kv.Key] >= Limits.ExportMinInterval && Take(kv.Value, now))
                    r.Add(kv);
            foreach (var kv in r)
            {
                waiting.Remove(kv.Key);
                lastWrite[kv.Key] = now;
            }
            return r;
        }

        // Everything still waiting, when the mod stops.
        public List<KeyValuePair<string, string>> Rest()
        {
            var r = new List<KeyValuePair<string, string>>(waiting);
            waiting.Clear();
            return r;
        }

        public bool AnyWaiting => waiting.Count > 0;
    }

    internal sealed class ExportArea : Area
    {
        public override string Name => "Export";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "ExportText( filename:string, text:string ) -> ok:boolean",
                "writes text to <game>/mods_data/<mod>/export/<filename>.txt for other programs such as OBS, replacing "
                    + "the file in one step (a reader never sees it missing or half written). filename: 1 to 64 letters, digits, "
                    + "space, _ - . (\".txt\" is added). One write per file every "
                    + LuaText.Num(Limits.ExportMinInterval)
                    + " s, and at most "
                    + Limits.ExportWritesPerSecond
                    + " writes and "
                    + Limits.ExportBytesPerSecond / 1024
                    + " KB a second for all of the mod's files: a newer text waits and is written when due (all waiting "
                    + "texts are written when the mod stops). false (nothing written) for another filename, a "
                    + "text over "
                    + Limits.ExportBytes / 1024
                    + " KB, or a file beyond the "
                    + Limits.ExportFiles
                    + " a mod may have",
                Export
            );
        }

        int Export(ApiCall c)
        {
            string name = c.String(1, "filename");
            string text = c.Text(2, "text");
            string file = ModPaths.ExportFileName(name, out var why);
            if (file == null)
                return Skip(c, 1, why);
            // A text of more characters than bytes allowed is over them in UTF-8 too; only shorter ones are counted.
            if (text.Length > Limits.ExportBytes || Encoding.UTF8.GetByteCount(text) > Limits.ExportBytes)
                return Skip(c, 2, "the text for " + file + " is over " + Limits.ExportBytes / 1024 + " KB");
            var limiter = c.Mod.Get<ExportLimiter>();
            if (!limiter.Knows(file) && limiter.Files >= Limits.ExportFiles)
                return Skip(
                    c,
                    0,
                    "the mod already exports " + Limits.ExportFiles + " files, so " + file + " is not written"
                );
            if (limiter.Offer(file, text, Host.Env.Now))
                Write(c.Mod, file, text);
            return c.ReturnOk();
        }

        static int Skip(ApiCall c, int n, string sentence)
        {
            c.Refuse(n, sentence);
            return c.ReturnOk();
        }

        protected internal override void Update(double now)
        {
            var mods = Host.Mods;
            for (int i = 0; i < mods.Count; i++)
            {
                var limiter = mods[i].Find<ExportLimiter>();
                if (limiter == null || !limiter.AnyWaiting)
                    continue;
                foreach (var kv in limiter.Due(now))
                    Write(mods[i], kv.Key, kv.Value);
            }
        }

        protected internal override void ModStopping(Mod mod)
        {
            var limiter = mod.Find<ExportLimiter>();
            if (limiter != null)
                foreach (var kv in limiter.Rest())
                    Write(mod, kv.Key, kv.Value);
        }

        void Write(Mod mod, string file, string text)
        {
            long t0 = HostClock.Now;
            try
            {
                string dir = Path.Combine(mod.DataDir, "export");
                ModFiles.CheckWrite(Host.Env.DataRoot, Path.Combine(dir, file));
                Directory.CreateDirectory(dir);
                AtomicFile.Write(Path.Combine(dir, file), text);
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                Host.WriteFailed(mod, "export " + file, e);
            }
            Host.ChargeWork(mod, FilesWork, ModLoad.MsSince(t0));
        }

        public const string FilesWork = "writing its files";
    }
}
