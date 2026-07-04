using System.Reflection;

internal static class S1InteropCli
{
    public static int Run(string[] args)
    {
        if (IsVersionRequest(args))
        {
            Console.WriteLine(GetVersionText());
            return 0;
        }

        ParsedCommand command = ParsedCommand.Parse(args);
        if (command.Errors.Count > 0)
        {
            foreach (string error in command.Errors)
            {
                Console.Error.WriteLine($"s1interop: {error}");
            }

            CliHelp.Print();
            return 2;
        }

        if (command.ShowHelp)
        {
            CliHelp.Print();
            return 0;
        }

        if (!SupportedCommands.Contains(command.Name))
        {
            Console.Error.WriteLine($"Unknown command '{command.Name}'.");
            CliHelp.Print();
            return 2;
        }

        return CliCommandDispatcher.Run(command);
    }

    private static bool IsVersionRequest(string[] args) =>
        args.Length == 1 &&
        (args[0].Equals("version", StringComparison.OrdinalIgnoreCase) ||
         args[0].Equals("--version", StringComparison.OrdinalIgnoreCase));

    private static string GetVersionText()
    {
        Assembly assembly = typeof(S1InteropCli).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString()
                         ?? "unknown";
        return $"S1Interop {version}";
    }
}
