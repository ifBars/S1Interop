using System.Text;

namespace S1Interop.Core.Generators;

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
        builder.AppendLine("// Backend-neutral S1Interop declarations for new mods.");
        builder.AppendLine("// Keep this file in source control and add S1InteropType/S1InteropMember attributes as your mod touches game APIs.");
        builder.AppendLine();
        builder.AppendLine("[assembly: S1Interop.S1InteropGenerateUnityEventBridge]");
        builder.AppendLine("[assembly: S1Interop.S1InteropGenerateDelegateEventBridge]");
        builder.AppendLine();
        builder.AppendLine("// Examples:");
        builder.AppendLine("// [assembly: S1Interop.S1InteropType(\"ScheduleOne.PlayerScripts.PlayerCamera\", Alias = \"PlayerCamera\")]");
        builder.AppendLine("// [assembly: S1Interop.S1InteropMember(\"PlayerCamera\", \"Instance\", Alias = \"PlayerCameraInstance\", IsStatic = true)]");
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
