using S1Interop.Core.Contracts;

/// <summary>
/// Writes human-readable S1Interop analysis, migration, and verification results to the console.
/// </summary>
public static class CliReporter
{
    /// <summary>
    /// Prints a workspace analysis with project configurations, evidence, and diagnostics.
    /// </summary>
    /// <param name="analysis">The workspace analysis to print.</param>
    public static void PrintTextReport(WorkspaceAnalysis analysis)
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

    /// <summary>
    /// Prints the planned operations for each project in a migration plan.
    /// </summary>
    /// <param name="plan">The migration plan to print.</param>
    public static void PrintMigrationPlan(MigrationPlan plan)
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

    /// <summary>
    /// Prints changed files, skipped operations, and rollback information from an applied migration.
    /// </summary>
    /// <param name="result">The migration apply result to print.</param>
    public static void PrintApplyResult(MigrationApplyResult result)
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

    /// <summary>
    /// Prints before-and-after diagnostics and build results for one sandbox verification.
    /// </summary>
    /// <param name="result">The project verification result to print.</param>
    public static void PrintVerificationResult(MigrationVerificationResult result)
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

    /// <summary>
    /// Prints sandbox verification results for every project discovered in a workspace.
    /// </summary>
    /// <param name="result">The workspace verification result to print.</param>
    public static void PrintWorkspaceVerificationResult(WorkspaceMigrationVerificationResult result)
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
                    Console.WriteLine($"{indent}      fix: {FormatRemediation(issue)}");
                }

                if (issue.RestoreSources is { Count: > 0 })
                {
                    Console.WriteLine($"{indent}      restore sources: {string.Join(", ", issue.RestoreSources)}");
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

    private static string FormatBool(bool value) => value ? "yes" : "no";

    private static string FormatConfiguration(string? configuration) =>
        configuration is null ? string.Empty : $" ({configuration})";

    private static string FormatEvidence(string? evidence) =>
        string.IsNullOrWhiteSpace(evidence) ? string.Empty : $" Evidence: {evidence}";

    private static string FormatRemediation(MigrationBuildIssue issue)
    {
        string remediation = issue.Remediation ?? string.Empty;
        if (issue.RestoreSources is not { Count: > 0 })
        {
            return remediation;
        }

        int restoreSourcesIndex = remediation.IndexOf(" Current restore sources:", StringComparison.OrdinalIgnoreCase);
        return restoreSourcesIndex < 0
            ? remediation
            : remediation[..restoreSourcesIndex].TrimEnd();
    }
}
