internal static class SupportedCommands
{
    private static readonly string[] Names =
    [
        "analyze",
        "init",
        "lint",
        "migrate",
        "new",
        "verify-migration",
        "build-hook",
        "sdkgen"
    ];

    public static bool Contains(string command) =>
        Names.Any(name => name.Equals(command, StringComparison.OrdinalIgnoreCase));
}
