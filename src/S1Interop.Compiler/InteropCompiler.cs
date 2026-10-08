using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Lowers author source bound against one runtime's metadata into a compilation against another runtime's references.
/// </summary>
public sealed class InteropCompiler
{
    /// <summary>
    /// Rewrites every author syntax tree with the author semantic model and binds the result against
    /// <paramref name="targetReferences"/>. Shared runtime support is emitted; the mod remains an un-emitted compilation.
    /// </summary>
    /// <param name="authorCompilation">The compilation the author wrote and the IDE binds against.</param>
    /// <param name="targetReferences">The complete reference set of the target runtime.</param>
    /// <param name="cancellationToken">Cancels analysis and rewriting.</param>
    /// <returns>The lowered target compilation, all diagnostics, and the number of rewritten nodes.</returns>
    public LoweringResult Lower(
        CSharpCompilation authorCompilation,
        IEnumerable<MetadataReference> targetReferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorCompilation);
        ArgumentNullException.ThrowIfNull(targetReferences);

        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(authorCompilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        CSharpCompilation target = CSharpCompilation.Create(
            authorCompilation.AssemblyName,
            syntaxTrees: null,
            targetReferences,
            authorCompilation.Options);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            // Invalid author programs can contain cyclic constraints and incomplete symbols. Bind failures
            // are the actionable result; do not attempt transformations against an invalid semantic graph.
            return new LoweringResult(target.AddSyntaxTrees(authorCompilation.SyntaxTrees), diagnostics.ToImmutable(), 0);
        }
        // Replace an existing support reference with this build's ABI rather than introducing duplicate types.
        target = target.RemoveReferences(target.References.Where(reference =>
            target.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol { Name: RuntimeSupportBuilder.AssemblyName }).ToArray());
        var map = new MetadataSymbolMap(authorCompilation, target);
        ImmutableArray<byte> runtimeAssembly = [];
        string runtimeSignature = string.Empty;
        if (map.NativeObjectBase is not null)
        {
            try
            {
                var support = RuntimeSupportBuilder.Build(target, map, cancellationToken);
                diagnostics.AddRange(support.Diagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
                if (support.Image.IsEmpty) return new LoweringResult(target, diagnostics.ToImmutable(), 0);
                runtimeAssembly = support.Image;
                runtimeSignature = support.Signature;
                target = target.AddReferences(MetadataReference.CreateFromImage(runtimeAssembly, filePath: RuntimeSupportBuilder.FileName));
                map = new MetadataSymbolMap(authorCompilation, target);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(Diagnostic.Create(new DiagnosticDescriptor("S1IC031", "Native support runtime unavailable",
                    "{0}", "S1Interop.Compiler", DiagnosticSeverity.Error, true), Location.None, exception.Message));
                return new LoweringResult(target, diagnostics.ToImmutable(), 0);
            }
        }
        var enumerationAdapters = new EnumerationAdapters(map);
        var collectionStorage = new CollectionStorageAnalysis(authorCompilation, map, cancellationToken);

        var trees = new List<SyntaxTree>();
        int rewrittenNodes = 0;
        foreach (SyntaxTree tree in authorCompilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.AddRange(NativeReflectionVerifier.Verify(authorCompilation.GetSemanticModel(tree), map, cancellationToken));
            diagnostics.AddRange(NativeTraverseLowering.Verify(authorCompilation.GetSemanticModel(tree), map, cancellationToken));
            var rewriter = new SemanticLoweringRewriter(authorCompilation.GetSemanticModel(tree), map, diagnostics, enumerationAdapters, collectionStorage);
            SyntaxNode root = rewriter.Rewrite(tree.GetRoot(cancellationToken));
            rewrittenNodes += rewriter.RewrittenNodes;

            // Reparse rather than reuse rewritten nodes so the target binder sees ordinary parser output;
            // trivia is preserved, so line numbers in the original path remain stable.
            trees.Add(rewriter.RewrittenNodes == 0
                ? tree
                : CSharpSyntaxTree.ParseText(
                    root.ToFullString(),
                    (CSharpParseOptions)tree.Options,
                    tree.FilePath,
                    tree.Encoding ?? Encoding.UTF8,
                    cancellationToken));
        }

        if (map.NativeObjectBase is not null)
        {
            CSharpParseOptions parseOptions = authorCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions
                ?? CSharpParseOptions.Default;
            if (enumerationAdapters.CreateTree(parseOptions, cancellationToken) is { } enumeration)
                trees.Add(enumeration);
            // Original source was already checked under the author's language version. Generated runtime
            // initialization requires C# 9, so only the lowered compilation raises the language floor.
            var targetOptions = parseOptions.LanguageVersion < LanguageVersion.CSharp9
                ? parseOptions.WithLanguageVersion(LanguageVersion.CSharp9) : parseOptions;
            trees = trees.Select(tree => tree.WithRootAndOptions(tree.GetRoot(cancellationToken), targetOptions)).ToList();
            if (ComparerAdapterGeneration.CreateTree(authorCompilation, map, collectionStorage, targetOptions,
                    runtimeSignature, cancellationToken) is { } comparers)
                trees.Add(comparers);
            trees.Add(RuntimeSupportBuilder.CreateModGuard(runtimeSignature, authorCompilation.AssemblyName!, targetOptions, target));
        }

        target = target.AddSyntaxTrees(trees);
        diagnostics.AddRange(target.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        if (map.NativeObjectBase is not null)
            diagnostics.AddRange(NativeArrayBoundaryVerifier.Verify(target, cancellationToken));

        return new LoweringResult(target, diagnostics.ToImmutable(), rewrittenNodes, runtimeAssembly);
    }
}

/// <summary>
/// The outcome of <see cref="InteropCompiler.Lower"/>.
/// </summary>
/// <param name="Compilation">The lowered compilation bound against the target references.</param>
/// <param name="Diagnostics">Author errors, lowering diagnostics, and target warnings/errors.</param>
/// <param name="RewrittenNodes">The number of syntax transformations applied.</param>
/// <param name="RuntimeAssembly">The shared S1Interop.Runtime.dll image for an IL2CPP target, or an empty array.</param>
public sealed record LoweringResult(CSharpCompilation Compilation, ImmutableArray<Diagnostic> Diagnostics, int RewrittenNodes,
    ImmutableArray<byte> RuntimeAssembly = default)
{
    /// <summary>
    /// Gets whether no diagnostic has error severity.
    /// </summary>
    public bool Success => !Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
