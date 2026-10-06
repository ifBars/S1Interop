using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using S1Interop.Compiler;

internal static class ReferencePreparation
{
    public static int Run(string[] args)
    {
        if (args is not ["--references", var list, "--output", var output])
            throw new ArgumentException("Expected prepare-references --references <list> --output <directory>.");
        string listPath = Path.GetFullPath(list);
        string directory = Path.GetFullPath(output);
        string[] paths = File.ReadAllLines(listPath).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => Path.GetFullPath(line.Trim(), Path.GetDirectoryName(listPath)!)).Distinct().ToArray();
        var prepared = new List<string>();
        var resolutionDirectories = paths.Select(path => Path.GetDirectoryName(path)!).Distinct().ToArray();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        int generated = 0;
        foreach (string path in paths)
        {
            byte[] image = File.ReadAllBytes(path);
            using (var stream = new MemoryStream(image))
            using (var pe = new PEReader(stream))
            {
                var metadata = pe.GetMetadataReader();
                names.Add(metadata.GetString(metadata.GetAssemblyDefinition().Name));
            }
            string key = Convert.ToHexString(SHA256.HashData(image));
            string destination = Path.Combine(directory, GameReferencePublicizer.FormatVersion, key, Path.GetFileName(path));
            if (string.Equals(Path.GetFullPath(destination), path, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reference output cannot overwrite its input.");
            // Each image is keyed by exact source bytes and the transformation version, never timestamps.
            if (!File.Exists(destination))
            {
                byte[] reference = GameReferencePublicizer.CreateReference(image, resolutionDirectories);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, reference);
                generated++;
            }
            prepared.Add(destination);
        }
        Directory.CreateDirectory(directory);
        WriteChanged(Path.Combine(directory, "references.list"), string.Join(Environment.NewLine, prepared) + Environment.NewLine);
        WriteChanged(Path.Combine(directory, "Access.g.cs"), AccessSource(names));
        Console.WriteLine($"S1Interop: prepared {prepared.Count} local compiler references ({generated} regenerated).");
        return 0;
    }

    internal static string AccessSource(IEnumerable<string> names) =>
        string.Join("\n", names.Select(name => "[assembly: global::System.Runtime.CompilerServices.IgnoresAccessChecksTo(" +
            SyntaxFactory.Literal(name).ToFullString() + ")]")) + "\n" + """
        namespace System.Runtime.CompilerServices {
            [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
            internal sealed class IgnoresAccessChecksToAttribute : global::System.Attribute {
                public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
                public string AssemblyName { get; private set; }
            }
        }
        """;

    private static void WriteChanged(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content) File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
