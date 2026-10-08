using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class InstallationVerification
{
    public static int Run(string[] args)
    {
        if (args.Length is not (4 or 6) || args[0] != "--mono-game-path" || args[2] != "--il2cpp-game-path" ||
            (args.Length == 6 && args[4] != "--report"))
            throw new ArgumentException("verify-installations --mono-game-path <install> --il2cpp-game-path <install> [--report <json>]");
        var mono = Inspect(args[1], native: false);
        var native = Inspect(args[3], native: true);
        bool match = mono.Version == native.Version;
        var freshness = InspectGenerationRecord(args[3]);
        if (args.Length == 6)
        {
            string path = Path.GetFullPath(args[5]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { Match = match, Mono = mono, Il2Cpp = native, Generation = freshness },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"Mono game version: {mono.Version}; IL2CPP game version: {native.Version}.");
        if (!match)
        {
            Console.Error.WriteLine("S1C905: The game versions differ. Use matching alternate/alternate-beta and public/beta installations before claiming cross-runtime compatibility.");
            return 1;
        }
        if (!freshness.Match)
        {
            Console.Error.WriteLine("S1C907: The IL2CPP generation record is absent or does not match GameAssembly.dll. Launch this install with MelonLoader to regenerate its interop assemblies before building.");
            return 1;
        }
        Console.WriteLine("Game versions match and MelonLoader's generation record matches the native image. Metadata fingerprints recorded; runtime behavior requires separate validation.");
        return 0;
    }

    private static GenerationIdentity InspectGenerationRecord(string root)
    {
        string image = Path.Combine(root, "GameAssembly.dll");
        string config = Path.Combine(root, "MelonLoader", "Dependencies", "Il2CppAssemblyGenerator", "Config.cfg");
        string? actual = null;
        if (File.Exists(image))
        {
            using var stream = File.OpenRead(image);
            actual = Convert.ToHexString(SHA512.HashData(stream));
        }
        string[] records = File.Exists(config)
            ? GeneratorHashPattern().Matches(File.ReadAllText(config)).Cast<Match>().Select(match => match.Groups[1].Value).ToArray()
            : [];
        string? recorded = records.Length == 1 ? records[0] : null;
        return new GenerationIdentity(actual, recorded,
            actual is not null && recorded is not null && string.Equals(actual, recorded, StringComparison.OrdinalIgnoreCase));
    }

    private static InstallationIdentity Inspect(string root, bool native)
    {
        root = Path.GetFullPath(root);
        string settings = Path.Combine(root, "Schedule I_Data", "globalgamemanagers");
        string assembly = Path.Combine(root, native ? "MelonLoader/Il2CppAssemblies" : "Schedule I_Data/Managed", "Assembly-CSharp.dll");
        // Unity serializes bundleVersion in PlayerSettings. Extract only the recognizable version;
        // do not depend on a potentially stale loader log or infer a version from directory names.
        string[] versions = GameVersionPattern().Matches(Encoding.UTF8.GetString(File.ReadAllBytes(settings)))
            .Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (versions.Length != 1)
            throw new ArgumentException($"Cannot identify a unique game version in '{settings}'. Found {versions.Length} candidates.");
        using var stream = File.OpenRead(assembly);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        Guid mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        return new InstallationIdentity(versions[0], assembly, hash, mvid);
    }

    [GeneratedRegex(@"\b0\.\d+\.\d+f\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex GameVersionPattern();

    [GeneratedRegex("(?m)^\\s*GameAssemblyHash\\s*=\\s*\"([A-Fa-f0-9]{128})\"\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratorHashPattern();

    private sealed record InstallationIdentity(string Version, string AssemblyPath, string Sha256, Guid ModuleVersionId);
    private sealed record GenerationIdentity(string? NativeImageSha512, string? RecordedImageSha512, bool Match);
}
