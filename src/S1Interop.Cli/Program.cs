using System.Text.Json;
using S1Interop.Core;

return S1InteropCli.Run(args);

internal static class S1InteropCli
{
    public static int Run(string[] args)
    {
        ParsedCommand command = ParsedCommand.Parse(args);
        if (command.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (!IsSupportedCommand(command.Name))
        {
            Console.Error.WriteLine($"Unknown command '{command.Name}'.");
            PrintHelp();
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

        var analyzer = new WorkspaceAnalyzer();
        WorkspaceAnalysis analysis;
        try
        {
            analysis = analyzer.Analyze(command.Path);
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
            var planner = new MigrationPlanner();
            MigrationPlan plan = planner.Plan(
                analysis,
                new MigrationPlannerOptions(
                    command.DualRuntime,
                    BuildHook: command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase)));
            if (command.Name.Equals("sdkgen", StringComparison.OrdinalIgnoreCase))
            {
                plan = FilterSdkGenerationPlan(plan);
            }
            else if (command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase))
            {
                plan = FilterBuildHookPlan(plan);
            }

            if (command.Apply)
            {
                var applier = new MigrationApplier();
                MigrationApplyResult result = applier.Apply(plan);
                if (command.Format == OutputFormat.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, S1InteropJsonContext.Default.MigrationApplyResult));
                }
                else
                {
                    PrintApplyResult(result);
                }

                return 0;
            }

