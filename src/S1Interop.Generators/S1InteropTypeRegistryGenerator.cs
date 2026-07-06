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

[Generator]
public sealed partial class S1InteropTypeRegistryGenerator : IIncrementalGenerator
{
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

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> allEntries = declaredEntries
            .Combine(attributedTypeEntries.Collect())
            .Select(static (input, _) => InteropDeclarationReader.MergeTypeEntries(input.Left.AddRange(input.Right)));

        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> allMemberEntries = context.CompilationProvider
            .Combine(allEntries)
            .Combine(memberEntries)
            .Select(static (input, _) => PublicMemberCatalog.MergeMemberEntries(
                input.Right,
                PublicMemberCatalog.DiscoverMemberEntries(input.Left.Left, input.Left.Right)));

        IncrementalValueProvider<ImmutableArray<S1InteropConstructorEntry>> allConstructorEntries = context.CompilationProvider
            .Combine(allEntries)
            .Select(static (input, _) => PublicMemberCatalog.DiscoverConstructorEntries(input.Left, input.Right));

        context.RegisterSourceOutput(runtimeProvider.Combine(allEntries).Combine(allMemberEntries).Combine(allConstructorEntries), static (sourceContext, input) =>
        {
            sourceContext.AddSource(
                "S1Interop.TypeRegistry.g.cs",
                SourceText.From(GenerateRegistrySource(input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right), Encoding.UTF8));
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
