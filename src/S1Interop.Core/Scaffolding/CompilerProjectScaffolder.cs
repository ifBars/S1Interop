using System.Text;
using System.Text.Json;

namespace S1Interop.Core.Scaffolding;

/// <summary>Creates a compiler project with pinned local tooling and matching build integration.</summary>
public sealed class CompilerProjectScaffolder
{
    /// <summary>Plans generated files without creating the destination.</summary>
    public IReadOnlyDictionary<string, string> CreatePlan(string directory)
    {
        var identity = new BackendNeutralProjectScaffolder().CreatePlan(directory);
        string name = identity.ProjectName;
        string version = S1InteropPackageInfo.CliPackageVersion;
        return new Dictionary<string, string>
        {
            [name + ".csproj"] = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="local.build.props" Condition="Exists('local.build.props')" />
                  <Import Project=".s1interop/S1Interop.Compiler.props" />
                  <PropertyGroup>
                    <AssemblyName>{{name}}</AssemblyName>
                    <LangVersion>latest</LangVersion>
                    <Nullable>enable</Nullable>
                    <Configurations>Debug;Release;Debug Mono;Debug Il2Cpp;Release Mono;Release Il2Cpp</Configurations>
                    <S1InteropCompilerUseLocalTool>true</S1InteropCompilerUseLocalTool>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                  <Import Project=".s1interop/S1Interop.Compiler.targets" />
                </Project>
                """,
            ["Mod.cs"] = $$"""
                using MelonLoader;
                using ScheduleOne.NPCs;
                using UnityEngine;

                [assembly: MelonInfo(typeof(Entry), "{{name}}", "0.1.0", "Mod author")]
                [assembly: MelonGame("TVGS", "Schedule I")]

                public sealed class Entry : MelonMod
                {
                    public override void OnInitializeMelon() => LoggerInstance.Msg("{{name}} loaded.");

                    public override void OnUpdate()
                    {
                        if (Input.GetKeyDown(KeyCode.F8))
                            LoggerInstance.Msg($"NPC count: {NPCManager.NPCRegistry.Count}");
                    }
                }
                """,
            [".config/dotnet-tools.json"] = JsonSerializer.Serialize(new
            {
                version = 1, isRoot = true,
                tools = new Dictionary<string, object>
                {
                    [S1InteropPackageInfo.CliPackageId.ToLowerInvariant()] = new { version, commands = new[] { "s1interop" } }
                }
            }, new JsonSerializerOptions { WriteIndented = true }),
            [".s1interop/S1Interop.Compiler.props"] = ReadAsset("Compiler.props"),
            [".s1interop/S1Interop.Compiler.targets"] = ReadAsset("Compiler.targets"),
            ["local.build.props.example"] = """
                <Project>
                  <PropertyGroup>
                    <MonoGamePath>C:\Games\ScheduleI-Mono</MonoGamePath>
                    <Il2CppGamePath>C:\Games\ScheduleI-Il2Cpp</Il2CppGamePath>
                  </PropertyGroup>
                </Project>
                """,
            [".gitignore"] = "bin/\nobj/\nlocal.build.props\n*.user\n",
            ["README.md"] = $$"""
                # {{name}}

                Write ordinary ScheduleOne C# once and build it for Mono and IL2CPP. This experimental compiler does not yet support every language or runtime behavior.

                ## Setup

                Install a .NET 8 SDK and matching Mono and IL2CPP game versions with MelonLoader. Launch the IL2CPP installation once to generate its interop assemblies. Do not commit game paths or game assemblies.

                Restore the pinned S1Interop {{version}} tool:

                ```powershell
                dotnet tool restore
                ```

                If this version is an unpublished candidate, add its local package directory: `dotnet tool restore --add-source <candidate-feed>`. The manifest and `.s1interop` build files were generated together; commit both. The build uses this project's local tool, not a globally installed version or a checkout-specific DLL path.

                Configure both installations with the restored tool, replacing these example paths:

                ```powershell
                dotnet tool run s1interop -- setup . --mono-game-path 'C:\Games\ScheduleI-Mono' --il2cpp-game-path 'C:\Games\ScheduleI-Il2Cpp' --apply
                dotnet tool run s1interop -- doctor .
                ```

                Setup writes the ignored `local.build.props` and never overwrites an existing file. For manual configuration, copy `local.build.props.example` to `local.build.props` and edit its paths. Doctor checks reference availability; builds also verify matching game versions and native generation provenance.

                ## Build and load

                Build serially because the project's NuGet restore state is shared:

                ```powershell
                dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
                dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
                ```

                Outputs are `bin/Release/Mono/netstandard2.1/{{name}}.dll` and `bin/Release/Il2Cpp/net6.0/{{name}}.dll`. Copy the selected DLL into that installation's `Mods` directory. For IL2CPP, also copy the adjacent `S1Interop.Runtime.dll` into `UserLibs`. All compiler-built mods must use compatible support generations. Do not distribute `.s1interop/authoring` metadata from build outputs or game reference assemblies.

                Builds do not deploy or launch the game. On launch, check for `{{name}} loaded.` in MelonLoader. After loading a save, press F8 to log the NPC count. Verify the actual behavior separately on both runtimes.

                See the [compiler guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for supported behavior, existing-mod adoption, diagnostics, and remaining limits.
                """
        };
    }

    /// <summary>Writes a fresh scaffold and rejects nonempty destinations.</summary>
    public void Apply(string directory)
    {
        var files = CreatePlan(directory);
        string root = Path.GetFullPath(directory);
        if (File.Exists(root) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new ArgumentException("Target directory must be empty.", nameof(directory));
        foreach (var file in files)
        {
            string path = Path.Combine(root, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Never overwrite a file created after the initial emptiness check.
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(file.Value);
        }
    }

    private static string ReadAsset(string name)
    {
        using var stream = typeof(CompilerProjectScaffolder).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing compiler build asset: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
