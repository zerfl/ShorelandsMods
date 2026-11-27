using System;
using System.IO;
using System.Text;

namespace ShorelandsMods.Core
{
    // Asset files checked before any decoder sees them: PNG by header, file size and chunk structure; WAV and OGG by
    // a length that a decoder cannot play past, so wrong sizes and overlong sounds are refused up front.
    internal static class FileHeaders
    {
        public const int MaxImageSide = 4096;

        // What a PNG may carry besides its image data (text, colour profile, ...).
        const long PngMetaBytes = 1 << 20;

        // Width and height of a PNG from its header, or a sentence why it is not one the host uses. Also refuses a
        // file bigger than any PNG of its declared size needs.
        public static bool Png(
            string path,
            out int width,
            out int height,
            out string problem,
            int maxSide = MaxImageSide
        )
        {
            width = height = 0;
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var head = new byte[33];
                    return PngHeader(head, Read(f, head), f.Length, maxSide, out width, out height, out _, out problem);
                }
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                return Bad("it cannot be read (" + e.Message + ")", out problem);
            }
        }

        // The whole PNG for a decoder, once its header, size and every chunk passed; null with a sentence otherwise.
        public static byte[] PngData(string path, int maxSide, out int width, out int height, out string problem)
        {
            width = height = 0;
            byte[] b;
            long maxIdat;
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var head = new byte[33];
                    if (
                        !PngHeader(
                            head,
                            Read(f, head),
                            f.Length,
                            maxSide,
                            out width,
                            out height,
                            out maxIdat,
                            out problem
                        )
                    )
                        return null;
                    b = new byte[f.Length];
                    f.Seek(0, SeekOrigin.Begin);
                    if (Read(f, b) < b.Length)
                        return Refuse("it changed while it was read", out problem);
                }
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                return Refuse("it cannot be read (" + e.Message + ")", out problem);
            }
            problem = PngChunks(b, maxIdat);
            return problem == null ? b : null;
        }

        static bool PngHeader(
            byte[] b,
            int n,
            long fileBytes,
            int maxSide,
            out int width,
            out int height,
            out long maxIdat,
            out string problem
        )
        {
            width = height = 0;
            maxIdat = 0;
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < sig.Length; i++)
                if (n < 24 || b[i] != sig[i])
                    return Bad("it is not a PNG file", out problem);
            if (BigEndian(b, 8) != 13 || Encoding.ASCII.GetString(b, 12, 4) != "IHDR")
                return Bad("it is not a readable PNG file (no IHDR)", out problem);
            width = BigEndian(b, 16);
            height = BigEndian(b, 20);
            if (width < 1 || height < 1 || width > maxSide || height > maxSide)
                return Bad(
                    "it is " + width + "x" + height + " pixels; at most " + maxSide + "x" + maxSide,
                    out problem
                );
            if (n < 29)
                return Bad("it is not a readable PNG file (cut off)", out problem);
            int depth = b[24],
                type = b[25],
                channels = type == 0 || type == 3 ? 1
                : type == 2 ? 3
                : type == 4 ? 2
                : type == 6 ? 4
                : 0;
            bool depthOk =
                depth == 8
                || (depth == 16 && type != 3)
                || ((depth == 1 || depth == 2 || depth == 4) && (type == 0 || type == 3));
            if (channels == 0 || !depthOk)
                return Bad(
                    "it is not a readable PNG file (bit depth " + depth + " with colour type " + type + ")",
                    out problem
                );
            if (b[26] != 0 || b[27] != 0 || b[28] > 1)
                return Bad("it is not a readable PNG file (unknown compression, filter or interlace)", out problem);
            // Filtered rows; Adam7 adds at most a filter byte and a partial byte per row and pass. Deflate never needs
            // a quarter more than its input.
            long raw = height * (1 + ((long)width * channels * depth + 7) / 8);
            maxIdat = raw + raw / 4 + 8L * height + 4096;
            long maxFile = 8 + maxIdat + PngMetaBytes;
            if (fileBytes > maxFile)
                return Bad(
                    "it is "
                        + ModFiles.Size(fileBytes)
                        + ", more than a "
                        + width
                        + "x"
                        + height
                        + " PNG can need ("
                        + ModFiles.Size(maxFile)
                        + ")",
                    out problem
                );
            problem = null;
            return true;
        }

        // Null when every chunk is whole, in order and known, with checksums on the ones a decoder must read; else
        // why not.
        static string PngChunks(byte[] b, long maxIdat)
        {
            int type = b[25];
            bool plte = false,
                idat = false,
                idatDone = false;
            long idatBytes = 0,
                meta = 0;
            long at = 8;
            while (true)
            {
                if (at + 12 > b.Length)
                    return "it is cut off";
                long len = (uint)BigEndian(b, (int)at);
                if (len > b.Length - at - 12)
                    return "it is cut off (a chunk runs past the end of the file)";
                int p = (int)at;
                for (int i = 4; i < 8; i++)
                    if (!char.IsLetter((char)b[p + i]) || b[p + i] > 'z')
                        return "it is damaged (a chunk has no name)";
                string name = Encoding.ASCII.GetString(b, p + 4, 4);
                bool critical = (b[p + 4] & 0x20) == 0;
                if (critical && PngCrc(b, p + 4, (int)len + 4) != (uint)BigEndian(b, p + 8 + (int)len))
                    return "it is damaged (the checksum of its " + name + " chunk is wrong)";
                if (idat && name != "IDAT")
                    idatDone = true;
                switch (name)
                {
                    case "IHDR":
                        if (at != 8)
                            return "it is damaged (two IHDR chunks)";
                        break;
                    case "PLTE":
                        if (idat || type == 0 || type == 4)
                            return "it is damaged (a palette in the wrong place)";
                        plte = true;
                        break;
                    case "IDAT":
                        if (idatDone)
                            return "it is damaged (its image data is split by other chunks)";
                        idat = true;
                        idatBytes += len;
                        if (idatBytes > maxIdat)
                            return "it holds more image data than an image of its size can need";
                        break;
                    case "IEND":
                        if (!idat)
                            return "it has no image data";
                        if (type == 3 && !plte)
                            return "it is damaged (its palette is missing)";
                        if (at + 12 + len != b.Length)
                            return "it has bytes after its end (is it damaged?)";
                        return null;
                    case "acTL":
                    case "fcTL":
                    case "fdAT":
                        return "it is an animated PNG; save it as a still PNG (a cue takes frames side by side)";
                    default:
                        if (critical)
                            return "it has a chunk the host does not know (" + name + ")";
                        meta += len + 12;
                        if (meta > PngMetaBytes)
                            return "it carries over " + ModFiles.Size(PngMetaBytes) + " besides its image";
                        break;
                }
                at += 12 + len;
            }
        }

        // Length in seconds of a .wav (PCM or float) or .ogg (Vorbis) at most SoundFileBytes, or a sentence why it
        // cannot be played.
        public static bool Audio(string path, out double seconds, out string problem)
        {
            seconds = 0;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                using (var f = File.OpenRead(path))
                {
                    if (f.Length > Limits.SoundFileBytes)
                        return Bad(
                            "it is "
                                + ModFiles.Size(f.Length)
                                + "; a sound file may be at most "
                                + ModFiles.Size(Limits.SoundFileBytes),
                            out problem
                        );
                    return ext == ".wav" ? Wav(f, out seconds, out problem) : Ogg(f, out seconds, out problem);
                }
            }
            catch (Exception e) when (ModFiles.IsFileError(e))
            {
                return Bad("it cannot be read (" + e.Message + ")", out problem);
            }
        }

        static bool Wav(Stream f, out double seconds, out string problem)
        {
            seconds = 0;
            var head = new byte[12];
            if (Read(f, head) < 12 || Ascii(head, 0) != "RIFF" || Ascii(head, 8) != "WAVE")
                return Bad("it is not a WAV file", out problem);
            int format = 0,
                channels = 0,
                rate = 0,
                bits = 0;
            var chunk = new byte[8];
            while (Read(f, chunk) == 8)
            {
                string id = Ascii(chunk, 0);
                long len = BitConverter.ToUInt32(chunk, 4);
                if (id == "fmt ")
                {
                    var fmt = new byte[16];
                    if (len < 16 || Read(f, fmt) < 16)
                        return Bad("its WAV format block is broken", out problem);
                    format = BitConverter.ToUInt16(fmt, 0);
                    channels = BitConverter.ToUInt16(fmt, 2);
                    rate = BitConverter.ToInt32(fmt, 4);
                    bits = BitConverter.ToUInt16(fmt, 14);
                    f.Seek(len - 16 + (len & 1), SeekOrigin.Current);
                }
                else if (id == "data")
                {
                    if (rate <= 0 || channels <= 0 || bits <= 0)
                        return Bad("its WAV format block is missing", out problem);
                    if (format != 1 && format != 3 && format != 0xFFFE)
                        return Bad("it is a compressed WAV; only plain PCM or float WAV files play", out problem);
                    if (bits % 8 != 0 || bits > 64)
                        return Bad("its WAV format block is broken (" + bits + " bits a sample)", out problem);
                    // A size of 0 or past the end (a recording that was never closed) plays to the end of the file.
                    long rest = f.Length - f.Position;
                    if (len == 0 || len > rest)
                        len = rest;
                    long frame = (long)channels * (bits / 8);
                    if (len < frame)
                        return Bad("it has no sound data", out problem);
                    seconds = (double)len / ((long)rate * frame);
                    problem = null;
                    return true;
                }
                else
                    f.Seek(len + (len & 1), SeekOrigin.Current);
            }
            return Bad("it has no sound data", out problem);
        }

        // Every page is read and checked (one stream, checksums, nothing between or after pages), and every audio
        // packet counted at the block size its mode gives, as a decoder plays it. The length is the samples those
        // packets decode to; the last page's position counts only when it agrees within one block, so a header that
        // claims a short sound cannot hide a long stream.
        static bool Ogg(Stream f, out double seconds, out string problem)
        {
            seconds = 0;
            var head = new byte[27];
            var lacing = new byte[255];
            var body = new byte[255 * 255];
            var packet = new MemoryStream();
            uint serial = 0;
            int pages = 0,
                rate = 0,
                channels = 0,
                shortBlock = 0,
                longBlock = 0,
                prevBlock = 0,
                modeBits = 0;
            bool[] modes = null;
            long packets = 0,
                granule = -1,
                samples = 0;
            bool open = false,
                ended = false;
            int first = -1;
            while (true)
            {
                int n = Read(f, head);
                if (n == 0)
                    break;
                if (n < 27 || Ascii(head, 0) != "OggS" || head[4] != 0)
                    return Bad(
                        pages == 0 ? "it is not an OGG file" : "it has bytes that are not part of the sound (damaged?)",
                        out problem
                    );
                int segs = head[26];
                if (Read(f, lacing, segs) < segs)
                    return Bad("it is cut off", out problem);
                int len = 0;
                for (int i = 0; i < segs; i++)
                    len += lacing[i];
                if (Read(f, body, len) < len)
                    return Bad("it is cut off", out problem);
                uint crc = BitConverter.ToUInt32(head, 22);
                head[22] = head[23] = head[24] = head[25] = 0;
                if (OggCrc(OggCrc(OggCrc(0, head, 27), lacing, segs), body, len) != crc)
                    return Bad("it is damaged (the checksum of page " + (pages + 1) + " is wrong)", out problem);
                uint ser = BitConverter.ToUInt32(head, 14);
                if (pages > 0 && ser != serial)
                    return Bad("it holds more than one stream; save it as one OGG Vorbis file", out problem);
                if (ended)
                    return Bad("it goes on after the end of its stream", out problem);
                serial = ser;
                pages++;
                bool continued = (head[5] & 1) != 0;
                if (continued != open)
                    return Bad("it is damaged (a packet is cut between pages)", out problem);
                ended = (head[5] & 4) != 0;
                long g = BitConverter.ToInt64(head, 6);
                if (g != -1)
                    granule = g;
                int at = 0;
                for (int i = 0; i < segs; i++)
                {
                    if (!open && lacing[i] > 0)
                        first = body[at];
                    if (packets == 0 || packets == 2)
                        packet.Write(body, at, lacing[i]);
                    open = true;
                    at += lacing[i];
                    if (lacing[i] == 255)
                        continue;
                    open = false;
                    if (packets == 0)
                    {
                        var id = packet.ToArray();
                        if (id.Length >= 8 && Encoding.ASCII.GetString(id, 0, 8) == "OpusHead")
                            return Bad("it is Opus; save it as OGG Vorbis", out problem);
                        if (id.Length < 30 || id[0] != 1 || Encoding.ASCII.GetString(id, 1, 6) != "vorbis")
                            return Bad("it is not OGG Vorbis", out problem);
                        channels = id[11];
                        rate = BitConverter.ToInt32(id, 12);
                        shortBlock = 1 << (id[28] & 15);
                        longBlock = 1 << (id[28] >> 4);
                        if (rate <= 0 || channels == 0 || shortBlock < 64 || longBlock > 8192 || shortBlock > longBlock)
                            return Bad("it is not a readable OGG file", out problem);
                    }
                    else if (packets == 2)
                    {
                        modes = VorbisModes(packet.ToArray(), channels);
                        if (modes == null)
                            return Bad("it is not a readable OGG Vorbis file (its setup header)", out problem);
                        modeBits = ILog(modes.Length - 1);
                    }
                    else if (packets > 2 && first >= 0 && (first & 1) == 0)
                    {
                        // An audio packet: its mode picks the block; each after the first decodes to a quarter of the
                        // last block plus a quarter of its own.
                        int mode = (first >> 1) & ((1 << modeBits) - 1);
                        if (mode < modes.Length)
                        {
                            int block = modes[mode] ? longBlock : shortBlock;
                            if (prevBlock > 0)
                                samples += prevBlock / 4 + block / 4;
                            prevBlock = block;
                        }
                    }
                    packets++;
                    packet.SetLength(0);
                    first = -1;
                }
            }
            if (pages == 0)
                return Bad("it is not an OGG file", out problem);
            if (modes == null || open)
                return Bad("it is cut off", out problem);
            if (granule < 0)
                return Bad("its length cannot be read (is the file cut off?)", out problem);
            seconds = (double)(samples - granule > longBlock / 2 ? samples : granule) / rate;
            problem = null;
            return true;
        }

        // Block size flags of the modes in a Vorbis setup header (Vorbis I, section 4.2.4), read past the codebooks,
        // floors, residues and mappings as a decoder reads them; null if it does not parse.
        static bool[] VorbisModes(byte[] p, int channels)
        {
            if (p.Length < 7 || p[0] != 5 || Encoding.ASCII.GetString(p, 1, 6) != "vorbis")
                return null;
            var r = new BitReader(p, 7);
            try
            {
                int books = r.Int(8) + 1;
                for (int i = 0; i < books; i++)
                {
                    if (r.Int(24) != 0x564342)
                        return null;
                    int dims = r.Int(16),
                        entries = r.Int(24);
                    if (r.Int(1) == 1)
                    {
                        r.Skip(5);
                        int e = 0;
                        while (e < entries)
                            e += r.Int(ILog(entries - e));
                        if (e > entries)
                            return null;
                    }
                    else
                    {
                        bool sparse = r.Int(1) == 1;
                        for (int e = 0; e < entries; e++)
                            if (!sparse || r.Int(1) == 1)
                                r.Skip(5);
                    }
                    int lookup = r.Int(4);
                    if (lookup == 1 || lookup == 2)
                    {
                        r.Skip(64);
                        int bits = r.Int(4) + 1;
                        r.Skip(1);
                        if (dims == 0)
                            return null;
                        r.Skip((lookup == 1 ? Lookup1(entries, dims) : (long)entries * dims) * bits);
                    }
                    else if (lookup != 0)
                        return null;
                }
                int times = r.Int(6) + 1;
                for (int i = 0; i < times; i++)
                    if (r.Int(16) != 0)
                        return null;
                int floors = r.Int(6) + 1;
                for (int i = 0; i < floors; i++)
                {
                    int type = r.Int(16);
                    if (type == 0)
                    {
                        r.Skip(8 + 16 + 16 + 6 + 8);
                        r.Skip(8 * (r.Int(4) + 1));
                    }
                    else if (type == 1)
                    {
                        int partitions = r.Int(5),
                            maxClass = -1;
                        var classOf = new int[partitions];
                        for (int k = 0; k < partitions; k++)
                            maxClass = Math.Max(maxClass, classOf[k] = r.Int(4));
                        var dimsOf = new int[maxClass + 1];
                        for (int c = 0; c <= maxClass; c++)
                        {
                            dimsOf[c] = r.Int(3) + 1;
                            int sub = r.Int(2);
                            if (sub > 0)
                                r.Skip(8);
                            r.Skip(8 * (1 << sub));
                        }
                        r.Skip(2);
                        int rangeBits = r.Int(4);
                        for (int k = 0; k < partitions; k++)
                            r.Skip(dimsOf[classOf[k]] * rangeBits);
                    }
                    else
                        return null;
                }
                int residues = r.Int(6) + 1;
                for (int i = 0; i < residues; i++)
                {
                    if (r.Int(16) > 2)
                        return null;
                    r.Skip(24 + 24 + 24);
                    int classes = r.Int(6) + 1;
                    r.Skip(8);
                    int books8 = 0;
                    for (int c = 0; c < classes; c++)
                    {
                        int low = r.Int(3),
                            high = r.Int(1) == 1 ? r.Int(5) : 0;
                        for (int bit = 0; bit < 8; bit++)
                            if ((((high << 3) | low) >> bit & 1) == 1)
                                books8++;
                    }
                    r.Skip(8 * books8);
                }
                int mappings = r.Int(6) + 1;
                int channelBits = ILog(channels - 1);
                for (int i = 0; i < mappings; i++)
                {
                    if (r.Int(16) != 0)
                        return null;
                    int submaps = r.Int(1) == 1 ? r.Int(4) + 1 : 1;
                    if (r.Int(1) == 1)
                        r.Skip((r.Int(8) + 1) * 2 * channelBits);
                    if (r.Int(2) != 0)
                        return null;
                    if (submaps > 1)
                        r.Skip(4 * channels);
                    r.Skip(24 * submaps);
                }
                var modes = new bool[r.Int(6) + 1];
                for (int i = 0; i < modes.Length; i++)
                {
                    modes[i] = r.Int(1) == 1;
                    if (r.Int(16) != 0 || r.Int(16) != 0)
                        return null;
                    if (r.Int(8) >= mappings)
                        return null;
                }
                return r.Int(1) == 1 ? modes : null;
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        // The greatest r with r^dims <= entries.
        static long Lookup1(int entries, int dims)
        {
            long r = (long)Math.Floor(Math.Pow(entries, 1.0 / dims));
            while (r > 0 && Pow(r, dims) > entries)
                r--;
            while (Pow(r + 1, dims) <= entries)
                r++;
            return r;
        }

        static long Pow(long x, int n)
        {
            long v = 1;
            for (int i = 0; i < n && v <= int.MaxValue; i++)
                v *= x;
            return v;
        }

        static int ILog(int x)
        {
            int n = 0;
            for (; x > 0; x >>= 1)
                n++;
            return n;
        }

        // Vorbis bit packing: least significant bit first.
        sealed class BitReader
        {
            readonly byte[] b;
            long pos;

            public BitReader(byte[] bytes, int startByte)
            {
                b = bytes;
                pos = startByte * 8L;
            }

            public int Int(int bits)
            {
                if (pos + bits > b.Length * 8L)
                    throw new EndOfStreamException();
                int v = 0;
                for (int i = 0; i < bits; i++, pos++)
                    v |= (b[pos >> 3] >> (int)(pos & 7) & 1) << i;
                return v;
            }

            public void Skip(long bits)
            {
                if (bits < 0 || pos + bits > b.Length * 8L)
                    throw new EndOfStreamException();
                pos += bits;
            }
        }

        static readonly uint[] PngTable = MakeTable(0xEDB88320u, true);
        static readonly uint[] OggTable = MakeTable(0x04C11DB7u, false);

        static uint[] MakeTable(uint poly, bool reflected)
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = reflected ? i : i << 24;
                for (int k = 0; k < 8; k++)
                    c = reflected
                        ? ((c & 1) != 0 ? poly ^ (c >> 1) : c >> 1)
                        : ((c & 0x80000000u) != 0 ? (c << 1) ^ poly : c << 1);
                t[i] = c;
            }
            return t;
        }

        static uint PngCrc(byte[] b, int at, int count)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = at; i < at + count; i++)
                c = PngTable[(c ^ b[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        static uint OggCrc(uint c, byte[] b, int count)
        {
            for (int i = 0; i < count; i++)
                c = (c << 8) ^ OggTable[((c >> 24) ^ b[i]) & 0xFF];
            return c;
        }

        static int Read(Stream f, byte[] b, int count = -1)
        {
            if (count < 0)
                count = b.Length;
            int got = 0;
            while (got < count)
            {
                int r = f.Read(b, got, count - got);
                if (r <= 0)
                    break;
                got += r;
            }
            return got;
        }

        static string Ascii(byte[] b, int at) => Encoding.ASCII.GetString(b, at, 4);

        static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        static bool Bad(string sentence, out string problem)
        {
            problem = sentence;
            return false;
        }

        static byte[] Refuse(string sentence, out string problem)
        {
            problem = sentence;
            return null;
        }
    }
}
