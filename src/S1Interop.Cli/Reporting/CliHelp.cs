internal static class CliHelp
{
    public static void Print()
    {
        Console.WriteLine(
            """
            S1Interop

            Usage:
              s1interop analyze [path=.] [--configuration name] [--format text|json]
              s1interop lint [path=.] [--configuration name] [--format text|json]
              s1interop sdkgen [path=.] [--dry-run|--apply] [--format text|json]
              s1interop build-hook [path=.] [--dry-run|--apply] [--format text|json]
              s1interop migrate [path=.] [--dry-run|--apply] [--dual-runtime] [--format text|json]
              s1interop verify-migration [path=.] [--dual-runtime] [--include-source-migrations] [--build] [--il2cpp-game-path path] [--mono-game-path path] [--build-timeout-seconds n] [--format text|json]
              s1interop migrate rollback <manifest.json> [--format text|json]

            What it does:
              analyze/lint inspect .csproj files and infer Mono, IL2CPP, and CrossCompat configurations.
              migrate can scaffold dual-runtime build settings, generated source helpers, and source-risk reports.
              verify-migration runs the migration in a temporary sandbox before changing a real project.
            """);
    }
}
