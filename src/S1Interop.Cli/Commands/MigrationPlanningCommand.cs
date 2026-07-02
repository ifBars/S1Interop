using System.Text.Json;
using S1Interop.Core;

internal static class MigrationPlanningCommand
{
    public static int Run(ParsedCommand command, WorkspaceAnalysis analysis)
    {
        MigrationPlan plan = command.Name.Equals("init", StringComparison.OrdinalIgnoreCase)
            ? CreateInitPlan(analysis)
            : new MigrationPlanner().Plan(
                analysis,
                new MigrationPlannerOptions(
                    command.DualRuntime,
                    BuildHook: command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase)));

        plan = command.Name.ToLowerInvariant() switch
        {
            "sdkgen" => FilterPlanOperations(plan, "generate_sdk_facade"),
            "build-hook" => FilterPlanOperations(plan, "install_build_validation_hook"),
            _ => plan
        };

        if (command.Apply)
        {
            MigrationApplyResult result = new MigrationApplier().Apply(plan);
            if (command.Format == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, S1InteropJsonContext.Default.MigrationApplyResult));
            }
            else
            {
                CliReporter.PrintApplyResult(result);
            }

            return 0;
        }

        if (command.Format == OutputFormat.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, S1InteropJsonContext.Default.MigrationPlan));
        }
        else
        {
            CliReporter.PrintMigrationPlan(plan);
        }

        return 0;
    }

    private static MigrationPlan FilterPlanOperations(MigrationPlan plan, string ruleId)
    {
        ProjectMigrationPlan[] projects = plan.Projects
            .Select(project => project with
            {
                Operations = project.Operations
                    .Where(operation => operation.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            })
            .ToArray();

        return plan with { Projects = projects };
    }

    private static MigrationPlan CreateInitPlan(WorkspaceAnalysis analysis)
    {
        ProjectMigrationPlan[] projects = analysis.Projects
            .Select(project =>
            {
                var operations = new List<MigrationOperation>
                {
                    new(
                        "install_s1interop_generator_package",
                        project.ProjectPath,
                        null,
                        "low",
                        true,
                        "Install the S1Interop Roslyn generator package for backend-neutral attributes and generated helpers.")
                };

                string starterPath = S1Interop.Core.Generators.BackendNeutralStarterGenerator.GetSourcePath(project.ProjectPath);
                if (!File.Exists(starterPath))
                {
                    operations.Add(new MigrationOperation(
                        "generate_backend_neutral_starter",
                        starterPath,
                        null,
                        "low",
                        true,
                        "Create an editable S1Interop backend-neutral declarations file with starter generator attributes."));
                }

                return new ProjectMigrationPlan(project.ProjectPath, operations);
            })
            .ToArray();

        return new MigrationPlan(analysis.RootPath, projects);
    }
}
