using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lua;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShorelandsMods.Core.Areas
{
    internal enum JsonProblem
    {
        None,

        // A value of a kind that can never be stored: the author's mistake.
        NotStorable,

        // An inf or nan number, which may come from the player's data (a rate over zero minutes).
        OutOfRange,

        // Over the bytes left; the walk stopped there.
        TooBig,
    }

    // Lua values <-> JSON: numbers, text, true/false and tables of those. A table is a list (keys 1..n) or a map
    // with text keys.
    internal static class LuaJson
    {
        const int MaxDepth = 32;

        // Null with the problem and a sentence if the value cannot be stored. Every key, value and separator is charged
        // against maxBytes while walking (texts by length, so a lower bound of the JSON's size), and the walk stops once
        // they run out: a table that holds the same sub-table many times costs no more than maxBytes of work.
        public static JToken ToJson(LuaValue v, int maxBytes, out JsonProblem problem, out string why)
        {
            problem = JsonProblem.None;
            why = null;
            var w = new Walk { Left = maxBytes };
            try
            {
                return w.Convert(v, 0);
            }
            catch (TooBig)
            {
                problem = JsonProblem.TooBig;
                return null;
            }
            catch (ArgumentOutOfRangeException e)
            {
                problem = JsonProblem.OutOfRange;
                why = e.ParamName;
                return null;
            }
            catch (ArgumentException e)
            {
                problem = JsonProblem.NotStorable;
                why = e.Message;
                return null;
            }
        }

        sealed class TooBig : Exception { }

        sealed class Walk
        {
            public long Left;
            readonly HashSet<LuaTable> open = new HashSet<LuaTable>();

            void Charge(long bytes)
            {
                Left -= bytes;
                if (Left < 0)
                    throw new TooBig();
            }

            public JToken Convert(LuaValue v, int depth)
            {
                switch (v.Type)
                {
                    case LuaValueType.Boolean:
                        Charge(4);
                        return new JValue(v.Read<bool>());
                    case LuaValueType.String:
                        var s = v.Read<string>();
                        Charge(s.Length + 2L);
                        return new JValue(s);
                    case LuaValueType.Number:
                        double d = v.Read<double>();
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            throw new ArgumentOutOfRangeException("numbers must be finite, not " + LuaText.Num(d));
                        Charge(NumberChars(d));
                        return new JValue(d);
                    case LuaValueType.Table:
                        var t = v.Read<LuaTable>();
                        if (depth >= MaxDepth)
                            throw new ArgumentException("tables may be nested at most " + MaxDepth + " deep");
                        if (!open.Add(t))
                            throw new ArgumentException("a table contains itself");
                        try
                        {
                            return Table(t, depth);
                        }
                        finally
                        {
                            open.Remove(t);
                        }
                    default:
                        throw new ArgumentException(
                            "only numbers, text, true/false and tables of those can be saved, not a " + v.TypeToString()
                        );
                }
            }

            // Newtonsoft writes a whole number as "12.0"; at least that many characters, and 3 for any other.
            static int NumberChars(double d)
            {
                if (d != Math.Floor(d) || Math.Abs(d) >= 1e15)
                    return 3;
                int n = d < 0 ? 4 : 3;
                for (double a = Math.Abs(d); a >= 10; a /= 10)
                    n++;
                return n;
            }

            // Keys and separators are charged in the first pass, values in the second.
            JToken Table(LuaTable t, int depth)
            {
                Charge(2);
                int n = t.ArrayLength;
                int total = 0;
                bool textKeys = false;
                foreach (var kv in t)
                {
                    if (total++ > 0)
                        Charge(1);
                    if (kv.Key.Type == LuaValueType.String)
                    {
                        textKeys = true;
                        Charge(kv.Key.Read<string>().Length + 3L);
                    }
                    else if (!(kv.Key.TryRead<double>(out var k) && k == Math.Floor(k) && k >= 1 && k <= n))
                        throw new ArgumentException(
                            "table keys must be 1, 2, 3, ... (a list) or text (a map), not " + LuaText.Describe(kv.Key)
                        );
                }
                if (textKeys && total != 0 && n > 0)
                    throw new ArgumentException("a table cannot mix list entries (1, 2, 3, ...) and text keys");
                if (!textKeys)
                {
                    var a = new JArray();
                    for (int i = 1; i <= n; i++)
                        a.Add(Convert(t[i], depth + 1));
                    return a;
                }
                var o = new JObject();
                foreach (var kv in t)
                    o[kv.Key.Read<string>()] = Convert(kv.Value, depth + 1);
                return o;
            }
        }

        public static LuaValue ToLua(JToken j)
        {
            switch (j.Type)
            {
                case JTokenType.Boolean:
                    return j.Value<bool>();
                case JTokenType.Integer:
                case JTokenType.Float:
                    return j.Value<double>();
                case JTokenType.String:
                    return j.Value<string>();
                case JTokenType.Array:
                    var a = (JArray)j;
                    var t = new LuaTable(a.Count, 0);
                    for (int i = 0; i < a.Count; i++)
                        t[i + 1] = ToLua(a[i]);
                    return t;
                case JTokenType.Object:
                    var o = (JObject)j;
                    var m = new LuaTable(0, o.Count);
                    foreach (var p in o.Properties())
                        m[p.Name] = ToLua(p.Value);
                    return m;
                default:
                    return LuaValue.Nil;
            }
        }
    }

    // A mod's saved data: mods_data/<id>/data.json, one JSON object, at most Limits.DataBytes. Without a file it lives
    // in memory only (the session store).
    internal sealed class DataStore
    {
        readonly string root,
            file;
        readonly JObject values;
        readonly Dictionary<string, int> sizes = new Dictionary<string, int>();
        int total;
        public bool Dirty { get; private set; }

        // When Flush last wrote the file (Host.Env.Now).
        public double WrittenAt = double.NegativeInfinity;

        // root: mods_data; nothing below it may be a link.
        public DataStore(string root, string file, Action<string> warn)
        {
            this.root = root;
            this.file = file;
            values = new JObject();
            if (file == null || !File.Exists(file))
                return;
            string text = ModFiles.ReadText(root, file, Limits.DataFileBytes, out string problem);
            try
            {
                if (text != null)
                    values = JObject.Parse(text);
            }
            catch (JsonException e)
            {
                problem = "data.json is not valid JSON (" + e.Message + ")";
            }
            if (problem != null)
            {
                values = new JObject();
                try
                {
                    ModFiles.CheckWrite(root, file + ".bad");
                    File.Delete(file + ".bad");
                    File.Move(file, file + ".bad");
                    problem += "; it was kept as data.json.bad";
                }
                catch (Exception e) when (ModFiles.IsFileError(e)) { }
                warn(problem);
            }
            foreach (var p in values.Properties())
                total += sizes[p.Name] = Size(p.Name, p.Value);
        }

        static int Size(string key, JToken value) =>
            Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value.ToString(Formatting.None)) + 4;

        // The bytes a value under key may take as JSON: what the other keys leave of Limits.DataBytes.
        public int Room(string key)
        {
            sizes.TryGetValue(key, out int old);
            return Math.Max(0, Limits.DataBytes - (total - old) - Encoding.UTF8.GetByteCount(key) - 4);
        }

        public static string Full(string key) =>
            "the mod's data would be over " + Limits.DataBytes / 1024 + " KB, so \"" + key + "\" was not stored";

        // Null, or the sentence why it does not fit.
        public string Save(string key, JToken value)
        {
            bool known = sizes.TryGetValue(key, out int old);
            if (!known && value != null && sizes.Count >= Limits.DataKeys)
                return "the mod already keeps "
                    + Limits.DataKeys.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                    + " keys, so \""
                    + key
                    + "\" was not stored";
            int size = value == null ? 0 : Size(key, value);
            if (total - old + size > Limits.DataBytes)
                return Full(key);
            total += size - old;
            if (value == null)
            {
                values.Remove(key);
                sizes.Remove(key);
            }
            else
            {
                values[key] = value;
                sizes[key] = size;
            }
            Dirty = true;
            return null;
        }

        public JToken Load(string key) => values.TryGetValue(key, out var v) ? v : null;

        public void Flush(double now)
        {
            if (!Dirty || file == null)
                return;
            ModFiles.CheckWrite(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            AtomicFile.Write(file, values.ToString(Formatting.Indented));
            Dirty = false;
            WrittenAt = now;
        }
    }

    // Readers (OBS, a text widget) see the old text or the new one, never no file: on Windows the new file is renamed
    // over the old one in one step (MoveFileEx); File.Replace on the game's Mono leaves a moment without the file.
    internal static class AtomicFile
    {
        const uint ReplaceExisting = 0x1,
            WriteThrough = 0x8;

        [System.Runtime.InteropServices.DllImport(
            "kernel32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            SetLastError = true
        )]
        static extern bool MoveFileEx(string from, string to, uint flags);

        static bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        // Writes that had to use File.Replace.
        public static int Fallbacks;

        public static void Write(string file, string text)
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (windows)
            {
                try
                {
                    // A reader that has the file open makes the rename fail for a moment; try again briefly.
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (MoveFileEx(tmp, file, ReplaceExisting | WriteThrough))
                            return;
                        System.Threading.Thread.Sleep(1);
                    }
                    Fallbacks++;
                }
                catch (EntryPointNotFoundException)
                {
                    windows = false;
                }
                catch (DllNotFoundException)
                {
                    windows = false;
                }
            }
            if (File.Exists(file))
                File.Replace(tmp, file, null);
            else
                File.Move(tmp, file);
        }
    }

    internal sealed class DataArea : Area
    {
        double nextFlush;

        public override string Name => "Saved data";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "DataSave( key:string, value ) -> ok:boolean",
                "keeps value under key for good: across reloads, game restarts and mod updates (mods_data/<mod>/data.json, "
                    + "written within 2 s of a change, at most every "
                    + Limits.DataWriteSeconds
                    + " s, and when the mod stops or the game closes). Numbers, texts, true/false and tables of those (a list, or a "
                    + "map with text keys); nil removes the key. false (not stored) for a key over 100 characters, inf or nan, "
                    + "or when the mod's data would be over "
                    + Limits.DataBytes / 1024
                    + " KB or "
                    + Limits.DataKeys.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                    + " keys",
                c => Save(c, Store(c.Mod))
            );
            s.Function(
                "DataLoad( key:string, [default] ) -> value",
                "a copy of what DataSave stored under key, or default (nil if not given) when nothing is stored",
                c => Load(c, Store(c.Mod))
            );
        }

        internal static int Load(ApiCall c, DataStore store)
        {
            var v = store.Load(c.String(1, "key"));
            return c.Return(v != null ? LuaJson.ToLua(v) : c.Arg(2));
        }

        internal static int Save(ApiCall c, DataStore store)
        {
            string key = c.String(1, "key");
            if (key.Length == 0 || key.Length > 100)
            {
                c.Refuse(
                    1,
                    "a key must be 1 to 100 characters, so \"" + TextEscape.Clip(key, 40) + "\" was not stored"
                );
                return c.ReturnOk();
            }
            JToken json = null;
            if (c.Has(2))
            {
                json = LuaJson.ToJson(c.Arg(2), store.Room(key), out var problem, out var why);
                if (problem == JsonProblem.NotStorable)
                    throw c.Error("argument 2 (value) cannot be stored: " + why);
                if (problem == JsonProblem.OutOfRange)
                    c.Refuse(2, why + ", so \"" + key + "\" was not stored");
                if (problem == JsonProblem.TooBig)
                    c.Refuse(0, DataStore.Full(key));
                if (problem != JsonProblem.None)
                    return c.ReturnOk();
            }
            string full = store.Save(key, json);
            if (full != null)
                c.Refuse(0, full);
            return c.ReturnOk();
        }

        DataStore Store(Mod mod)
        {
            var holder = mod.Get<Holder>();
            return holder.Store
                ?? (
                    holder.Store = new DataStore(Host.Env.DataRoot, Path.Combine(mod.DataDir, "data.json"), w =>
                        Host.Notice(mod, w)
                    )
                );
        }

        sealed class Holder
        {
            public DataStore Store;
        }

        // Every 2 s, each store changed since its last write and written at least Limits.DataWriteSeconds ago.
        protected internal override void Update(double now)
        {
            if (now < nextFlush)
                return;
            nextFlush = now + 2;
            var mods = Host.Mods;
            for (int i = 0; i < mods.Count; i++)
                Flush(mods[i], now - Limits.DataWriteSeconds);
        }

        protected internal override void ModStopping(Mod mod) => Flush(mod, double.PositiveInfinity);

        void Flush(Mod mod, double writtenBefore)
        {
            var store = mod.Find<Holder>()?.Store;
            if (store == null || !store.Dirty || store.WrittenAt > writtenBefore)
                return;
            long t0 = HostClock.Now;
            try
            {
                store.Flush(Host.Env.Now);
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                // Tried again after Limits.DataWriteSeconds.
                store.WrittenAt = Host.Env.Now;
                Host.WriteFailed(mod, "saving data.json", e);
            }
            Host.ChargeWork(mod, ExportArea.FilesWork, ModLoad.MsSince(t0));
        }
    }

    // SessionSet / SessionGet: values a mod keeps for this run of the game. Held by the host, so they outlive a
    // reload, switching the mod off and on, and a relogin; gone when the game closes.
    internal sealed class SessionArea : Area
    {
        readonly Dictionary<string, DataStore> stores = new Dictionary<string, DataStore>();

        public override string Name => "Session data";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "SessionSet( key:string, value ) -> ok:boolean",
                "keeps value under key until the game closes: it outlives a reload of the mod, switching it off and on, "
                    + "and logging out and in, so \"this session\" figures need no other check. Values and limits as DataSave "
                    + "(shares no keys with it); false (not stored) as DataSave",
                c => DataArea.Save(c, Store(c.Mod))
            );
            s.Function(
                "SessionGet( key:string, [default] ) -> value",
                "a copy of what SessionSet stored under key during this run of the game, or default (nil if not given)",
                c => DataArea.Load(c, Store(c.Mod))
            );
        }

        DataStore Store(Mod mod)
        {
            if (!stores.TryGetValue(mod.Id, out var store))
                stores[mod.Id] = store = new DataStore(null, null, null);
            return store;
        }
    }
}
