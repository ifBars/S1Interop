using System.Text;

namespace S1Interop.Core.CodeGeneration;

public sealed class BackendNeutralStarterGenerator
{
    public const string SourceFileName = "S1Interop.BackendNeutral.cs";

    public static string GetSourcePath(string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        return Path.Combine(projectDirectory, "S1Interop.Generated", SourceFileName);
    }

    public string GenerateSource()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Backend-neutral S1Interop declarations for this mod.");
        builder.AppendLine("// Prefer S1InteropType declarations or generated sdkgen output for game API coverage.");
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
