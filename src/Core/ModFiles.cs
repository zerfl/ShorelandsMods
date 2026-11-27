using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ShorelandsMods.Core
{
    // Every file the host opens or writes for a mod goes through here. Below a trusted folder (the mods folder, a mod's
    // own folder, mods_data) no part of the path may be a link, and nothing is read past a size cap. The trusted folder
    // itself may be a link: where the player keeps mods is the player's choice. Hard links cannot be told from files;
    // one only reaches that file's own data.
    internal static class ModFiles
    {
        // Entries (files and folders) one walk of a folder visits at most.
        public const int MaxEntries = 5000;

        public const string LinkSentence =
            "a link (a junction or symbolic link) to another place; the host does not follow links inside a mod folder";

        // The errors listing, opening or reading a file can really throw.
        public static bool IsFileError(Exception e) =>
            e is IOException
            || e is UnauthorizedAccessException
            || e is SecurityException
            || e is NotSupportedException
            || e is ArgumentException;

        // A junction, symbolic link or mount point. Missing: false.
        public static bool IsLink(string path)
        {
            FileAttributes a;
            try
            {
                a = File.GetAttributes(path);
            }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
            {
                return false;
            }
            return (a & FileAttributes.ReparsePoint) != 0 && NameSurrogate(path);
        }

        public static bool IsLink(FileSystemInfo entry) =>
            (entry.Attributes & FileAttributes.ReparsePoint) != 0 && NameSurrogate(entry.FullName);

        // The part of full below root that is a link ("files/" or "files/a.lua"), or null. Throws for a full path
        // outside root.
        public static string LinkBelow(string root, string full)
        {
            string r = Path.GetFullPath(root).TrimEnd('\\', '/');
            string f = Path.GetFullPath(full);
            if (!f.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(full + " is not inside " + root);
            var parts = f.Substring(r.Length + 1).Split(Path.DirectorySeparatorChar);
            string at = r;
            for (int i = 0; i < parts.Length; i++)
            {
                at += Path.DirectorySeparatorChar + parts[i];
                if (IsLink(at))
                    return string.Join("/", parts, 0, i + 1) + (i < parts.Length - 1 ? "/" : "");
            }
            return null;
        }

        // Null, or the sentence why the host opens nothing at full for root.
        public static string Problem(string root, string full)
        {
            try
            {
                string link = LinkBelow(root, full);
                return link == null ? null : link + " is " + LinkSentence;
            }
            catch (Exception e) when (IsFileError(e))
            {
                return Rel(root, full) + " cannot be read (" + e.Message + ")";
            }
        }

        // The whole file, or null with a sentence: a link on the way, over maxBytes, or unreadable.
        public static byte[] ReadBytes(string root, string full, long maxBytes, out string problem)
        {
            problem = Problem(root, full);
            if (problem != null)
                return null;
            try
            {
                using (var f = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (f.Length > maxBytes)
                    {
                        problem = Rel(root, full) + " is " + Size(f.Length) + "; it may be at most " + Size(maxBytes);
                        return null;
                    }
                    var b = new byte[f.Length];
                    int got = 0;
                    while (got < b.Length)
                    {
                        int r = f.Read(b, got, b.Length - got);
                        if (r <= 0)
                            break;
                        got += r;
                    }
                    if (got < b.Length)
                        Array.Resize(ref b, got);
                    return b;
                }
            }
            catch (Exception e) when (IsFileError(e))
            {
                problem = Rel(root, full) + " cannot be read (" + e.Message + ")";
                return null;
            }
        }

        // Text as File.ReadAllText reads it (a byte order mark picks the encoding, else UTF-8).
        public static string ReadText(string root, string full, long maxBytes, out string problem)
        {
            var b = ReadBytes(root, full, maxBytes, out problem);
            if (b == null)
                return null;
            using (var r = new StreamReader(new MemoryStream(b), Encoding.UTF8, true))
                return r.ReadToEnd();
        }

        // Before writing full (and full.tmp beside it) below root: throws an IOException naming a link on the way.
        public static void CheckWrite(string root, string full)
        {
            string link = LinkBelow(root, full) ?? LinkBelow(root, full + ".tmp");
            if (link != null)
                throw new IOException(link + " is a link to another place; the host writes nothing through it");
        }

        // The files and folders below dir, at most MaxEntries, without entering a link (a link is listed itself).
        public static IEnumerable<FileSystemInfo> Walk(string dir)
        {
            var todo = new Stack<DirectoryInfo>();
            todo.Push(new DirectoryInfo(dir));
            int seen = 0;
            while (todo.Count > 0)
                foreach (var e in todo.Pop().EnumerateFileSystemInfos())
                {
                    if (++seen > MaxEntries)
                        yield break;
                    yield return e;
                    if (e is DirectoryInfo d && !IsLink(e))
                        todo.Push(d);
                }
        }

        public static string Rel(string root, string full)
        {
            string r = root.TrimEnd('\\', '/');
            return full.Length > r.Length ? full.Substring(r.Length + 1).Replace('\\', '/') : full;
        }

        public static string Size(long bytes) =>
            bytes >= 1 << 20
                ? Math.Round(bytes / 1048576.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB"
                : (bytes + 1023) / 1024 + " KB";

        // Only reparse points that stand for another name redirect (IsReparseTagNameSurrogate): junctions, symbolic
        // links, mount points. Cloud placeholders (OneDrive) and deduplicated files are ordinary files.
        static bool NameSurrogate(string path)
        {
            if (!windows)
                return true;
            try
            {
                IntPtr h = FindFirstFileW(path.TrimEnd('\\', '/'), out var d);
                if (h == new IntPtr(-1))
                    return true;
                FindClose(h);
                return (d.Tag & 0x20000000) != 0;
            }
            catch (Exception e) when (e is EntryPointNotFoundException || e is DllNotFoundException)
            {
                windows = false;
                return true;
            }
        }

        static bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FindData
        {
            public uint Attributes,
                CreatedLow,
                CreatedHigh,
                AccessedLow,
                AccessedHigh,
                WrittenLow,
                WrittenHigh,
                SizeHigh,
                SizeLow,
                Tag,
                Reserved;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string Name;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string ShortName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileW(string name, out FindData data);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr handle);
    }
}
