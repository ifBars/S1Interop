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

        scaffolder.Apply(plan);

        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                projectName = plan.ProjectName,
                targetDirectory = plan.TargetDirectory,
                files = plan.PlannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }

        Console.WriteLine($"S1Interop backend-neutral project created: {plan.ProjectName}");
        Console.WriteLine($"Directory: {plan.TargetDirectory}");
        foreach (string file in plan.PlannedFiles)
        {
            Console.WriteLine($"  created {file}");
        }

        return 0;
    }

    private static void PrintDryRun(ParsedCommand command, NewProjectPlan plan)
    {
        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                projectName = plan.ProjectName,
                targetDirectory = plan.TargetDirectory,
                apply = false,
                files = plan.PlannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return;
        }

        Console.WriteLine($"S1Interop new project dry-run: {plan.ProjectName}");
        Console.WriteLine($"Directory: {plan.TargetDirectory}");
        foreach (string file in plan.PlannedFiles)
        {
            Console.WriteLine($"  create {file}");
        }
        Console.WriteLine("Run again with --apply to write files.");
    }
}
