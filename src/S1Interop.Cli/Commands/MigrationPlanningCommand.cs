using System.Text.Json;
using S1Interop.Core.CodeGeneration;
using S1Interop.Core.Contracts;
using S1Interop.Core.Migration;

internal static class MigrationPlanningCommand
{
    public static int Run(ParsedCommand command, WorkspaceAnalysis analysis)
    {
        MigrationPlan plan = command.Name.Equals("init", StringComparison.OrdinalIgnoreCase)
            ? CreateInitPlan(analysis)
            : command.Name.Equals("sdkgen", StringComparison.OrdinalIgnoreCase)
                ? CreateSdkGenPlan(analysis, command.FullSdk)
            : new MigrationPlanner().Plan(
                analysis,
                new MigrationPlannerOptions(
                    command.DualRuntime,
                    BuildHook: command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase)));

        plan = command.Name.ToLowerInvariant() switch
        {
            "sdkgen" => FilterPlanOperations(plan, "install_s1interop_generator_package", "generate_sdk_facade", "generate_full_sdk_facade"),
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

    private static MigrationPlan FilterPlanOperations(MigrationPlan plan, params string[] ruleIds)
    {
        var includedRuleIds = new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase);
        ProjectMigrationPlan[] projects = plan.Projects
            .Select(project => project with
            {
                Operations = project.Operations
                    .Where(operation => includedRuleIds.Contains(operation.RuleId))
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
                        "Install the S1Interop Roslyn generator package for attributes, diagnostics, and generated helpers.")
                };

                string starterPath = BackendNeutralStarterGenerator.GetSourcePath(project.ProjectPath);
                if (!File.Exists(starterPath))
                {
                    operations.Add(new MigrationOperation(
                        "generate_backend_neutral_starter",
                        starterPath,
                        null,
                        "low",
                        true,
                        "Create an editable S1Interop declarations file for optional generated helpers and facade bindings."));
                }

                return new ProjectMigrationPlan(project.ProjectPath, operations);
            })
            .ToArray();

        return new MigrationPlan(analysis.RootPath, projects);
    }

    private static MigrationPlan CreateSdkGenPlan(WorkspaceAnalysis analysis, bool fullSdk)
    {
        ProjectMigrationPlan[] projects = analysis.Projects
            .Select(project =>
            {
                SdkFacadePlan facadePlan = new SdkFacadeGenerator()
                    .Plan(project, new SdkFacadeGeneratorOptions(FullSdk: fullSdk));
                var operations = new List<MigrationOperation>();
                bool needsGeneratorPackage =
                    facadePlan.TypeAliases.Count > 0 ||
                    facadePlan.NamespaceImports.Count > 0;

                if (needsGeneratorPackage)
                {
                    operations.Add(new MigrationOperation(
                        "install_s1interop_generator_package",
                        project.ProjectPath,
                        null,
                        "low",
                        true,
                        "Install the S1Interop Roslyn generator package required by generated facade type registry attributes."));
                }

                if (facadePlan.HasContent)
                {
                    operations.Add(new MigrationOperation(
                        fullSdk ? "generate_full_sdk_facade" : "generate_sdk_facade",
                        facadePlan.OutputPath,
                        null,
                        "low",
                        true,
                        fullSdk
                            ? "Generate S1Interop backend-neutral SDK declarations for all discoverable ScheduleOne reference types."
                            : "Generate S1Interop global using facade for detected ScheduleOne namespaces so source can move away from manual Mono/IL2CPP using blocks."));
                }

                return new ProjectMigrationPlan(project.ProjectPath, operations);
            })
            .ToArray();

        return new MigrationPlan(analysis.RootPath, projects);
    }
}
