internal sealed record ParsedCommand(
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
    bool FullSdk,
    string? Il2CppGamePath,
    string? MonoGamePath,
    string? Configuration,
    IReadOnlyList<string> Errors)
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedCommand("analyze", null, ".", OutputFormat.Text, false, false, false, false, 120, false, false, null, null, null, Array.Empty<string>());
        }

        string command = args[0].StartsWith("-", StringComparison.Ordinal) ? "analyze" : args[0];
        string? subcommand = null;
        string path = ".";
        var errors = new List<string>();
        OutputFormat format = OutputFormat.Text;
        bool showHelp = args.Any(arg =>
            arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-h", StringComparison.OrdinalIgnoreCase));
        bool apply = args.Any(arg => arg.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        bool dualRuntime = args.Any(arg => arg.Equals("--dual-runtime", StringComparison.OrdinalIgnoreCase));
        bool build = args.Any(arg => arg.Equals("--build", StringComparison.OrdinalIgnoreCase));
        bool fullSdk = args.Any(arg => arg.Equals("--full-sdk", StringComparison.OrdinalIgnoreCase));
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
                string value = args[++i];
                if (value.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    format = OutputFormat.Json;
                }
                else if (value.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    format = OutputFormat.Text;
                }
                else
                {
                    errors.Add("Invalid value for --format. Expected 'text' or 'json'.");
                }

                continue;
            }

            if (arg.Equals("--path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                path = args[++i];
                continue;
            }

            if (arg.Equals("--build-timeout-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string value = args[++i];
                if (int.TryParse(value, out int parsed) && parsed > 0)
                {
                    buildTimeoutSeconds = parsed;
                }
                else
                {
                    errors.Add("Invalid value for --build-timeout-seconds. Expected a positive integer.");
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

            if (IsKnownValueOption(arg))
            {
                errors.Add($"Missing value for {arg}.");
                continue;
            }

            if (IsKnownFlag(arg))
            {
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                errors.Add($"Unknown option '{arg}'.");
                continue;
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                path = arg;
            }
        }

        return new ParsedCommand(command, subcommand, path, format, showHelp, apply, dualRuntime, build, buildTimeoutSeconds, includeSourceMigrations, fullSdk, il2CppGamePath, monoGamePath, configuration, errors);
    }

    private static bool IsKnownFlag(string arg) =>
        arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--apply", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--dual-runtime", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--build", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--full-sdk", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--include-source-migrations", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--source-migrations", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownValueOption(string arg) =>
        arg.Equals("--format", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--path", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--build-timeout-seconds", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--il2cpp-game-path", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--mono-game-path", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--configuration", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-c", StringComparison.OrdinalIgnoreCase);
}
