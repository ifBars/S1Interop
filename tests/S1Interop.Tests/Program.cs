var tests = new S1InteropFixtureTests();
string mode = args.FirstOrDefault() ?? "--all";
int count = mode switch
{
    "--quick" => tests.RunQuick(),
    "--portable" => tests.RunPortable(),
    "--integration" => tests.RunIntegration(requireWorkspace: true),
    "--integration-backend-neutral" => tests.RunIntegrationBackendNeutral(requireWorkspace: true),
    "--integration-build-gates" => tests.RunIntegrationBuildGates(requireWorkspace: true),
    "--integration-hoverboard" => tests.RunIntegrationHoverboard(requireWorkspace: true),
    "--all" => tests.RunAll(),
    _ => throw new ArgumentException($"Unknown test mode '{mode}'. Expected --all, --quick, --portable, --integration, --integration-backend-neutral, --integration-build-gates, or --integration-hoverboard.")
};
Console.WriteLine($"S1Interop fixture tests passed ({count} executed).");
