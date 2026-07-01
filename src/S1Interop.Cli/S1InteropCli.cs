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

        if (!SupportedCommands.Contains(command.Name))
        {
            Console.Error.WriteLine($"Unknown command '{command.Name}'.");
            CliHelp.Print();
            return 2;
        }

        return CliCommandDispatcher.Run(command);
    }
}
