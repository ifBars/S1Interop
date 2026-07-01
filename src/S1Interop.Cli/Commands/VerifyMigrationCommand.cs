using System.Text.Json;
using S1Interop.Core;

internal static class VerifyMigrationCommand
{
    public static int Run(ParsedCommand command)
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

            PrintResult(command, workspaceResult);
            return workspaceResult.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"s1interop verify-migration failed: {ex.Message}");
            return 5;
        }
    }

    private static void PrintResult(ParsedCommand command, WorkspaceMigrationVerificationResult workspaceResult)
    {
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

            return;
        }

        if (command.Format == OutputFormat.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                workspaceResult,
                S1InteropJsonContext.Default.WorkspaceMigrationVerificationResult));
        }
        else
        {
            CliReporter.PrintWorkspaceVerificationResult(workspaceResult);
        }
    }
}
