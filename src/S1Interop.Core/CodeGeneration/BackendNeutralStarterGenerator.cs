using System.Text;

namespace S1Interop.Core.CodeGeneration;

/// <summary>
/// Generates the editable declaration file added to backend-neutral mod projects.
/// </summary>
public sealed class BackendNeutralStarterGenerator
{
    /// <summary>
    /// Gets the declaration file name used under a project's <c>S1Interop.Generated</c> directory.
    /// </summary>
    public const string SourceFileName = "S1Interop.BackendNeutral.cs";

    /// <summary>
    /// Gets the declaration file path for a project.
    /// </summary>
    /// <param name="projectPath">The path to the owning <c>.csproj</c> file.</param>
    /// <returns>The path where the editable declaration file belongs.</returns>
    public static string GetSourcePath(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        return Path.Combine(projectDirectory, "S1Interop.Generated", SourceFileName);
    }

    /// <summary>
    /// Generates an empty declaration file with commented examples for common S1Interop attributes.
    /// </summary>
    /// <returns>The complete C# source for the starter declaration file.</returns>
    public string GenerateSource()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Optional S1Interop declarations for this mod.");
        builder.AppendLine("// Leave this file empty if you only want S1Interop diagnostics or built-in runtime helpers.");
        builder.AppendLine("// Add S1InteropType declarations or generated sdkgen output when you want game API facades.");
        builder.AppendLine("// Use S1InteropMember only for private members, ambiguous overloads, or migration-specific overrides.");
        builder.AppendLine();
        builder.AppendLine("// Examples:");
        builder.AppendLine("// [assembly: S1Interop.S1InteropType(\"ScheduleOne.PlayerScripts.PlayerCamera\", Alias = \"PlayerCamera\")]");
        builder.AppendLine("// [assembly: S1Interop.S1InteropGenerateUnityEventBridge]");
        builder.AppendLine("// [assembly: S1Interop.S1InteropGenerateDelegateEventBridge]");
        builder.AppendLine();
        builder.AppendLine("namespace S1Interop.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class S1InteropBackendNeutralDeclarations");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }
}
