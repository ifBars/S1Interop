internal static class SupportedCommands
{
    private static readonly string[] Names =
    [
        "analyze",
        "lint",
        "migrate",
        "verify-migration",
        "build-hook",
        "sdkgen"
    ];

    public static bool Contains(string command) =>
        Names.Any(name => name.Equals(command, StringComparison.OrdinalIgnoreCase));
}
