using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace S1Interop.Generators;

[Generator]
public sealed partial class S1InteropTypeRegistryGenerator : IIncrementalGenerator
{
    private static readonly string[] DefaultIl2CppRuntimeProbeTypeNames =
    [
        "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase",
        "Il2CppSystem.Object",
        "Il2CppScheduleOne.GameManager"
    ];

    private static readonly string[] DefaultIl2CppRuntimeProbeAssemblyNames =
    [
        "Il2CppInterop.Runtime",
        "Il2Cppmscorlib",
        "Il2CppAssembly-CSharp"
    ];

    private static readonly string[] DefaultMonoRuntimeProbeTypeNames =
    [
        "ScheduleOne.GameManager",
        "ScheduleOne.PlayerScripts.PlayerCamera"
    ];

    private static readonly string[] DefaultMonoRuntimeProbeAssemblyNames =
    [
        "Assembly-CSharp"
    ];

    private static readonly string[] DefaultGameAssemblyProbeNames =
    [
        "Assembly-CSharp",
        "Il2CppAssembly-CSharp",
        "Il2CppScheduleOne.Core"
    ];

    private const string AttributeMetadataName = "S1Interop.S1InteropTypeAttribute";
    private const string MemberAttributeMetadataName = "S1Interop.S1InteropMemberAttribute";
    private const string UnityEventBridgeAttributeMetadataName = "S1Interop.S1InteropGenerateUnityEventBridgeAttribute";
    private const string DelegateEventBridgeAttributeMetadataName = "S1Interop.S1InteropGenerateDelegateEventBridgeAttribute";

#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor TypeNotFoundDiagnostic = new(
        "S1I001",
        "S1Interop type was not found",
        "S1Interop type '{0}' for alias '{1}' was not found in referenced {2} assemblies",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MemberOwnerNotFoundDiagnostic = new(
        "S1I002",
        "S1Interop member owner was not declared",
        "S1Interop member '{0}' references owner alias '{1}', but no S1InteropType declaration with that alias was found",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MemberNotFoundDiagnostic = new(
        "S1I003",
        "S1Interop member was not found",
        "S1Interop member '{0}' for owner alias '{1}' was not found on type '{2}' in referenced {3} assemblies",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("S1InteropTypeAttribute.g.cs", SourceText.From(GenerateAttributeSource(), Encoding.UTF8)));

        IncrementalValueProvider<RuntimeBackend> runtimeProvider = context.AnalyzerConfigOptionsProvider
            .Combine(context.ParseOptionsProvider)
            .Select(static (input, _) => ResolveRuntime(input.Left, input.Right));

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> assemblyEntries = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyEntries(compilation));
        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> memberEntries = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyMemberEntries(compilation));
        IncrementalValueProvider<S1InteropBridgeRequests> bridgeRequests = context.CompilationProvider
            .Select(static (compilation, _) => GetBridgeRequests(compilation));

        context.RegisterSourceOutput(context.CompilationProvider, static (sourceContext, compilation) =>
        {
            ReportDeclarationDiagnostics(sourceContext, compilation);
        });

        IncrementalValuesProvider<S1InteropTypeEntry> attributedTypeEntries = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (context, _) => GetTypeEntry(context))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!.Value);

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> allEntries = assemblyEntries
            .Combine(attributedTypeEntries.Collect())
            .Select(static (input, _) => MergeTypeEntries(input.Left.AddRange(input.Right)));

        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> allMemberEntries = context.CompilationProvider
            .Combine(allEntries)
            .Combine(memberEntries)
            .Select(static (input, _) => MergeMemberEntries(
                input.Right,
                DiscoverPublicMemberEntries(input.Left.Left, input.Left.Right)));

        context.RegisterSourceOutput(runtimeProvider.Combine(allEntries).Combine(allMemberEntries), static (sourceContext, input) =>
        {
            sourceContext.AddSource(
                "S1Interop.TypeRegistry.g.cs",
                SourceText.From(GenerateRegistrySource(input.Left.Left, input.Left.Right, input.Right), Encoding.UTF8));
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

    private static RuntimeBackend ResolveRuntime(AnalyzerConfigOptionsProvider optionsProvider, ParseOptions parseOptions)
    {
        if (optionsProvider.GlobalOptions.TryGetValue("build_property.S1InteropTargetRuntime", out string? propertyValue) &&
            TryParseRuntime(propertyValue, out RuntimeBackend propertyRuntime))
        {
            return propertyRuntime;
        }

        if (parseOptions is CSharpParseOptions csharpOptions)
        {
            if (csharpOptions.PreprocessorSymbolNames.Contains("IL2CPP", StringComparer.OrdinalIgnoreCase))
            {
                return RuntimeBackend.Il2Cpp;
            }

            if (csharpOptions.PreprocessorSymbolNames.Contains("MONO", StringComparer.OrdinalIgnoreCase))
            {
                return RuntimeBackend.Mono;
            }
        }

        return RuntimeBackend.Unknown;
    }

    private static bool TryParseRuntime(string? value, out RuntimeBackend runtime)
    {
        if (string.Equals(value, "Mono", StringComparison.OrdinalIgnoreCase))
        {
            runtime = RuntimeBackend.Mono;
            return true;
        }

        if (string.Equals(value, "Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "IL2CPP", StringComparison.OrdinalIgnoreCase))
        {
            runtime = RuntimeBackend.Il2Cpp;
            return true;
        }

        runtime = RuntimeBackend.Unknown;
        return false;
    }
}
