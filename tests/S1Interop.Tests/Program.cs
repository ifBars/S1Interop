var tests = new S1InteropFixtureTests();
string mode = args.FirstOrDefault() ?? "--all";
int count = mode switch
{
    "--portable" => tests.RunPortable(),
    "--integration" => tests.RunIntegration(requireWorkspace: true),
    "--all" => tests.RunAll(),
    _ => throw new ArgumentException($"Unknown test mode '{mode}'. Expected --all, --portable, or --integration.")
};
Console.WriteLine($"S1Interop fixture tests passed ({count} executed).");
