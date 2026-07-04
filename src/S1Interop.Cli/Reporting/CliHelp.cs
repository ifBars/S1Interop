internal static class CliHelp
{
    public static void Print()
    {
        Console.WriteLine(
            """
            S1Interop

            Usage:
              s1interop analyze [path=.] [--configuration name] [--format text|json]
              s1interop new <path> [--dry-run|--apply] [--format text|json]
              s1interop init [path=.] [--dry-run|--apply] [--format text|json]
              s1interop lint [path=.] [--configuration name] [--format text|json]
              s1interop sdkgen [path=.] [--full-sdk] [--dry-run|--apply] [--format text|json]
              s1interop build-hook [path=.] [--dry-run|--apply] [--format text|json]
              s1interop migrate [path=.] [--dry-run|--apply] [--dual-runtime] [--format text|json]
              s1interop verify-migration [path=.] [--dual-runtime] [--include-source-migrations] [--build] [--il2cpp-game-path path] [--mono-game-path path] [--build-timeout-seconds n] [--format text|json]
              s1interop migrate rollback <manifest.json> [--format text|json]
              s1interop --version

            What it does:
              analyze/lint inspect .csproj files and infer Mono, IL2CPP, and CrossCompat configurations.
              new creates a backend-neutral project scaffold with a solution, local path example, and generator attributes ready to edit.
              init opts a project into backend-neutral attributes and generated helpers from the start.
              sdkgen emits usage-driven facades by default; --full-sdk emits declarations for all discoverable ScheduleOne reference types.
              migrate can scaffold dual-runtime build settings, generated source helpers, and source-risk reports.
              verify-migration runs the migration in a temporary sandbox before changing a real project.
            """);
    }
}
