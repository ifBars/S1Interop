using System.Text.Json;
using S1Interop.Core.Scaffolding;

internal static class NewProjectCommand
{
    public static int Run(ParsedCommand command)
    {
        var scaffolder = new BackendNeutralProjectScaffolder();
        NewProjectPlan plan;
        try
        {
            plan = scaffolder.CreatePlan(command.Path);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"s1interop: {ex.Message}");
            return 2;
        }

        string targetDirectory = plan.TargetDirectory;
        if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
        {
            Console.Error.WriteLine($"s1interop: target directory is not empty: {targetDirectory}");
            return 2;
        }

        if (!command.Apply)
        {
            PrintDryRun(command, plan);
            return 0;
        }

        scaffolder.Apply(plan, command.BackendNeutral);

        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                mode = command.BackendNeutral ? "experimental-backend-neutral" : "dual-runtime",
                projectName = plan.ProjectName,
                targetDirectory = plan.TargetDirectory,
                files = plan.PlannedFiles,
                next = GetNextSteps(plan, command.BackendNeutral)
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }

        Console.WriteLine($"S1Interop project created: {plan.ProjectName}");
        Console.WriteLine($"Mode: {(command.BackendNeutral ? "experimental backend-neutral" : "dual-runtime (recommended)")}");
        Console.WriteLine($"Directory: {plan.TargetDirectory}");
        foreach (string file in plan.PlannedFiles)
        {
            Console.WriteLine($"  created {file}");
        }
        PrintNextSteps(plan, command.BackendNeutral);

        return 0;
    }

    private static void PrintDryRun(ParsedCommand command, NewProjectPlan plan)
    {
        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                mode = command.BackendNeutral ? "experimental-backend-neutral" : "dual-runtime",
                projectName = plan.ProjectName,
                targetDirectory = plan.TargetDirectory,
                apply = false,
                files = plan.PlannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return;
        }

        Console.WriteLine($"S1Interop new project dry-run: {plan.ProjectName}");
        Console.WriteLine($"Mode: {(command.BackendNeutral ? "experimental backend-neutral" : "dual-runtime (recommended)")}");
        Console.WriteLine($"Directory: {plan.TargetDirectory}");
        foreach (string file in plan.PlannedFiles)
        {
            Console.WriteLine($"  create {file}");
        }
        Console.WriteLine("Run again with --apply to write files.");
    }

    private static void PrintNextSteps(NewProjectPlan plan, bool backendNeutral)
    {
        Console.WriteLine();
        if (backendNeutral)
        {
            Console.WriteLine("Experimental mode: backend-neutral facades are fragile; keep a dual-runtime fallback and validate both game branches.");
        }

        Console.WriteLine("Next:");
        foreach (string step in GetNextSteps(plan, backendNeutral))
        {
            Console.WriteLine($"  {step}");
        }
    }

    private static string[] GetNextSteps(NewProjectPlan plan, bool backendNeutral) =>
        backendNeutral
            ?
            [
                $"Set-Location \"{plan.TargetDirectory}\"",
                "s1interop doctor .",
                "s1interop setup . --apply",
                $"dotnet build .\\{plan.ProjectName}.sln -c Debug",
                $"DLL: bin\\Single\\Debug\\netstandard2.1\\{plan.ProjectName}.dll",
                $"Deploy: Copy-Item \".\\bin\\Single\\Debug\\netstandard2.1\\{plan.ProjectName}.dll\" \"<MonoGamePath>\\Mods\\{plan.ProjectName}.dll\" -Force",
                "Run: & \"<MonoGamePath>\\Schedule I.exe\"",
                $"Expected log: {plan.ProjectName} loaded on Mono. (or Il2Cpp)"
            ]
            :
            [
                $"Set-Location \"{plan.TargetDirectory}\"",
                "s1interop doctor .",
                "s1interop setup . --apply",
                $"dotnet build .\\{plan.ProjectName}.sln -c \"Debug Mono\"",
                $"dotnet build .\\{plan.ProjectName}.sln -c \"Debug Il2Cpp\"",
                $"Mono DLL: bin\\Mono\\Debug Mono\\netstandard2.1\\{plan.ProjectName}.dll",
                $"IL2CPP DLL: bin\\Il2Cpp\\Debug Il2Cpp\\net6.0\\{plan.ProjectName}.dll",
                $"Mono deploy: Copy-Item \".\\bin\\Mono\\Debug Mono\\netstandard2.1\\{plan.ProjectName}.dll\" \"<MonoGamePath>\\Mods\\{plan.ProjectName}.dll\" -Force",
                "Mono run: & \"<MonoGamePath>\\Schedule I.exe\"",
                $"IL2CPP deploy: Copy-Item \".\\bin\\Il2Cpp\\Debug Il2Cpp\\net6.0\\{plan.ProjectName}.dll\" \"<Il2CppGamePath>\\Mods\\{plan.ProjectName}.dll\" -Force",
                "IL2CPP run: & \"<Il2CppGamePath>\\Schedule I.exe\"",
                $"Expected log: {plan.ProjectName} loaded on Mono. (or Il2Cpp)"
            ];
}
