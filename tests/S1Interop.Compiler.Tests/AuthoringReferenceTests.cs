using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler.Tests;

internal static class AuthoringReferenceTests
{
    public static void Run(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "s1interop-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var author = CompilationSupport.Create("ModLibrary", "public class OriginalType { public int Field; }",
                CompilationSupport.PlatformReferences);
            var authored = AuthoringReferences.Create(author, new CSharpParseOptions(LanguageVersion.Latest));
            if (authored.Image.Length == 0) throw new InvalidOperationException("Failed to emit fixture reference.");
            string target = Path.Combine(directory, "ModLibrary.dll");
            var native = CompilationSupport.Create("ModLibrary", "public class TargetType { public int Property { get; set; } }",
                CompilationSupport.PlatformReferences).AddSyntaxTrees(authored.Stamp);
            using (var stream = File.Create(target))
            {
                var emitted = native.Emit(stream);
                if (!emitted.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
            }
            string companion = Path.Combine(directory, ".s1interop", "authoring", "ModLibrary.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(companion)!);
            if (scenario != "missing") File.WriteAllBytes(companion, authored.Image);
            if (scenario == "stale") File.AppendAllText(companion, "changed");
            string[] references = CompilationSupport.PlatformReferences.Select(reference => reference.Display!)
                .Append(target).ToArray();
            try
            {
                var resolved = AuthoringReferences.Resolve(references);
                if (scenario != "match") throw new InvalidOperationException("Invalid companion was accepted.");
                var probe = CSharpCompilation.Create("AuthorProbe", references: resolved.References);
                if (probe.GetTypeByMetadataName("OriginalType") is null || probe.GetTypeByMetadataName("TargetType") is not null ||
                    resolved.Companions.Single() != companion)
                    throw new InvalidOperationException("Original public API was not substituted.");
            }
            catch (AuthoringReferenceException) when (scenario is "stale" or "missing") { }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
