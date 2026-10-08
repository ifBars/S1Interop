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

                Write ordinary ScheduleOne C# and build it for Mono and IL2CPP. The compiler is experimental; test your mod's behavior on each runtime.

                ## Restore the compiler

                Run commands from this project directory. Use the .NET 8 SDK and the package feed from [Install S1Interop](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/getting-started.md).

                Restore the pinned S1Interop {{version}} tool from the same PowerShell window used for installation:

                ```powershell
                dotnet tool restore --add-source $candidateFeed
                ```

                In a new terminal, first set `$candidateFeed` to the absolute path of that installation's `artifacts/packages` directory. Once your pinned version is published on NuGet.org, plain `dotnet tool restore` is sufficient.

                Commit `.config/dotnet-tools.json` and the matching `.s1interop` build files. Builds use this project's pinned tool.

                ## Configure the game

                Prepare matching Mono and IL2CPP game versions with MelonLoader. Launch each installation once. Wait for IL2CPP interop generation to finish, then close both games.

                Replace the example paths with your game installation roots:

                ```powershell
                dotnet tool run s1interop -- setup . --mono-game-path 'C:\Games\ScheduleI-Mono' --il2cpp-game-path 'C:\Games\ScheduleI-Il2Cpp' --apply
                dotnet tool run s1interop -- doctor .
                ```

                Setup creates the ignored `local.build.props` file. If it already exists, edit its paths directly. Doctor checks reference availability; builds also verify game versions and native generation provenance.

                ## Build and load

                Run these commands one at a time:

                ```powershell
                dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
                dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
                ```

                | Runtime | Mod DLL |
                | --- | --- |
                | Mono | `bin/Release/Mono/netstandard2.1/{{name}}.dll` |
                | IL2CPP | `bin/Release/Il2Cpp/net6.0/{{name}}.dll` |

                Builds do not deploy or launch the game. With the game closed, copy its matching mod DLL into `Mods`. For IL2CPP, also copy the adjacent `S1Interop.Runtime.dll` into `UserLibs`. Installed compiler-built mods must use compatible support generations.

                Launch the game and check MelonLoader for `{{name}} loaded.`. Load a save, then press F8 to log the NPC count. Repeat on the other runtime.

                ## Develop your mod

                Edit `Mod.cs` using ordinary game and Unity APIs. Rebuild the selected runtime, replace its deployed DLL with the game closed, and test your change. Leave generated files under `obj` unchanged.

                Follow [Everyday development](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/common-tasks.md) for reference updates and [Troubleshooting](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/troubleshooting.md) for build failures.

                Before sharing, follow [Test and distribute a mod](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/distributing-mods.md). Keep game references, local paths, and `.s1interop/authoring` metadata out of player downloads.
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
