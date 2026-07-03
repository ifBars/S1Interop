using System.Text;
using System.Text.Json;
using S1Interop.Core.Generators;

internal static class NewProjectCommand
{
    public static int Run(ParsedCommand command)
    {
        string targetDirectory = Path.GetFullPath(command.Path);
        if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
        {
            Console.Error.WriteLine($"s1interop: target directory is not empty: {targetDirectory}");
            return 2;
        }

        string projectName = SanitizeIdentifier(new DirectoryInfo(targetDirectory).Name);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            Console.Error.WriteLine("s1interop: could not infer a valid project name from the target path.");
            return 2;
        }

        string projectPath = Path.Combine(targetDirectory, $"{projectName}.csproj");
        string corePath = Path.Combine(targetDirectory, "ModCore.cs");
        string starterPath = Path.Combine(targetDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
        string localPropsExamplePath = Path.Combine(targetDirectory, "local.build.props.example");
        string gitignorePath = Path.Combine(targetDirectory, ".gitignore");
        string readmePath = Path.Combine(targetDirectory, "README.md");
        string[] plannedFiles = [projectPath, corePath, starterPath, localPropsExamplePath, gitignorePath, readmePath];

        if (!command.Apply)
        {
            PrintDryRun(command, targetDirectory, projectName, plannedFiles);
            return 0;
        }

        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(starterPath)!);
        File.WriteAllText(projectPath, GenerateProject(projectName), Encoding.UTF8);
        File.WriteAllText(corePath, GenerateCore(projectName), Encoding.UTF8);
        File.WriteAllText(starterPath, new BackendNeutralStarterGenerator().GenerateSource(), Encoding.UTF8);
        File.WriteAllText(localPropsExamplePath, GenerateLocalPropsExample(), Encoding.UTF8);
        File.WriteAllText(gitignorePath, GenerateGitignore(), Encoding.UTF8);
        File.WriteAllText(readmePath, GenerateReadme(projectName), Encoding.UTF8);

        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                projectName,
                targetDirectory,
                files = plannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }

        Console.WriteLine($"S1Interop backend-neutral project created: {projectName}");
        Console.WriteLine($"Directory: {targetDirectory}");
        foreach (string file in plannedFiles)
        {
            Console.WriteLine($"  created {file}");
        }

        return 0;
    }

    private static void PrintDryRun(ParsedCommand command, string targetDirectory, string projectName, IReadOnlyList<string> plannedFiles)
    {
        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                projectName,
                targetDirectory,
                apply = false,
                files = plannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return;
        }

        Console.WriteLine($"S1Interop new project dry-run: {projectName}");
        Console.WriteLine($"Directory: {targetDirectory}");
        foreach (string file in plannedFiles)
        {
            Console.WriteLine($"  create {file}");
        }
        Console.WriteLine("Run again with --apply to write files.");
    }

    private static string GenerateProject(string projectName) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="local.build.props" Condition="Exists('local.build.props')" />

          <PropertyGroup>
            <TargetFramework>netstandard2.1</TargetFramework>
            <LangVersion>10.0</LangVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{projectName}</RootNamespace>
            <AssemblyName>{projectName}</AssemblyName>
            <Version>0.1.0</Version>
            <S1InteropReferenceRuntime Condition="'$(S1InteropReferenceRuntime)'==''">Mono</S1InteropReferenceRuntime>
            <GamePath Condition="'$(GamePath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp'">$(Il2CppGamePath)</GamePath>
            <GamePath Condition="'$(GamePath)'==''">$(MonoGamePath)</GamePath>
            <ManagedPath Condition="'$(ManagedPath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp' and '$(GamePath)'!=''">$(GamePath)\MelonLoader\Il2CppAssemblies</ManagedPath>
            <ManagedPath Condition="'$(ManagedPath)'=='' and '$(GamePath)'!=''">$(GamePath)\Schedule I_Data\Managed</ManagedPath>
            <MelonLoaderPath Condition="'$(MelonLoaderPath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp' and '$(GamePath)'!=''">$(GamePath)\MelonLoader\net6</MelonLoaderPath>
            <MelonLoaderPath Condition="'$(MelonLoaderPath)'=='' and '$(GamePath)'!=''">$(GamePath)\MelonLoader\net35</MelonLoaderPath>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.1" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
          </ItemGroup>

          <ItemGroup>
            <Reference Include="MelonLoader">
              <HintPath>$(MelonLoaderPath)\MelonLoader.dll</HintPath>
              <Private>false</Private>
            </Reference>
            <Reference Include="0Harmony">
              <HintPath>$(MelonLoaderPath)\0Harmony.dll</HintPath>
              <Private>false</Private>
            </Reference>
            <Reference Include="UnityEngine.CoreModule">
              <HintPath>$(ManagedPath)\UnityEngine.CoreModule.dll</HintPath>
              <Private>false</Private>
            </Reference>
            <Reference Include="Assembly-CSharp">
              <HintPath>$(ManagedPath)\Assembly-CSharp.dll</HintPath>
              <Private>false</Private>
            </Reference>
            <Reference Include="ScheduleOne.Core" Condition="'$(S1InteropReferenceRuntime)'!='Il2Cpp'">
              <HintPath>$(ManagedPath)\ScheduleOne.Core.dll</HintPath>
              <Private>false</Private>
            </Reference>
            <Reference Include="Il2CppScheduleOne.Core" Condition="'$(S1InteropReferenceRuntime)'=='Il2Cpp'">
              <HintPath>$(ManagedPath)\Il2CppScheduleOne.Core.dll</HintPath>
              <Private>false</Private>
            </Reference>
          </ItemGroup>

          <Target Name="ValidateS1InteropLocalPaths" BeforeTargets="ResolveReferences">
            <Error Text="Missing MelonLoader at $(MelonLoaderPath). Copy local.build.props.example to local.build.props and set MonoGamePath, or pass -p:MonoGamePath=..." Condition="'$(MelonLoaderPath)'=='' or !Exists('$(MelonLoaderPath)\MelonLoader.dll')" />
            <Error Text="Missing Unity assemblies at $(ManagedPath). Copy local.build.props.example to local.build.props and set MonoGamePath, or pass -p:MonoGamePath=..." Condition="'$(ManagedPath)'=='' or !Exists('$(ManagedPath)\UnityEngine.CoreModule.dll')" />
            <Error Text="Missing Schedule One game assembly at $(ManagedPath). Copy local.build.props.example to local.build.props and set MonoGamePath/Il2CppGamePath, or pass the game path as an MSBuild property." Condition="'$(ManagedPath)'=='' or !Exists('$(ManagedPath)\Assembly-CSharp.dll')" />
            <Error Text="Missing ScheduleOne.Core at $(ManagedPath). Copy local.build.props.example to local.build.props and set MonoGamePath/Il2CppGamePath, or pass the game path as an MSBuild property." Condition="'$(S1InteropReferenceRuntime)'!='Il2Cpp' and ('$(ManagedPath)'=='' or !Exists('$(ManagedPath)\ScheduleOne.Core.dll'))" />
            <Error Text="Missing Il2CppScheduleOne.Core at $(ManagedPath). Copy local.build.props.example to local.build.props and set Il2CppGamePath, or pass the game path as an MSBuild property." Condition="'$(S1InteropReferenceRuntime)'=='Il2Cpp' and ('$(ManagedPath)'=='' or !Exists('$(ManagedPath)\Il2CppScheduleOne.Core.dll'))" />
          </Target>

        </Project>
        """;

    private static string GenerateCore(string projectName) =>
        $$"""
        using MelonLoader;

        [assembly: MelonInfo(typeof({{projectName}}.ModCore), "{{projectName}}", "0.1.0", "YourName")]
        [assembly: MelonGame("TVGS", "Schedule I")]

        namespace {{projectName}};

        public sealed class ModCore : MelonMod
        {
            public const string ModName = "{{projectName}}";

            public override void OnInitializeMelon()
            {
                LoggerInstance.Msg($"{ModName} loaded.");
            }
        }
        """;

    private static string GenerateLocalPropsExample() =>
        """
        <Project>
          <PropertyGroup>
            <!-- Local-only paths. Copy this file to local.build.props and keep that file out of source control. -->
            <MonoGamePath>C:\Path\To\Schedule I_alternate</MonoGamePath>
            <Il2CppGamePath>C:\Path\To\Schedule I_public</Il2CppGamePath>
          </PropertyGroup>
        </Project>
        """;

    private static string GenerateGitignore() =>
        """
        bin/
        obj/
        local.build.props
        """;

    private static string GenerateReadme(string projectName) =>
        $$"""
        # {{projectName}}

        Backend-neutral Schedule One mod scaffold created by S1Interop.

        Before building locally, copy `local.build.props.example` to `local.build.props` and set your own game paths.
        `local.build.props` is ignored so machine-specific paths do not get committed.

        The default build uses Mono reference assemblies and keeps generated S1Interop helpers runtime-detecting.
        To sanity-check the same source against IL2CPP references, build with:

        ```powershell
        dotnet build -p:S1InteropReferenceRuntime=Il2Cpp
        ```

        Add game type declarations in `S1Interop.Generated/S1Interop.BackendNeutral.cs` as your mod touches Schedule One APIs.

        To seed a generated backend-neutral SDK from your local game references instead of writing type declarations by hand, run:

        ```powershell
        s1interop sdkgen . --full-sdk --apply
        ```

        ```csharp
        [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
        [assembly: S1Interop.S1InteropMember("PlayerCamera", "Instance", Alias = "PlayerCameraInstance", IsStatic = true)]
        ```

        Use generated helpers from `S1Interop.Generated.S1InteropTypeRegistry` and `S1Interop.Generated.S1InteropMemberRegistry` when you want one assembly to resolve Mono or IL2CPP game types at runtime.
        """;

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
        }

        if (builder.Length == 0)
        {
            return string.Empty;
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
