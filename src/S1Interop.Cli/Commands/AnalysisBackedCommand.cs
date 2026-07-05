using System.Text.Json;
using S1Interop.Core.Contracts;

internal static class AnalysisBackedCommand
{
    public static int Run(ParsedCommand command, WorkspaceAnalysis analysis)
    {
        if (IsReportCommand(command) && !string.IsNullOrWhiteSpace(command.Configuration))
        {
            analysis = FilterAnalysisByConfiguration(analysis, command.Configuration);
        }

        if (IsMigrationPlanningCommand(command))
        {
            return MigrationPlanningCommand.Run(command, analysis);
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

    private static bool IsReportCommand(ParsedCommand command) =>
        command.Name.Equals("analyze", StringComparison.OrdinalIgnoreCase) ||
        command.Name.Equals("lint", StringComparison.OrdinalIgnoreCase);

    private static bool IsMigrationPlanningCommand(ParsedCommand command) =>
        command.Name.Equals("migrate", StringComparison.OrdinalIgnoreCase) ||
        command.Name.Equals("init", StringComparison.OrdinalIgnoreCase) ||
        command.Name.Equals("sdkgen", StringComparison.OrdinalIgnoreCase) ||
        command.Name.Equals("build-hook", StringComparison.OrdinalIgnoreCase);

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
}
