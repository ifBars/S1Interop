using System.Diagnostics;
using S1Interop.Core.Scaffolding;

internal static class AuthoringDefineTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "S1Interop-defines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = new CompilerProjectScaffolder().CreatePlan(root);
            foreach (var asset in plan.Where(pair => pair.Key.EndsWith(".targets") || pair.Key.EndsWith(".props")))
            {
                string path = Path.Combine(root, asset.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, asset.Value);
            }
            // Replace game-dependent bodies, retaining the real ordering hooks and SDK defines.
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), """
                <Project>
                  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <DefineConstants>DEBUG;CUSTOM;CUSTOM_IL2CPP;mono;il2cpp;éMONO;MONO;IL2CPP</DefineConstants>
                  </PropertyGroup>
                  <Import Project=".s1interop/S1Interop.Compiler.targets" />
                  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
                  <Target Name="S1InteropCompilerDomain" />
                  <Target Name="S1InteropLowerSources"><PropertyGroup><ObservedDefines>$(DefineConstants)</ObservedDefines></PropertyGroup></Target>
                  <Target Name="CoreCompile"><PropertyGroup><ObservedDefines>$(DefineConstants)</ObservedDefines></PropertyGroup></Target>
                </Project>
                """);
            foreach (var (configuration, designTime, globalDefines) in new[]
                { ("Mono", false, false), ("Il2Cpp", false, false), ("Debug", false, false),
                  ("Il2Cpp", true, false), ("Il2Cpp", false, true) })
            foreach (string target in new[] { "S1InteropLowerSources", "CoreCompile" })
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = root, RedirectStandardOutput = true,
                    RedirectStandardError = true, UseShellExecute = false,
                };
                foreach (string argument in new[] { "msbuild", "Probe.csproj", "-nologo", "-t:" + target,
                    "-p:Configuration=" + configuration, "-p:DesignTimeBuild=" + designTime, "-getProperty:ObservedDefines" })
                    start.ArgumentList.Add(argument);
                if (globalDefines)
                    start.ArgumentList.Add("-p:DefineConstants=DEBUG%3BCUSTOM%3BCUSTOM_IL2CPP%3Bmono%3Bil2cpp%3BéMONO%3BIL2CPP");
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    throw new InvalidOperationException("Authoring define MSBuild probe timed out.");
                }
                string actual = output.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult() + actual);
                var symbols = actual.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (symbols.Count(value => value == "MONO") != 1 || symbols.Contains("IL2CPP") ||
                    new[] { "DEBUG", "CUSTOM", "CUSTOM_IL2CPP", "mono", "il2cpp", "éMONO", "NET8_0_OR_GREATER" }
                        .Any(value => !symbols.Contains(value)))
                    throw new InvalidOperationException($"Incorrect author symbols for {configuration}/{target}: {actual}");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
