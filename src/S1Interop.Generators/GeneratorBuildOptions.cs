using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace S1Interop.Generators;

/// <summary>
/// MSBuild-controlled generator switches, made visible to the compiler by the package's build props.
/// </summary>
internal readonly struct GeneratorBuildOptions : IEquatable<GeneratorBuildOptions>
{
    /// <summary>
    /// The oldest C# version the generated helpers, registries, and facades compile under.
    /// </summary>
    public const LanguageVersion MinimumGeneratedCodeLanguageVersion = LanguageVersion.CSharp9;

    public GeneratorBuildOptions(
        RuntimeBackend runtime,
        bool emitRuntimeHelpers,
        bool emitPlatformDomain,
        LanguageVersion languageVersion)
    {
        Runtime = runtime;
        EmitRuntimeHelpers = emitRuntimeHelpers;
        EmitPlatformDomain = emitPlatformDomain;
        LanguageVersion = languageVersion;
    }

    public RuntimeBackend Runtime { get; }

    public LanguageVersion LanguageVersion { get; }

    /// <summary>
    /// Gets whether generated source can be added. Older projects still get compiler diagnostics, plus one
    /// warning that explains how to enable generated code, instead of errors inside generated files.
    /// </summary>
    public bool CanEmitGeneratedCode => LanguageVersion >= MinimumGeneratedCodeLanguageVersion;

    /// <summary>
    /// Gets whether runtime-specific helper extensions are emitted. On by default because they are internal and only
    /// visible to files that import the <c>S1Interop</c> namespace.
    /// </summary>
    public bool EmitRuntimeHelpers { get; }

    /// <summary>
    /// Gets whether a <c>MelonPlatformDomain</c> attribute is emitted. Off unless the build targets set it, because a
    /// single DLL that loads on both branches must not be pinned to one domain.
    /// </summary>
    public bool EmitPlatformDomain { get; }

    public static GeneratorBuildOptions Resolve(AnalyzerConfigOptionsProvider optionsProvider, ParseOptions parseOptions)
    {
        AnalyzerConfigOptions options = optionsProvider.GlobalOptions;
        return new GeneratorBuildOptions(
            RuntimeBackendResolver.Resolve(optionsProvider, parseOptions),
            !IsFalse(options, "build_property.S1InteropEmitRuntimeHelpers"),
            IsTrue(options, "build_property.S1InteropEmitPlatformDomain"),
            parseOptions is CSharpParseOptions csharpOptions ? csharpOptions.LanguageVersion : LanguageVersion.Latest);
    }

    public bool Equals(GeneratorBuildOptions other) =>
        Runtime == other.Runtime &&
        EmitRuntimeHelpers == other.EmitRuntimeHelpers &&
        EmitPlatformDomain == other.EmitPlatformDomain &&
        LanguageVersion == other.LanguageVersion;

    public override bool Equals(object? obj) => obj is GeneratorBuildOptions other && Equals(other);

    public override int GetHashCode() =>
        ((int)LanguageVersion * 16) + ((int)Runtime * 4) + (EmitRuntimeHelpers ? 2 : 0) + (EmitPlatformDomain ? 1 : 0);

    private static bool IsTrue(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
}
