using System.Text.Json;
using S1Interop.Core.Scaffolding;

internal static class CompilerProjectCommand
{
    public static int Run(ParsedCommand command)
    {
        try
        {
            string root = Path.GetFullPath(command.Path);
            var scaffolder = new CompilerProjectScaffolder();
            var plan = scaffolder.CreatePlan(root);
            if (File.Exists(root) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new ArgumentException("Target directory must be empty: " + root);
            if (command.Apply) scaffolder.Apply(root);
            string[] files = plan.Keys.Select(path => Path.GetFullPath(Path.Combine(root, path))).ToArray();
            string[] next = ["Open the created directory in your terminal.", "dotnet tool restore (add --add-source <candidate-feed> for an unpublished version)",
                "dotnet tool run s1interop -- setup . --mono-game-path <mono-install> --il2cpp-game-path <il2cpp-install> --apply",
                "dotnet tool run s1interop -- doctor .",
                "dotnet build -c Release -p:S1InteropCompilerRuntime=Mono", "dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp"];
            if (command.Format == OutputFormat.Json)
                Console.WriteLine(JsonSerializer.Serialize(new { mode = "source-compiler", targetDirectory = root, apply = command.Apply, files, next }));
            else
            {
                Console.WriteLine(command.Apply ? "S1Interop compiler project created" : "S1Interop new project dry-run (source compiler)");
                foreach (string file in files) Console.WriteLine("  " + file);
                if (!command.Apply) Console.WriteLine("Run again with --apply to write files.");
                else foreach (string step in next) Console.WriteLine("  " + step);
            }
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("s1interop: " + exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("s1interop: Could not write the scaffold: " + exception.Message +
                " Inspect the destination for partially created files before retrying.");
            return 2;
        }
    }
}
