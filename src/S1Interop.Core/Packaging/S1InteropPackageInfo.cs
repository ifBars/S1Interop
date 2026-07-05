namespace S1Interop.Core.Packaging;

/// <summary>
/// Provides package IDs, versions, and MSBuild property names shared by S1Interop tooling.
/// </summary>
public static class S1InteropPackageInfo
{
    /// <summary>
    /// Gets the current alpha version used by the CLI and generator packages.
    /// </summary>
    public const string AlphaPackageVersion = "0.1.0-alpha.1";

    /// <summary>
    /// Gets the NuGet package ID for the Roslyn generator package.
    /// </summary>
    public const string GeneratorsPackageId = "S1Interop.Generators";

    /// <summary>
    /// Gets the NuGet package ID for the S1Interop CLI tool package.
    /// </summary>
    public const string CliPackageId = "S1Interop";

    /// <summary>
    /// Gets the CLI tool package version.
    /// </summary>
    public const string CliPackageVersion = AlphaPackageVersion;

    /// <summary>
    /// Gets the Roslyn generator package version.
    /// </summary>
    public const string GeneratorsPackageVersion = AlphaPackageVersion;

    /// <summary>
    /// Gets the PrivateAssets value used when adding the generator package to mod projects.
    /// </summary>
    public const string PrivateAssets = "all";

    /// <summary>
    /// Gets the IncludeAssets value used to consume the generator package as an analyzer dependency.
    /// </summary>
    public const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";

    /// <summary>
    /// Gets the MSBuild property name that can point to a local generator package feed.
    /// </summary>
    public const string GeneratorsPackageSourceProperty = "S1InteropGeneratorPackageSource";

    /// <summary>
    /// Gets the MSBuild property name used to append local restore sources for generated projects.
    /// </summary>
    public const string RestoreAdditionalProjectSourcesProperty = "RestoreAdditionalProjectSources";

    /// <summary>
    /// Creates a unique local generator package version for sandbox or unpublished-package validation.
    /// </summary>
    /// <param name="timestamp">The timestamp to embed in the local version suffix.</param>
    /// <returns>A package version based on <see cref="GeneratorsPackageVersion"/> with a local timestamp suffix.</returns>
    public static string CreateLocalGeneratorsPackageVersion(DateTimeOffset timestamp) =>
        $"{GeneratorsPackageVersion}.local.{timestamp:yyyyMMddHHmmssfff}";
}
