namespace S1Interop.Core.Packaging;

public static class S1InteropPackageInfo
{
    public const string AlphaPackageVersion = "0.1.0-alpha.1";
    public const string GeneratorsPackageId = "S1Interop.Generators";
    public const string CliPackageId = "S1Interop";
    public const string CliPackageVersion = AlphaPackageVersion;
    public const string GeneratorsPackageVersion = AlphaPackageVersion;
    public const string PrivateAssets = "all";
    public const string AnalyzerIncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive";
    public const string GeneratorsPackageSourceProperty = "S1InteropGeneratorPackageSource";
    public const string RestoreAdditionalProjectSourcesProperty = "RestoreAdditionalProjectSources";

    public static string CreateLocalGeneratorsPackageVersion(DateTimeOffset timestamp) =>
        $"{GeneratorsPackageVersion}.local.{timestamp:yyyyMMddHHmmssfff}";
}
