string mode = args.FirstOrDefault() ?? "--all";
if (mode == "--emit-large-redirected-output")
{
    Console.Error.Write(new string('e', 1_048_576));
    Console.Error.WriteLine("stderr-complete");
    Console.Out.WriteLine("stdout-complete");
    return;
}

var tests = new S1InteropFixtureTests();
if (mode == "--list-tests")
{
    foreach (string name in tests.ListTests())
    {
        Console.WriteLine(name);
    }

    return;
}

int count = mode switch
{
    "--filter" when args.Length == 2 && !string.IsNullOrWhiteSpace(args[1]) => tests.RunFiltered(args[1]),
    "--quick" => tests.RunQuick(),
    "--portable" => tests.RunPortable(),
    "--integration" => tests.RunIntegration(requireWorkspace: true),
    "--integration-backend-neutral" => tests.RunIntegrationBackendNeutral(requireWorkspace: true),
    "--integration-build-gates" => tests.RunIntegrationBuildGates(requireWorkspace: true),
    "--integration-hoverboard" => tests.RunIntegrationHoverboard(requireWorkspace: true),
    "--all" => tests.RunAll(),
    _ => throw new ArgumentException($"Unknown test mode or arguments '{mode}'. Expected --all, --quick, --portable, --integration, --integration-backend-neutral, --integration-build-gates, --integration-hoverboard, --list-tests, or --filter <name>.")
};
Console.WriteLine($"S1Interop fixture tests passed ({count} executed).");
