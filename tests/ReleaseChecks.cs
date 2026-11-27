using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

// Checks of what is shipped, run on built files: `--assembly <ShorelandsMods.dll>` after the build and
// `--package <zip>` on the release zip (tools/package.ps1).
static partial class Program
{
    static int Release(string what, Action run)
    {
        run();
        Console.WriteLine((failed == 0 ? "ALL PASS" : failed + " FAILED") + " (" + what + ", " + passed + " checks)");
        return failed == 0 ? 0 : 1;
    }

    // Traces of the machine that built the files: the source folder and the user's home folder.
    static IEnumerable<string> BuildMachinePaths()
    {
        string repo = Files.RepoRoot();
        if (repo != null)
            yield return Path.GetFullPath(repo).TrimEnd('\\', '/');
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            yield return home.TrimEnd('\\', '/');
    }

    static readonly Regex KnownReference = new Regex(
        @"^(mscorlib|netstandard|System(\..+)?|UnityEngine(\..+)?|Unity\..+|Game|Mirror|Newtonsoft\.Json|Sirenix\..+|BepInEx|0Harmony|Lua|Microsoft\.Bcl\..+)$"
    );

    // The host DLL: references only the game, Unity, BepInEx and the bundled libraries, and has no test entry point.
    static void HostAssembly(string dll)
    {
        using var pe = new PEReader(File.OpenRead(dll));
        var md = pe.GetMetadataReader();
        string S(StringHandle h) => md.GetString(h);

        var refs = md.AssemblyReferences.Select(h => S(md.GetAssemblyReference(h).Name)).ToList();
        Check(refs.Contains("Lua") && refs.Contains("BepInEx"), "assembly references are read", string.Join(",", refs));
        foreach (var r in refs)
            Check(KnownReference.IsMatch(r), "a known assembly reference", r);

        var publicTypes = new List<string>();
        foreach (var th in md.TypeDefinitions)
        {
            var t = md.GetTypeDefinition(th);
            string name = S(t.Namespace) + "." + S(t.Name);
            Check(!name.Contains("Test"), "no test type", name);
            var vis = t.Attributes & TypeAttributes.VisibilityMask;
            if (vis == TypeAttributes.Public)
                publicTypes.Add(name);
            bool plugin =
                !t.BaseType.IsNil
                && t.BaseType.Kind == HandleKind.TypeReference
                && S(md.GetTypeReference((TypeReferenceHandle)t.BaseType).Name) == "BaseUnityPlugin";
            foreach (var mh in t.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                string mn = S(m.Name);
                Check(!mn.Contains("Test"), "no test method", name + "." + mn);
                if (plugin)
                {
                    Check(
                        (m.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public || mn == ".ctor",
                        "the plugin class has no public method",
                        mn
                    );
                    Check(
                        m.DecodeSignature(new ParamCount(), null).ParameterTypes.Length == 0,
                        "no plugin method takes an argument (a test entry point takes the command text)",
                        mn
                    );
                }
            }
            if (plugin)
                foreach (var fh in t.GetFields())
                    Check(
                        (md.GetFieldDefinition(fh).Attributes & FieldAttributes.FieldAccessMask)
                            != FieldAttributes.Public,
                        "the plugin class has no public field",
                        S(md.GetFieldDefinition(fh).Name)
                    );
        }
        Check(
            publicTypes.SequenceEqual(new[] { "ShorelandsMods.Game.ModsPlugin" }),
            "the plugin class is the only public type",
            string.Join(", ", publicTypes)
        );

        var bytes = File.ReadAllBytes(dll);
        foreach (var p in BuildMachinePaths())
            Check(!Contains(bytes, p), "the DLL does not contain the build machine's path " + p);
    }

    static readonly string[] Examples =
    {
        "chat_helper",
        "example",
        "icon_pack",
        "koi_checklist",
        "offering_reminder",
        "session_stats",
        "sound_pack",
        "stream_ticker",
        "ui_skin",
    };

    static readonly Regex[] Allowed =
    {
        new Regex(@"^ShorelandsMods-README\.txt$"),
        new Regex(
            @"^BepInEx/plugins/ShorelandsMods/(ShorelandsMods|Lua|Microsoft\.Bcl\.TimeProvider|Microsoft\.Bcl\.AsyncInterfaces)\.dll$"
        ),
        new Regex(@"^BepInEx/plugins/ShorelandsMods/(THIRD-PARTY-NOTICES|LICENSE)\.txt$"),
        new Regex(
            @"^BepInEx/plugins/ShorelandsMods/licenses/(Lua-CSharp|dotnet-runtime)\.(LICENSE|THIRD-PARTY-NOTICES)\.txt$"
        ),
        new Regex(@"^mods/_guide\.html$"),
        new Regex(
            @"^mods/(" + string.Join("|", Examples) + @")/([A-Za-z0-9_]+/)*[A-Za-z0-9_.-]+\.(lua|xml|txt|png|ogg|wav)$"
        ),
    };

    // Shipped as published on nuget.org (SHA-256), so they carry their builders' paths and authors' names.
    static readonly Dictionary<string, string> ThirdParty = new Dictionary<string, string>
    {
        ["BepInEx/plugins/ShorelandsMods/Lua.dll"] = "2494e456e1b4d13d77665e5d3725b3eded4b932f05d8b4b38492a3f98eb2cb7c",
        ["BepInEx/plugins/ShorelandsMods/Microsoft.Bcl.AsyncInterfaces.dll"] =
            "136d5965cf4768e8420b547a8bddea882921f426c371833d558a858a2f0c235a",
        ["BepInEx/plugins/ShorelandsMods/Microsoft.Bcl.TimeProvider.dll"] =
            "d9941b9603506c46e2aaf8b44166c93646b55ebea0d365792b405c2562753442",
        ["BepInEx/plugins/ShorelandsMods/licenses/dotnet-runtime.LICENSE.txt"] =
            "d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b",
        ["BepInEx/plugins/ShorelandsMods/licenses/dotnet-runtime.THIRD-PARTY-NOTICES.txt"] =
            "19c19dcac9f3ee6302cfbc6745bb8e79f08efc4c933eb8dc5509bf14b88347ec",
    };

    // The release zip: only files on the allow-list, everything a player needs, no build machine path in any file.
    static void Package(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        var names = z.Entries.Select(e => e.FullName).ToList();
        foreach (var n in names)
            Check(Allowed.Any(r => r.IsMatch(n)), "file on the allow-list", n);
        var required = new List<string>
        {
            "ShorelandsMods-README.txt",
            "BepInEx/plugins/ShorelandsMods/ShorelandsMods.dll",
            "BepInEx/plugins/ShorelandsMods/Lua.dll",
            "BepInEx/plugins/ShorelandsMods/Microsoft.Bcl.TimeProvider.dll",
            "BepInEx/plugins/ShorelandsMods/Microsoft.Bcl.AsyncInterfaces.dll",
            "BepInEx/plugins/ShorelandsMods/THIRD-PARTY-NOTICES.txt",
            "BepInEx/plugins/ShorelandsMods/LICENSE.txt",
            "BepInEx/plugins/ShorelandsMods/licenses/Lua-CSharp.LICENSE.txt",
            "BepInEx/plugins/ShorelandsMods/licenses/dotnet-runtime.LICENSE.txt",
            "BepInEx/plugins/ShorelandsMods/licenses/dotnet-runtime.THIRD-PARTY-NOTICES.txt",
            "mods/_guide.html",
        };
        required.AddRange(Examples.Select(e => "mods/" + e + "/mod.xml"));
        foreach (var r in required)
            Check(names.Contains(r), "the package has " + r);
        foreach (var e in z.Entries)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            if (ThirdParty.TryGetValue(e.FullName, out var sha))
            {
                Check(
                    Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) == sha,
                    e.FullName + " is the pinned NuGet file"
                );
                continue;
            }
            foreach (var p in BuildMachinePaths())
                Check(!Contains(bytes, p), e.FullName + " does not contain the build machine's path " + p);
        }
    }

    // ASCII/UTF-8 and UTF-16 (the DLLs' user strings), case-insensitive.
    static bool Contains(byte[] bytes, string text)
    {
        string a = Encoding.Latin1.GetString(bytes);
        string w = Encoding.Unicode.GetString(bytes) + Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1);
        return a.Contains(text, StringComparison.OrdinalIgnoreCase)
            || w.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    sealed class ParamCount : ISignatureTypeProvider<int, object>
    {
        public int GetArrayType(int e, ArrayShape s) => 0;

        public int GetByReferenceType(int e) => 0;

        public int GetFunctionPointerType(MethodSignature<int> s) => 0;

        public int GetGenericInstantiation(int g, System.Collections.Immutable.ImmutableArray<int> a) => 0;

        public int GetGenericMethodParameter(object c, int i) => 0;

        public int GetGenericTypeParameter(object c, int i) => 0;

        public int GetModifiedType(int m, int u, bool r) => 0;

        public int GetPinnedType(int e) => 0;

        public int GetPointerType(int e) => 0;

        public int GetPrimitiveType(PrimitiveTypeCode t) => 0;

        public int GetSZArrayType(int e) => 0;

        public int GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => 0;

        public int GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => 0;

        public int GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => 0;
    }
}
