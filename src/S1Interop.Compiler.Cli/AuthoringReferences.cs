using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

/// <summary>Pairs lowered dependencies with their original author-facing metadata without loading assemblies.</summary>
internal static class AuthoringReferences
{
    internal const string HashKey = "S1Interop.AuthoringSha256";
    private const string MetadataAttribute = "System.Reflection.AssemblyMetadataAttribute";

    public static (MetadataReference[] References, string[] Companions) Resolve(string[] paths)
    {
        var references = paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        var inspection = CSharpCompilation.Create("S1Interop.ReferenceInspection", references: references);
        var companions = new List<string>();
        var authorReferences = new List<MetadataReference>();
        for (int index = 0; index < references.Length; index++)
        {
            if (inspection.GetAssemblyOrModuleSymbol(references[index]) is not IAssemblySymbol assembly) continue;
            if (assembly.Name == "S1Interop.Runtime") continue;
            var stamps = assembly.GetAttributes().Where(attribute =>
                attribute.AttributeClass?.ToDisplayString() == MetadataAttribute &&
                attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[0].Value as string == HashKey).ToArray();
            if (stamps.Length == 0)
            {
                if (assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Any(reference => reference.Name == "S1Interop.Runtime")))
                    throw new AuthoringReferenceException($"Dependency '{paths[index]}' references S1Interop.Runtime but has no authoring metadata stamp. Rebuild it with the source compiler.");
                authorReferences.Add(references[index]);
                continue;
            }
            string companion = Path.Combine(Path.GetDirectoryName(paths[index])!, ".s1interop", "authoring", Path.GetFileName(paths[index]));
            if (stamps.Length != 1 || stamps[0].ConstructorArguments[1].Value is not string expected ||
                !File.Exists(companion) || !string.Equals(Hash(File.ReadAllBytes(companion)), expected, StringComparison.Ordinal))
                throw new AuthoringReferenceException($"Dependency '{paths[index]}' has missing or stale authoring metadata. Rebuild it with the source compiler and retain its .s1interop/authoring directory.");
            var reference = MetadataReference.CreateFromFile(companion);
            var paired = CSharpCompilation.Create("S1Interop.PairInspection", references: [reference]);
            if (paired.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol original || !original.Identity.Equals(assembly.Identity))
                throw new AuthoringReferenceException($"Dependency '{paths[index]}' has authoring metadata with a different assembly identity.");
            authorReferences.Add(reference);
            companions.Add(companion);
        }
        return (authorReferences.ToArray(), companions.ToArray());
    }

    public static (byte[] Image, SyntaxTree Stamp, IEnumerable<Diagnostic> Diagnostics) Create(CSharpCompilation author,
        CSharpParseOptions parseOptions)
    {
        if (author.Assembly.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == MetadataAttribute &&
            attribute.ConstructorArguments.FirstOrDefault().Value as string == HashKey))
            throw new AuthoringReferenceException($"Assembly metadata key '{HashKey}' is reserved for the compiler.");
        using var output = new MemoryStream();
        var result = author.WithOptions(author.Options.WithDeterministic(true)).Emit(output,
            options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
        byte[] image = result.Success ? output.ToArray() : [];
        var stamp = CSharpSyntaxTree.ParseText(
            $"[assembly: global::System.Reflection.AssemblyMetadata(\"{HashKey}\", \"{Hash(image)}\")]",
            parseOptions, "S1Interop.Compiler.Authoring.g.cs", System.Text.Encoding.UTF8);
        return (image, stamp, result.Diagnostics);
    }

    private static string Hash(byte[] image) => Convert.ToHexString(SHA256.HashData(image));
}

internal sealed class AuthoringReferenceException(string message) : Exception(message);
