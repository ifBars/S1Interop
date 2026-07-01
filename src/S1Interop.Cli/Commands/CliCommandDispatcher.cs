using S1Interop.Core;

internal static class CliCommandDispatcher
{
    public static int Run(ParsedCommand command)
    {
        if (command.Name.Equals("migrate", StringComparison.OrdinalIgnoreCase) &&
            command.Subcommand?.Equals("rollback", StringComparison.OrdinalIgnoreCase) == true)
        {
            return RollbackCommand.Run(command);
        }

        if (command.Name.Equals("verify-migration", StringComparison.OrdinalIgnoreCase))
        {
            return VerifyMigrationCommand.Run(command);
        }

        WorkspaceAnalysis analysis;
        try
        {
            analysis = new WorkspaceAnalyzer().Analyze(command.Path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"s1interop: analysis failed: {ex.Message}");
            return 3;
        }

        return AnalysisBackedCommand.Run(command, analysis);
    }
}
