using System.Text.Json;
using S1Interop.Core;

internal static class S1InteropCli
{
    public static int Run(string[] args)
    {
        ParsedCommand command = ParsedCommand.Parse(args);
        if (command.ShowHelp)
        {
            CliHelp.Print();
            return 0;
        }

        if (!IsSupportedCommand(command.Name))
        {
            Console.Error.WriteLine($"Unknown command '{command.Name}'.");
            CliHelp.Print();
            return 2;
        }

        if (command.Name.Equals("migrate", StringComparison.OrdinalIgnoreCase) &&
            command.Subcommand?.Equals("rollback", StringComparison.OrdinalIgnoreCase) == true)
        {
            return RunRollback(command);
        }

        if (command.Name.Equals("verify-migration", StringComparison.OrdinalIgnoreCase))
        {
            return RunVerifyMigration(command);
        }

        WorkspaceAnalysis analysis;
        try
        {
            analysis = new WorkspaceAnalyzer().Analyze(command.Path);
            if (!string.IsNullOrWhiteSpace(command.Configuration) &&
                (command.Name.Equals("analyze", StringComparison.OrdinalIgnoreCase) ||
                 command.Name.Equals("lint", StringComparison.OrdinalIgnoreCase)))
            {
                analysis = FilterAnalysisByConfiguration(analysis, command.Configuration);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"s1interop: analysis failed: {ex.Message}");
            return 3;
        }

        if (command.Name.Equals("migrate", StringComparison.OrdinalIgnoreCase) ||
            command.Name.Equals("sdkgen", StringComparison.OrdinalIgnoreCase) ||
            command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase))
        {
            return RunMigrationPlanningCommand(command, analysis);
        }

        if (command.Format == OutputFormat.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(analysis, S1InteropJsonContext.Default.WorkspaceAnalysis));
        }
        else
        {
            CliReporter.PrintTextReport(analysis);
        }

        return command.Name.Equals("lint", StringComparison.OrdinalIgnoreCase) &&
               analysis.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static bool IsSupportedCommand(string command) =>
        command.Equals("analyze", StringComparison.OrdinalIgnoreCase) ||
        command.Equals("lint", StringComparison.OrdinalIgnoreCase) ||
        command.Equals("migrate", StringComparison.OrdinalIgnoreCase) ||
        command.Equals("verify-migration", StringComparison.OrdinalIgnoreCase) ||
        command.Equals("build-hook", StringComparison.OrdinalIgnoreCase) ||
        command.Equals("sdkgen", StringComparison.OrdinalIgnoreCase);

    private static int RunMigrationPlanningCommand(ParsedCommand command, WorkspaceAnalysis analysis)
    {
        MigrationPlan plan = new MigrationPlanner().Plan(
            analysis,
            new MigrationPlannerOptions(
                command.DualRuntime,
                BuildHook: command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase)));
        if (command.Name.Equals("sdkgen", StringComparison.OrdinalIgnoreCase))
        {
            plan = FilterPlanOperations(plan, "generate_sdk_facade");
        }
        else if (command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase))
        {
            plan = FilterPlanOperations(plan, "install_build_validation_hook");
        }

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

    private static WorkspaceAnalysis FilterAnalysisByConfiguration(WorkspaceAnalysis analysis, string configuration)
    {
        ProjectAnalysis[] projects = analysis.Projects
            .Select(project =>
            {
                ConfigurationAnalysis[] configurations = project.Configurations
                    .Where(entry => string.Equals(entry.Name, configuration, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                InteropDiagnostic[] diagnostics = project.Diagnostics
                    .Where(diagnostic =>
                        diagnostic.Configuration is null ||
                        string.Equals(diagnostic.Configuration, configuration, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return project with
                {
                    Configurations = configurations,
                    Diagnostics = diagnostics
                };
            })
            .ToArray();

        return analysis with { Projects = projects };
    }

    private static int RunVerifyMigration(ParsedCommand command)
    {
        try
        {
            WorkspaceMigrationVerificationResult workspaceResult = new MigrationVerifier().VerifyWorkspace(
                command.Path,
                new MigrationVerifierOptions(
                    command.DualRuntime,
                    Build: command.Build,
                    BuildTimeoutSeconds: command.BuildTimeoutSeconds,
                    IncludeSourceMigrations: command.IncludeSourceMigrations,
                    Il2CppGamePath: command.Il2CppGamePath,
                    MonoGamePath: command.MonoGamePath));

            if (workspaceResult.ProjectCount == 1)
            {
                MigrationVerificationResult result = workspaceResult.Projects[0];
                if (command.Format == OutputFormat.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(
                        result,
                        S1InteropJsonContext.Default.MigrationVerificationResult));
                }
                else
                {
                    CliReporter.PrintVerificationResult(result);
                }
            }
            else if (command.Format == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    workspaceResult,
                    S1InteropJsonContext.Default.WorkspaceMigrationVerificationResult));
            }
            else
            {
                CliReporter.PrintWorkspaceVerificationResult(workspaceResult);
            }

            return workspaceResult.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"s1interop verify-migration failed: {ex.Message}");
            return 5;
        }
    }

    private static int RunRollback(ParsedCommand command)
    {
        try
        {
            MigrationRollbackResult result = new MigrationApplier().Rollback(command.Path);
            if (command.Format == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, S1InteropJsonContext.Default.MigrationRollbackResult));
            }
            else
            {
                Console.WriteLine($"S1Interop rollback {result.RunId}");
                foreach (string file in result.RestoredFiles)
                {
                    Console.WriteLine($"  restored {file}");
                }

                foreach (string file in result.RemovedFiles)
                {
                    Console.WriteLine($"  removed {file}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"s1interop rollback failed: {ex.Message}");
            return 4;
        }
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
}