            if (command.Format == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(plan, S1InteropJsonContext.Default.MigrationPlan));
            }
            else
            {
                PrintMigrationPlan(plan);
            }

            return 0;
        }

        if (command.Format == OutputFormat.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(analysis, S1InteropJsonContext.Default.WorkspaceAnalysis));
        }
        else
        {
            PrintTextReport(analysis);
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
        var verifier = new MigrationVerifier();
        try
        {
            WorkspaceMigrationVerificationResult workspaceResult = verifier.VerifyWorkspace(
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
                    PrintVerificationResult(result);
                }
            }
            else
            {
                if (command.Format == OutputFormat.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(
                        workspaceResult,
                        S1InteropJsonContext.Default.WorkspaceMigrationVerificationResult));
                }
                else
                {
                    PrintWorkspaceVerificationResult(workspaceResult);
                }
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
        var applier = new MigrationApplier();
        try
        {
            MigrationRollbackResult result = applier.Rollback(command.Path);
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

    private static void PrintTextReport(WorkspaceAnalysis analysis)
    {
        Console.WriteLine($"S1Interop analysis: {analysis.Projects.Count} project(s)");
        Console.WriteLine($"Root: {analysis.RootPath}");

        foreach (ProjectAnalysis project in analysis.Projects)
        {
            Console.WriteLine();
            Console.WriteLine(project.ProjectPath);

            foreach (ConfigurationAnalysis configuration in project.Configurations)
            {
                Console.WriteLine(
                    $"  {configuration.Name}: {configuration.Runtime} " +
                    $"tfm={configuration.TargetFramework ?? "<unknown>"} " +
                    $"scores mono={configuration.MonoScore} il2cpp={configuration.Il2CppScore} cross={configuration.CrossCompatScore} " +
                    $"refs={configuration.References.Count} packages={configuration.PackageReferences.Count}");
            }

            PrintSourceInteropReport(project.SourceInterop);

            foreach (InteropDiagnostic diagnostic in project.Diagnostics)
            {
                Console.WriteLine(
                    $"  [{diagnostic.Severity}] {diagnostic.RuleId}" +
                    $"{FormatConfiguration(diagnostic.Configuration)}: {diagnostic.Message}" +
                    $"{FormatEvidence(diagnostic.Evidence)}");
            }
        }
    }

    private static void PrintSourceInteropReport(SourceInteropAnalysis? source)
    {
        if (source is null ||
            (source.InjectedTypes.Count == 0 &&
             source.Il2CppGuardEvidence.Count == 0 &&
             source.BridgeEvidence.Count == 0 &&
             source.SourceRisks.Count == 0))
        {
            return;
        }

        Console.WriteLine(
            $"  source: injected-types={source.InjectedTypes.Count} " +
            $"il2cpp-guards={source.Il2CppGuardEvidence.Count} bridges={source.BridgeEvidence.Count} risks={source.SourceRisks.Count}");

        foreach (InjectedTypeAnalysis injectedType in source.InjectedTypes)
        {
            string hiddenMembers = injectedType.HiddenMembers.Count == 0
                ? "none"
                : string.Join(", ", injectedType.HiddenMembers);
            Console.WriteLine(
                $"    injected {injectedType.Name}: intptr={FormatBool(injectedType.HasIntPtrConstructor)} " +
                $"derived-body={FormatBool(!injectedType.HasDerivedConstructorPointer || injectedType.HasDerivedConstructorBody)} " +
                $"hidden={hiddenMembers}");
        }

        foreach (IGrouping<string, SourceRisk> group in source.SourceRisks
                     .GroupBy(risk => risk.Kind)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            SourceRisk first = group.First();
            Console.WriteLine($"    risk {group.Key}: {group.Count()} ({first.Risk})");
            Console.WriteLine($"      {first.Evidence}");
            Console.WriteLine($"      fix: {first.Remediation}");
        }
    }

    private static string FormatBool(bool value) => value ? "yes" : "no";

    private static string FormatConfiguration(string? configuration) =>
        configuration is null ? string.Empty : $" ({configuration})";

    private static string FormatEvidence(string? evidence) =>
        string.IsNullOrWhiteSpace(evidence) ? string.Empty : $" Evidence: {evidence}";

    private static void PrintMigrationPlan(MigrationPlan plan)
    {
        int operationCount = plan.Projects.Sum(project => project.Operations.Count);
        Console.WriteLine($"S1Interop migration dry-run: {operationCount} operation(s)");
        Console.WriteLine($"Root: {plan.RootPath}");

        foreach (ProjectMigrationPlan project in plan.Projects)
        {
            Console.WriteLine();
            Console.WriteLine(project.ProjectPath);
            if (project.Operations.Count == 0)
            {
                Console.WriteLine("  No migration operations planned.");
                continue;
            }

            foreach (MigrationOperation operation in project.Operations)
            {
                string mode = operation.Automatic ? "auto" : "manual";
                Console.WriteLine(
                    $"  [{mode}, {operation.Risk}] {operation.RuleId}" +
                    $"{FormatConfiguration(operation.Configuration)}: {operation.Description}");
            }
        }
    }

    private static void PrintApplyResult(MigrationApplyResult result)
    {
        Console.WriteLine($"S1Interop migration applied: {result.Operations.Count} operation(s)");
        Console.WriteLine($"Run: {result.RunId}");
        Console.WriteLine($"Manifest: {result.ManifestPath}");
        foreach (MigrationFileChange change in result.FileChanges)
        {
            string action = change.Created ? "created" : "updated";
            Console.WriteLine($"  {action} {change.FilePath}");
        }
    }

    private static void PrintVerificationResult(MigrationVerificationResult result)
    {
        string status = result.Success ? "passed" : "failed";
        Console.WriteLine($"S1Interop verify-migration {status}");
        Console.WriteLine($"Source: {result.SourceProjectPath}");
        Console.WriteLine($"Sandbox: {result.SandboxProjectPath}");
        Console.WriteLine($"Planned operations: {result.PlannedOperations}");
        Console.WriteLine($"Applied operations: {result.AppliedOperations}");
        Console.WriteLine($"Sandbox deleted: {FormatBool(result.SandboxDeleted)}");
        PrintBuildResults(result.BuildResults);

        if (result.AfterDiagnostics.Count == 0)
        {
            Console.WriteLine("Residual diagnostics: none");
            return;
        }

        Console.WriteLine($"Residual diagnostics: {result.AfterDiagnostics.Count}");
        foreach (InteropDiagnostic diagnostic in result.AfterDiagnostics)
        {
            Console.WriteLine(
                $"  [{diagnostic.Severity}] {diagnostic.RuleId}" +
                $"{FormatConfiguration(diagnostic.Configuration)}: {diagnostic.Message}" +
                $"{FormatEvidence(diagnostic.Evidence)}");
        }
    }

    private static void PrintWorkspaceVerificationResult(WorkspaceMigrationVerificationResult result)
    {
        string status = result.Success ? "passed" : "failed";
        Console.WriteLine($"S1Interop workspace verify-migration {status}");
        Console.WriteLine($"Root: {result.RootPath}");
        Console.WriteLine($"Projects: {result.ProjectCount}");
        Console.WriteLine($"Planned operations: {result.PlannedOperations}");
        Console.WriteLine($"Applied operations: {result.AppliedOperations}");

        foreach (MigrationVerificationResult project in result.Projects)
        {
            string projectStatus = project.Success && project.SandboxDeleted ? "passed" : "failed";
            Console.WriteLine();
            Console.WriteLine($"{projectStatus}: {project.SourceProjectPath}");
            Console.WriteLine($"  planned={project.PlannedOperations}, applied={project.AppliedOperations}, sandboxDeleted={FormatBool(project.SandboxDeleted)}");
            PrintBuildResults(project.BuildResults, indent: "  ");
            if (project.AfterDiagnostics.Count == 0)
            {
                Console.WriteLine("  residual diagnostics: none");
                continue;
            }

            Console.WriteLine($"  residual diagnostics: {project.AfterDiagnostics.Count}");
            foreach (IGrouping<string, InteropDiagnostic> group in project.AfterDiagnostics.GroupBy(diagnostic => diagnostic.RuleId).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {group.Key}: {group.Count()}");
            }
        }
    }

    private static void PrintBuildResults(IReadOnlyList<MigrationBuildResult>? buildResults, string indent = "")
    {
        if (buildResults is null || buildResults.Count == 0)
        {
            return;
        }

        Console.WriteLine($"{indent}Build verification:");
        foreach (MigrationBuildResult build in buildResults)
        {
            string status = build.Success ? "passed" : build.TimedOut ? "timed out" : "failed";
            Console.WriteLine($"{indent}  {status}: {build.Configuration} ({build.Runtime}) exit={build.ExitCode} readiness={build.ReadinessStatus} attribution={build.Attribution} kind={build.FailureKind}");
            if (!string.IsNullOrWhiteSpace(build.Summary))
            {
                Console.WriteLine($"{indent}    {build.Summary}");
            }

            foreach (MigrationBuildIssue issue in build.Issues.Take(10))
            {
                string include = string.IsNullOrWhiteSpace(issue.Include) ? string.Empty : $" include={issue.Include}";
                string version = string.IsNullOrWhiteSpace(issue.Version) ? string.Empty : $" version={issue.Version}";
                string path = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" path={issue.Path}";
                Console.WriteLine($"{indent}    issue: {issue.Kind}{include}{version}{path}");
                if (!string.IsNullOrWhiteSpace(issue.Remediation))
                {
                    Console.WriteLine($"{indent}      fix: {issue.Remediation}");
                }
            }

            if (build.Issues.Count > 10)
            {
                Console.WriteLine($"{indent}    issue: {build.Issues.Count - 10} more omitted");
            }

            if (!build.Success &&
                !build.Attribution.Equals("DependencyNotReady", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(build.Output))
            {
                Console.WriteLine($"{indent}    {build.Output.Replace(Environment.NewLine, $"{Environment.NewLine}{indent}    ", StringComparison.Ordinal)}");
            }
        }
    }

    private static MigrationPlan FilterSdkGenerationPlan(MigrationPlan plan)
    {
        return FilterPlanOperations(plan, "generate_sdk_facade");
    }

    private static MigrationPlan FilterBuildHookPlan(MigrationPlan plan)
    {
        return FilterPlanOperations(plan, "install_build_validation_hook");
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

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            S1Interop

            Usage:
              s1interop analyze [path=.] [--configuration name] [--format text|json]
              s1interop lint [path=.] [--configuration name] [--format text|json]
              s1interop sdkgen [path=.] [--dry-run|--apply] [--format text|json]
              s1interop build-hook [path=.] [--dry-run|--apply] [--format text|json]
              s1interop migrate [path=.] [--dry-run|--apply] [--dual-runtime] [--format text|json]
              s1interop verify-migration [path=.] [--dual-runtime] [--include-source-migrations] [--build] [--il2cpp-game-path path] [--mono-game-path path] [--build-timeout-seconds n] [--format text|json]
              s1interop migrate rollback <manifest.json> [--format text|json]

            Current slice:
              analyze/lint discover .csproj files, infer Mono/IL2CPP/CrossCompat configurations,
              report build-surface diagnostics, install build validation hooks, generate public-safe source facades, UnityEvent bridges, and source-risk reports,
              and verify sandboxed migrations for one project or a discovered workspace without reading or redistributing game assemblies.
            """);
    }

    private enum OutputFormat
    {
        Text,
        Json
    }

    private sealed record ParsedCommand(
        string Name,
        string? Subcommand,
        string Path,
        OutputFormat Format,
        bool ShowHelp,
        bool Apply,
        bool DualRuntime,
        bool Build,
        int BuildTimeoutSeconds,
        bool IncludeSourceMigrations,
        string? Il2CppGamePath,
        string? MonoGamePath,
        string? Configuration)
    {
        public static ParsedCommand Parse(string[] args)
        {
            if (args.Length == 0)
            {
                return new ParsedCommand("analyze", null, ".", OutputFormat.Text, false, false, false, false, 120, false, null, null, null);
            }

            string command = args[0].StartsWith("-", StringComparison.Ordinal) ? "analyze" : args[0];
            string? subcommand = null;
            string path = ".";
            OutputFormat format = OutputFormat.Text;
            bool showHelp = args.Any(arg =>
                arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase));
            bool apply = args.Any(arg => arg.Equals("--apply", StringComparison.OrdinalIgnoreCase));
            bool dualRuntime = args.Any(arg => arg.Equals("--dual-runtime", StringComparison.OrdinalIgnoreCase));
            bool build = args.Any(arg => arg.Equals("--build", StringComparison.OrdinalIgnoreCase));
            bool includeSourceMigrations = args.Any(arg =>
                arg.Equals("--include-source-migrations", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--source-migrations", StringComparison.OrdinalIgnoreCase));
            int buildTimeoutSeconds = 120;
            string? il2CppGamePath = null;
            string? monoGamePath = null;
            string? configuration = null;

            int startIndex = command == "analyze" && args[0].StartsWith("-", StringComparison.Ordinal) ? 0 : 1;
            if (command.Equals("migrate", StringComparison.OrdinalIgnoreCase) &&
                args.Length > 1 &&
                args[1].Equals("rollback", StringComparison.OrdinalIgnoreCase))
            {
                subcommand = "rollback";
                startIndex = 2;
            }

            for (int i = startIndex; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    format = args[++i].Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? OutputFormat.Json
                        : OutputFormat.Text;
                    continue;
                }

                if (arg.Equals("--path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    path = args[++i];
                    continue;
                }

                if (arg.Equals("--build-timeout-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int parsed) && parsed > 0)
                    {
                        buildTimeoutSeconds = parsed;
                    }

                    continue;
                }

                if (arg.Equals("--il2cpp-game-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    il2CppGamePath = args[++i];
                    continue;
                }

                if (arg.Equals("--mono-game-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    monoGamePath = args[++i];
                    continue;
                }

                if ((arg.Equals("--configuration", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-c", StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < args.Length)
                {
                    configuration = args[++i];
                    continue;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    path = arg;
                }
            }

            return new ParsedCommand(command, subcommand, path, format, showHelp, apply, dualRuntime, build, buildTimeoutSeconds, includeSourceMigrations, il2CppGamePath, monoGamePath, configuration);
        }
    }
}
