using System.Text.Json;
using S1Interop.Core.Packaging;

internal static class ScaffoldingTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "S1Interop-scaffold-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "Compiler Mod");
        TextWriter previousOut = Console.Out, previousError = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output); Console.SetError(output);
            Require(S1InteropCli.Run(["new", project, "--format", "json"]) == 0, "Preview failed");
            Require(!Directory.Exists(root), "Preview wrote files");
            using (var preview = JsonDocument.Parse(output.ToString()))
                Require(preview.RootElement.GetProperty("mode").GetString() == "source-compiler", "Wrong default mode");
            output.GetStringBuilder().Clear();
            Require(S1InteropCli.Run(["new", project, "--apply", "--format", "json"]) == 0, "Apply failed");
            using (var applied = JsonDocument.Parse(output.ToString()))
                foreach (var file in applied.RootElement.GetProperty("files").EnumerateArray())
                    Require(File.Exists(file.GetString()), "Planned file missing");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".config/dotnet-tools.json")));
            Require(manifest.RootElement.GetProperty("tools").GetProperty("s1interop").GetProperty("version").GetString() ==
                S1InteropPackageInfo.CliPackageVersion, "Tool version is not pinned to scaffold version");
            string source = File.ReadAllText(Path.Combine(project, "Mod.cs"));
            Require(source.Contains("using ScheduleOne.NPCs;") && !source.Contains("#if"), "Starter must use ordinary game source");
            string csproj = File.ReadAllText(Path.Combine(project, "CompilerMod.csproj"));
            Require(!csproj.Contains("S1Interop.Generators") && csproj.Contains("S1InteropCompilerUseLocalTool"), "Wrong build integration");
            Require(S1InteropCli.Run(["new", project, "--apply"]) == 2, "Nonempty target was accepted");
            Require(File.ReadAllText(Path.Combine(project, "Mod.cs")) == source, "Existing source changed");
            Require(S1InteropCli.Run(["new", Path.Combine(root, "Rejected"), "--legacy-generator", "--backend-neutral", "--apply"]) == 2,
                "Conflicting scaffold modes accepted");
            Require(!Directory.Exists(Path.Combine(root, "Rejected")), "Conflicting mode wrote files");
            string existingFile = Path.Combine(root, "Existing");
            File.WriteAllText(existingFile, "keep");
            Require(S1InteropCli.Run(["new", existingFile, "--apply"]) == 2 && File.ReadAllText(existingFile) == "keep",
                "Existing file destination was not preserved");
            Require(S1InteropCli.Run(["new", Path.Combine(root, "Rejected"), "--mono-game-path", root, "--apply"]) == 2,
                "Ignored setup options accepted");
            Require(!Directory.Exists(Path.Combine(root, "Rejected")), "Invalid options wrote files");
        }
        finally
        {
            Console.SetOut(previousOut); Console.SetError(previousError);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
