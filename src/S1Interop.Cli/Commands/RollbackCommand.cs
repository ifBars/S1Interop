using System.Text.Json;
using S1Interop.Core;

internal static class RollbackCommand
{
    public static int Run(ParsedCommand command)
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
                PrintText(result);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"s1interop rollback failed: {ex.Message}");
            return 4;
        }
    }

    private static void PrintText(MigrationRollbackResult result)
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
}
