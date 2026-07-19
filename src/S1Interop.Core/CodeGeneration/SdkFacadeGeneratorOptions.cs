namespace S1Interop.Core.CodeGeneration;

/// <summary>
/// Configures how SDK facade declarations are planned from a project analysis.
/// </summary>
/// <param name="FullSdk">True to seed broad namespace registration from local reference metadata; false to infer declarations from project usage.</param>
public sealed record SdkFacadeGeneratorOptions(bool FullSdk = false)
{
    /// <summary>
    /// Gets the usage-driven SDK generation options.
    /// </summary>
    public static SdkFacadeGeneratorOptions Default { get; } = new();
}
