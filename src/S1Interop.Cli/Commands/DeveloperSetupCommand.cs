using System.Text.Json;
using S1Interop.Core.Setup;

internal static class DeveloperSetupCommand
{
    public static int Run(ParsedCommand command)
    {
        var service = new DeveloperSetupService();
        DeveloperSetupReport report;
        try
        {
            report = service.Inspect(
                command.Path,
                command.MonoGamePath,
                command.Il2CppGamePath,
                command.GeneratorPackageSource);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"s1interop: setup inspection failed: {ex.Message}");
            return 2;
        }

        bool isSetup = command.Name.Equals("setup", StringComparison.OrdinalIgnoreCase);
        if (isSetup && command.Apply)
        {
            try
            {
                service.Apply(report);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"s1interop: {ex.Message}");
                Print(report, command.Format, "setup blocked");
                return 2;
            }

            Print(report with { CanApply = false, LocalPropsExists = true }, command.Format, "setup applied");
            return 0;
        }

        Print(
            report,
            command.Format,
            isSetup ? "setup dry-run" : "doctor");
        return report.Ready && (!isSetup || report.CanApply || report.LocalPropsExists) ? 0 : 1;
    }

    private static void Print(DeveloperSetupReport report, OutputFormat format, string title)
    {
        if (format == OutputFormat.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { command = title, report }));
            return;
        }

        Console.WriteLine($"S1Interop {title}");
        Console.WriteLine($"Project directory: {report.ProjectDirectory}");
        Console.WriteLine($"Local configuration: {report.LocalPropsPath}");
        foreach (DeveloperSetupCheck check in report.Checks)
        {
            Console.WriteLine($"  [{check.Status}] {check.Id}: {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Remediation))
            {
                Console.WriteLine($"    next: {check.Remediation}");
            }
        }

        if (title == "setup dry-run")
        {
            Console.WriteLine(
                report.LocalPropsExists
                    ? "Existing local.build.props was validated and will not be overwritten."
                    : report.CanApply
                        ? "Run again with --apply to write only the ignored local.build.props file."
                        : "No files were changed. Resolve the checks above before using --apply.");
        }
        else if (title == "setup applied")
        {
            Console.WriteLine($"Wrote ignored local configuration: {report.LocalPropsPath}");
        }
        else
        {
            Console.WriteLine("doctor is read-only; no files or software were changed.");
        }
    }
}
