namespace S1Interop.Core.CodeGeneration;

public sealed record SdkFacadeGeneratorOptions(bool FullSdk = false)
{
    public static SdkFacadeGeneratorOptions Default { get; } = new();
}
