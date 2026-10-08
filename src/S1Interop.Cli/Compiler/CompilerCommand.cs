using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using S1Interop.Compiler;

internal static class CompilerCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("S1Interop experimental source compiler");
            Console.WriteLine("s1interop compiler lower --sources <list> --author-references <list> --target-references <list> --output <directory> [--assembly-name <name>] [--defines <semicolon-list>] [--lang-version <version>] [--nullable <context>]");
            Console.WriteLine("Lists contain one absolute file path per line. Original source files are never edited.");
            Console.WriteLine("s1interop compiler verify-installations --mono-game-path <install> --il2cpp-game-path <install> [--report <json>]");
            Console.WriteLine("s1interop compiler prepare-references --references <list> --output <directory>");
            return 0;
        }

        try
        {
            if (args[0] == "verify-installations")
                return InstallationVerification.Run(args[1..]);
            if (args[0] == "prepare-references")
                return ReferencePreparation.Run(args[1..]);
            if (args[0] != "lower")
                throw new ArgumentException($"Unknown command '{args[0]}'. Expected lower.");

            var options = ParseOptions(args[1..]);
            string outputDirectory = Path.GetFullPath(Required(options, "output"));
            string[] sources = ReadFileList(Required(options, "sources"));
            if (sources.Length == 0)
                throw new ArgumentException("The source list is empty.");

            var versionText = options.GetValueOrDefault("lang-version", "latest");
            if (!LanguageVersionFacts.TryParse(versionText, out var languageVersion))
                throw new ArgumentException($"Unknown C# language version '{versionText}'.");
            if (!Enum.TryParse<NullableContextOptions>(options.GetValueOrDefault("nullable", "disable"), true, out var nullableContext))
                throw new ArgumentException("Unknown nullable context. Expected disable, enable, warnings, or annotations.");
            var parseOptions = new CSharpParseOptions(languageVersion,
                preprocessorSymbols: options.GetValueOrDefault("defines", "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var trees = sources.Select(path => CSharpSyntaxTree.ParseText(
                SourceText.From(File.ReadAllText(path), Encoding.UTF8), parseOptions, path)).ToArray();
            string[] authorPaths = ReadFileList(Required(options, "author-references"));
            string[] targetPaths = ReadFileList(Required(options, "target-references"));
            var repairPlans = EventRepairPreparation.Create(authorPaths, targetPaths);
            var authorReferences = AuthoringReferences.Resolve(authorPaths);
            var author = CSharpCompilation.Create(options.GetValueOrDefault("assembly-name", "Mod"), trees,
                authorReferences.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: nullableContext));

            LoweringResult result = new InteropCompiler().Lower(author,
                References(Required(options, "target-references")));
            foreach (Diagnostic diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
                Console.Error.WriteLine(diagnostic.ToString());
            if (!result.Success)
                return 1;
            if (result.RuntimeAssembly.IsDefaultOrEmpty)
            {
                Console.Error.WriteLine("S1C909: IL2CPP support metadata is missing. Regenerate the install's interop assemblies before lowering source.");
                return 1;
            }
            if (repairPlans.Count != 0)
            {
                result = result with { Compilation = result.Compilation.AddSyntaxTrees(EventRepairPreparation.Initializer(repairPlans,
                    (CSharpParseOptions)result.Compilation.SyntaxTrees.First().Options)) };
                var repairErrors = result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                foreach (var error in repairErrors) Console.Error.WriteLine(error);
                if (repairErrors.Length != 0) return 1;
            }
            var authoring = AuthoringReferences.Create(author, (CSharpParseOptions)result.Compilation.SyntaxTrees.First().Options);
            foreach (var diagnostic in authoring.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                Console.Error.WriteLine(diagnostic);
            if (authoring.Image.Length == 0) return 1;
            result = result with { Compilation = result.Compilation.AddSyntaxTrees(authoring.Stamp) };

            // Prepare every destination before writing, so a bad path cannot overwrite author source.
            var sourcePaths = sources.ToHashSet(PathComparer);
            var outputs = result.Compilation.SyntaxTrees.Select((tree, index) =>
                (Tree: tree, Path: Path.Combine(outputDirectory, $"{index:D4}_{SafeName(tree.FilePath)}.g.cs")))
                .ToArray();
            string listPath = Path.Combine(outputDirectory, "sources.list");
            string runtimePath = Path.Combine(outputDirectory, "S1Interop.Runtime.dll");
            string authoringPath = Path.Combine(outputDirectory, "authoring.dll");
            string companionsPath = Path.Combine(outputDirectory, "authoring-references.list");
            foreach (string destination in outputs.Select(item => item.Path).Append(listPath).Append(runtimePath).Append(authoringPath).Append(companionsPath))
            {
                if (sourcePaths.Contains(Path.GetFullPath(destination)))
                    throw new ArgumentException($"Output would overwrite author input '{destination}'.");
            }

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(authoringPath, authoring.Image);
            File.WriteAllLines(companionsPath, authorReferences.Companions, new UTF8Encoding(false));
            if (!result.RuntimeAssembly.IsDefaultOrEmpty) File.WriteAllBytes(runtimePath, result.RuntimeAssembly.ToArray());
            foreach (var output in outputs)
            {
                string nullableDirective = languageVersion >= LanguageVersion.CSharp8
                    ? nullableContext switch
                    {
                        NullableContextOptions.Enable => "#nullable enable\n",
                        NullableContextOptions.Warnings => "#nullable disable\n#nullable enable warnings\n",
                        NullableContextOptions.Annotations => "#nullable disable\n#nullable enable annotations\n",
                        _ => "#nullable disable\n"
                    } : "";
                string sourceMapping = File.Exists(output.Tree.FilePath)
                    ? $"#line 1 {SyntaxFactory.Literal(output.Tree.FilePath).ToFullString()}\n" : "";
                File.WriteAllText(output.Path, nullableDirective + sourceMapping +
                    output.Tree.GetText().ToString(), new UTF8Encoding(false));
            }
            // Only the current invocation's manifest is consumed; stale generated files are never globbed.
            File.WriteAllLines(listPath, outputs.Select(item => item.Path), new UTF8Encoding(false));
            Console.WriteLine($"S1Interop: lowered {sources.Length} source files; {result.RewrittenNodes} transformations.");
            if (repairPlans.Count != 0) Console.WriteLine($"S1Interop: planned {repairPlans.Sum(p => p.Accessors.Count)} source-recognized event accessor repairs.");
            return 0;
        }
        catch (AuthoringReferenceException exception)
        {
            Console.Error.WriteLine($"S1C908: {exception.Message}");
            return 2;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
                                         UnauthorizedAccessException or BadImageFormatException)
        {
            Console.Error.WriteLine($"S1C900: {exception.Message}");
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "sources", "author-references", "target-references", "output", "assembly-name", "defines", "lang-version", "nullable"
        };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 == args.Length)
                throw new ArgumentException($"Expected an option and value at '{args[index]}'.");
            string key = args[index][2..];
            if (!known.Contains(key) || !result.TryAdd(key, args[index + 1]))
                throw new ArgumentException($"Unknown or duplicate option '--{key}'.");
        }
        return result;
    }

    private static string Required(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing --{key}." );

    private static string[] ReadFileList(string path)
    {
        string listPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(listPath)!;
        return File.ReadAllLines(listPath).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => Path.GetFullPath(line.Trim(), directory)).Distinct(PathComparer).ToArray();
    }

    private static IEnumerable<MetadataReference> References(string list) =>
        ReadFileList(list).Select(path => MetadataReference.CreateFromFile(path));

    private static string SafeName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return "Generated";
        return string.Concat(name.Select(character => char.IsLetterOrDigit(character) || character is '_' or '.' ? character : '_'));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
