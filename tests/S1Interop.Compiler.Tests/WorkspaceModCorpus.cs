using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using S1Interop.Compiler;

internal static class WorkspaceModCorpus
{
    // Deliberately supports explicit source manifests only. Evaluating or building a foreign
    // project can run its deployment hooks; silently guessing Compile conditions hides gaps.
    public static int Verify(string projectPath, string authorList, string targetList, string reportPath, string defines,
        string? sourceList = null)
    {
        projectPath = Path.GetFullPath(projectPath);
        string root = Path.GetDirectoryName(projectPath)!;
        var project = XDocument.Load(projectPath);
        var compileItems = project.Descendants().Where(element => element.Name.LocalName == "Compile").ToArray();
        if (sourceList is null && (!project.Descendants().Any(element => element.Name.LocalName == "EnableDefaultCompileItems" && element.Value == "false") ||
            compileItems.Length == 0 || compileItems.Any(element => element.Attribute("Include") is null ||
                element.AncestorsAndSelf().Any(parent => parent.Attribute("Condition") is not null) ||
                element.Attribute("Include")!.Value.IndexOfAny(['?', '$', ';']) >= 0)))
            throw new ArgumentException("Corpus requires an unconditional, explicit Compile manifest with default items disabled.");

        var files = sourceList is null
            ? compileItems.SelectMany(element => Expand(root, element.Attribute("Include")!.Value)).Distinct().ToArray()
            : ReadList(sourceList);
        if (files.Length == 0) throw new ArgumentException("Corpus source manifest is empty.");
        if (files.Any(path => Path.GetRelativePath(root, path).StartsWith("..", StringComparison.Ordinal)))
            throw new ArgumentException("Corpus source files must be inside the mod project directory.");
        var hashes = files.ToDictionary(path => path, Hash);
        string sandbox = Path.Combine(Path.GetTempPath(), "S1Interop-mod-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var diagnostics = new List<string>();
        string stage = "author";
        int transformations = 0;
        bool passed = false;
        try
        {
            var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
            var trees = files.Select(path =>
            {
                string copy = Path.Combine(sandbox, Path.GetRelativePath(root, path));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(path, copy);
                return CSharpSyntaxTree.ParseText(File.ReadAllText(copy), parse, Path.GetRelativePath(root, path));
            }).ToList();
            if (project.Descendants().Any(element => element.Name.LocalName == "ImplicitUsings" && element.Value == "enable"))
                trees.Add(CSharpSyntaxTree.ParseText("""
                    global using System;
                    global using System.Collections.Generic;
                    global using System.IO;
                    global using System.Linq;
                    global using System.Net.Http;
                    global using System.Threading;
                    global using System.Threading.Tasks;
                    """, parse, "Corpus.GlobalUsings.g.cs"));
            var authorReferences = AuthoringReferences.Resolve(ReadList(authorList));
            var nullable = project.Descendants().LastOrDefault(element => element.Name.LocalName == "Nullable" &&
                !element.AncestorsAndSelf().Any(parent => parent.Attribute("Condition") is not null))?.Value == "enable"
                ? NullableContextOptions.Enable : NullableContextOptions.Disable;
            var author = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(projectPath), trees,
                authorReferences.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: nullable));
            using var authorImage = new MemoryStream();
            var authorEmit = author.Emit(authorImage);
            diagnostics.AddRange(authorEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            if (!authorEmit.Success) return 1;
            stage = "lowering";
            var result = new InteropCompiler().Lower(author, ReadList(targetList).Select(path => MetadataReference.CreateFromFile(path)));
            transformations = result.RewrittenNodes;
            diagnostics.AddRange(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()));
            if (!result.Success) return 1;
            stage = "target-emission";
            using var nativeImage = new MemoryStream();
            var emitted = result.Compilation.Emit(nativeImage);
            diagnostics.AddRange(emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            passed = emitted.Success;
            return passed ? 0 : 1;
        }
        finally
        {
            bool unchanged = hashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value);
            var report = new
            {
                Project = projectPath, Passed = passed && unchanged, Stage = stage,
                Evidence = "Compile only; unchanged Mono branch for both backends. No mod or project targets executed.",
                Defines = defines.Split(';'), LanguageVersion = "latest", SourceFiles = hashes, SourcesUnchanged = unchanged,
                SourceSelection = sourceList is null ? "Project explicit Compile manifest" : "Caller-supplied audited source manifest",
                AuthorReferences = ReadList(authorList).ToDictionary(path => path, Hash),
                TargetReferences = ReadList(targetList).ToDictionary(path => path, Hash),
                Transformations = transformations, Diagnostics = diagnostics
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Delete(sandbox, recursive: true);
            foreach (string diagnostic in diagnostics) Console.WriteLine(diagnostic);
            Console.WriteLine($"{(passed && unchanged ? "PASS" : "FAIL")} {Path.GetFileNameWithoutExtension(projectPath)}: {stage}, {files.Length} original files, {transformations} transformations. Compile evidence only.");
            if (!unchanged) throw new InvalidOperationException("Original mod sources changed during corpus verification.");
        }
    }

    private static IEnumerable<string> Expand(string root, string include)
    {
        string path = Path.GetFullPath(Path.Combine(root, include.Replace('\\', Path.DirectorySeparatorChar)));
        if (!include.Contains('*')) return [path];
        string directory = Path.GetDirectoryName(path)!;
        if (directory.Contains('*') || Path.GetFileName(path) != "*.cs")
            throw new ArgumentException("Corpus supports only explicit files and single-directory *.cs globs.");
        return Directory.GetFiles(directory, "*.cs").Order(StringComparer.Ordinal);
    }

    private static string[] ReadList(string path) => File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
