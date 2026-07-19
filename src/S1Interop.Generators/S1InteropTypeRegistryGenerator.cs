using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace S1Interop.Generators;

/// <summary>
/// Generates S1Interop declarations, runtime registries, type facades, patch bindings, bridge helpers, and interop diagnostics for a consuming mod project.
/// </summary>
[Generator]
public sealed partial class S1InteropTypeRegistryGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Registers the incremental source and diagnostic pipelines used by the generator.
    /// </summary>
    /// <param name="context">The Roslyn incremental generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("S1InteropTypeAttribute.g.cs", SourceText.From(GenerateAttributeSource(), Encoding.UTF8)));

        IncrementalValueProvider<RuntimeBackend> runtimeProvider = context.AnalyzerConfigOptionsProvider
            .Combine(context.ParseOptionsProvider)
            .Select(static (input, _) => RuntimeBackendResolver.Resolve(input.Left, input.Right));

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> assemblyEntries = context.CompilationProvider
            .Select(static (compilation, _) => InteropDeclarationReader.GetAssemblyEntries(compilation));
        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> namespaceEntries = context.CompilationProvider
            .Select(static (compilation, _) => InteropDeclarationReader.GetNamespaceEntries(compilation));
        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> memberEntries = context.CompilationProvider
            .Select(static (compilation, _) => InteropDeclarationReader.GetAssemblyMemberEntries(compilation));
        IncrementalValueProvider<ImmutableArray<S1InteropPatchEntry>> patchEntries = context.CompilationProvider
            .Select(static (compilation, _) => InteropDeclarationReader.GetPatchEntries(compilation));
        IncrementalValueProvider<S1InteropBridgeRequests> bridgeRequests = context.CompilationProvider
            .Select(static (compilation, _) => InteropDeclarationReader.GetBridgeRequests(compilation));

        context.RegisterSourceOutput(context.CompilationProvider, static (sourceContext, compilation) =>
        {
            ReportDeclarationDiagnostics(sourceContext, compilation);
        });

        IncrementalValuesProvider<S1InteropTypeEntry> attributedTypeEntries = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (context, _) => InteropDeclarationReader.GetTypeEntry(context))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!.Value);

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> declaredEntries = assemblyEntries
            .Combine(namespaceEntries)
            .Select(static (input, _) => input.Left.AddRange(input.Right));

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> patchTypeEntries = patchEntries
            .Select(static (entries, _) => entries
                .Select(entry => entry.OwnerEntry)
                .Distinct(S1InteropTypeEntryComparer.Instance)
                .ToImmutableArray());

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> allEntries = declaredEntries
            .Combine(attributedTypeEntries.Collect())
            .Combine(patchTypeEntries)
            .Select(static (input, _) => InteropDeclarationReader.MergeTypeEntries(input.Left.Left.AddRange(input.Left.Right).AddRange(input.Right)));

        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> explicitMemberEntries = memberEntries
            .Combine(patchEntries)
            .Select(static (input, _) => input.Left
                .AddRange(input.Right.Select(entry => entry.TargetMemberEntry))
                .Distinct(S1InteropMemberEntryComparer.Instance)
                .ToImmutableArray());

        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> allMemberEntries = context.CompilationProvider
            .Combine(allEntries)
            .Combine(explicitMemberEntries)
            .Select(static (input, _) => PublicMemberCatalog.MergeMemberEntries(
                PublicMemberCatalog.EnrichMemberEntries(input.Left.Left, input.Left.Right, input.Right),
                PublicMemberCatalog.DiscoverMemberEntries(input.Left.Left, input.Left.Right)));

        IncrementalValueProvider<ImmutableArray<S1InteropConstructorEntry>> allConstructorEntries = context.CompilationProvider
            .Combine(allEntries)
            .Select(static (input, _) => PublicMemberCatalog.DiscoverConstructorEntries(input.Left, input.Right));

        IncrementalValueProvider<ImmutableArray<S1InteropEnumEntry>> allEnumEntries = context.CompilationProvider
            .Combine(allEntries)
            .Select(static (input, _) => PublicEnumCatalog.DiscoverEnumEntries(input.Left, input.Right));

        context.RegisterSourceOutput(runtimeProvider.Combine(allEntries).Combine(allMemberEntries).Combine(allConstructorEntries).Combine(allEnumEntries), static (sourceContext, input) =>
        {
            sourceContext.AddSource(
                "S1Interop.TypeRegistry.g.cs",
                SourceText.From(GenerateRegistrySource(input.Left.Left.Left.Left, input.Left.Left.Left.Right, input.Left.Left.Right, input.Left.Right, input.Right), Encoding.UTF8));
        });
        context.RegisterSourceOutput(patchEntries, static (sourceContext, entries) =>
        {
            if (!entries.IsDefaultOrEmpty)
            {
                sourceContext.AddSource(
                    "S1Interop.HarmonyPatcher.g.cs",
                    SourceText.From(GenerateHarmonyPatchSource(entries), Encoding.UTF8));
            }
        });
        context.RegisterSourceOutput(bridgeRequests, static (sourceContext, requests) =>
        {
            if (requests.GenerateUnityEventBridge)
            {
                sourceContext.AddSource(
                    "S1Interop.UnityEventBridge.g.cs",
                    SourceText.From(GenerateUnityEventBridgeSource(), Encoding.UTF8));
            }

            if (requests.GenerateDelegateEventBridge)
            {
                sourceContext.AddSource(
                    "S1Interop.DelegateEventBridge.g.cs",
                    SourceText.From(GenerateDelegateEventBridgeSource(), Encoding.UTF8));
            }
        });
    }
}
