using System.Security.Cryptography;
using System.Text;

namespace S1Interop.Core.Scaffolding;

public sealed class BackendNeutralProjectScaffolder
{
    public NewProjectPlan CreatePlan(string targetDirectory)
    {
        string fullTargetDirectory = Path.GetFullPath(targetDirectory);
        string projectName = SanitizeIdentifier(new DirectoryInfo(fullTargetDirectory).Name);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("Could not infer a valid project name from the target path.", nameof(targetDirectory));
        }

        string starterPath = Path.Combine(fullTargetDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
        return new NewProjectPlan(
            projectName,
            fullTargetDirectory,
            Path.Combine(fullTargetDirectory, $"{projectName}.sln"),
            Path.Combine(fullTargetDirectory, $"{projectName}.csproj"),
            Path.Combine(fullTargetDirectory, "ModCore.cs"),
            starterPath,
            Path.Combine(fullTargetDirectory, "local.build.props.example"),
            Path.Combine(fullTargetDirectory, ".gitignore"),
            Path.Combine(fullTargetDirectory, "README.md"));
    }

    public void Apply(NewProjectPlan plan)
    {
        Directory.CreateDirectory(plan.TargetDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.StarterPath)!);
        File.WriteAllText(plan.SolutionPath, GenerateSolution(plan.ProjectName), Encoding.UTF8);
        File.WriteAllText(plan.ProjectPath, GenerateProject(plan.ProjectName), Encoding.UTF8);
        File.WriteAllText(plan.CorePath, GenerateCore(plan.ProjectName), Encoding.UTF8);
        File.WriteAllText(plan.StarterPath, new BackendNeutralStarterGenerator().GenerateSource(), Encoding.UTF8);
        File.WriteAllText(plan.LocalPropsExamplePath, GenerateLocalPropsExample(), Encoding.UTF8);
        File.WriteAllText(plan.GitignorePath, GenerateGitignore(), Encoding.UTF8);
        File.WriteAllText(plan.ReadmePath, GenerateReadme(plan.ProjectName), Encoding.UTF8);
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
            <Configurations>Debug;Release;Debug Il2Cpp;Release Il2Cpp</Configurations>
            <RootNamespace>{projectName}</RootNamespace>
            <AssemblyName>{projectName}</AssemblyName>
            <Version>0.1.0</Version>
            <S1InteropReferenceRuntime Condition="'$(S1InteropReferenceRuntime)'=='' and ('$(Configuration)'=='Debug Il2Cpp' or '$(Configuration)'=='Release Il2Cpp')">Il2Cpp</S1InteropReferenceRuntime>
            <S1InteropReferenceRuntime Condition="'$(S1InteropReferenceRuntime)'==''">Mono</S1InteropReferenceRuntime>
            <GamePath Condition="'$(GamePath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp'">$(Il2CppGamePath)</GamePath>
            <GamePath Condition="'$(GamePath)'==''">$(MonoGamePath)</GamePath>
            <ManagedPath Condition="'$(ManagedPath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp' and '$(GamePath)'!=''">$(GamePath)\MelonLoader\Il2CppAssemblies</ManagedPath>
            <ManagedPath Condition="'$(ManagedPath)'=='' and '$(GamePath)'!=''">$(GamePath)\Schedule I_Data\Managed</ManagedPath>
            <MelonLoaderPath Condition="'$(MelonLoaderPath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp' and '$(GamePath)'!=''">$(GamePath)\MelonLoader\net6</MelonLoaderPath>
            <MelonLoaderPath Condition="'$(MelonLoaderPath)'=='' and '$(GamePath)'!=''">$(GamePath)\MelonLoader\net35</MelonLoaderPath>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="{S1InteropPackageInfo.GeneratorsPackageId}" Version="{S1InteropPackageInfo.GeneratorsPackageVersion}" PrivateAssets="{S1InteropPackageInfo.PrivateAssets}" IncludeAssets="{S1InteropPackageInfo.AnalyzerIncludeAssets}" />
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

    private static string GenerateSolution(string projectName)
    {
        string projectGuid = CreateStableGuid($"{projectName}.csproj").ToString("B").ToUpperInvariant();
        const string projectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        string[] configurations =
        [
            "Debug",
            "Release",
            "Debug Il2Cpp",
            "Release Il2Cpp"
        ];
        var builder = new StringBuilder();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("# Visual Studio Version 17");
        builder.AppendLine("VisualStudioVersion = 17.0.31903.59");
        builder.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        builder.AppendLine($"Project(\"{projectTypeGuid}\") = \"{projectName}\", \"{projectName}.csproj\", \"{projectGuid}\"");
        builder.AppendLine("EndProject");
        builder.AppendLine("Global");
        builder.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        foreach (string configuration in configurations)
        {
            builder.AppendLine($"\t\t{configuration}|Any CPU = {configuration}|Any CPU");
        }

        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (string configuration in configurations)
        {
            builder.AppendLine($"\t\t{projectGuid}.{configuration}|Any CPU.ActiveCfg = {configuration}|Any CPU");
            builder.AppendLine($"\t\t{projectGuid}.{configuration}|Any CPU.Build.0 = {configuration}|Any CPU");
        }

        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("EndGlobal");
        return builder.ToString();
    }

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
        $"""
        <Project>
          <PropertyGroup>
            <!-- Local-only paths and package feeds. Copy this file to local.build.props and keep that file out of source control. -->
            <MonoGamePath>C:\Path\To\Schedule I_alternate</MonoGamePath>
            <Il2CppGamePath>C:\Path\To\Schedule I_public</Il2CppGamePath>
            <!-- Required only while using unpublished/local S1Interop.Generators packages. -->
            <{S1InteropPackageInfo.GeneratorsPackageSourceProperty}>C:\Path\To\S1Interop\artifacts\packages</{S1InteropPackageInfo.GeneratorsPackageSourceProperty}>
            <{S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty} Condition="'$({S1InteropPackageInfo.GeneratorsPackageSourceProperty})'!=''">$({S1InteropPackageInfo.GeneratorsPackageSourceProperty});$({S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty})</{S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty}>
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
        If you are using an unpublished/local build of S1Interop, set `{S1InteropPackageInfo.GeneratorsPackageSourceProperty}` to the folder containing `{S1InteropPackageInfo.GeneratorsPackageId}.*.nupkg`.
        `local.build.props` is ignored so machine-specific paths do not get committed.

        Open `{{projectName}}.sln` in Visual Studio or Rider. `Debug` and `Release` use Mono references.
        `Debug Il2Cpp` and `Release Il2Cpp` use IL2CPP references while keeping the same source and output assembly logic.

        ```powershell
        dotnet build .\{{projectName}}.sln -c Debug
        dotnet build .\{{projectName}}.sln -c "Debug Il2Cpp"
        ```

        Add game type declarations in `S1Interop.Generated/S1Interop.BackendNeutral.cs` as your mod touches Schedule One APIs.
        Prefer `S1InteropType` declarations and generated SDK output. Use explicit member declarations only for private members, ambiguous overloads, or migration-specific overrides.

        To seed a generated backend-neutral SDK from your local game references instead of writing type declarations by hand, run:

        ```powershell
        s1interop sdkgen . --full-sdk --apply
        ```

        ```csharp
        [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
        ```

        Use generated facades under `S1Interop.ScheduleOne.*` when you want one assembly to resolve Mono or IL2CPP game types at runtime.
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

    private static Guid CreateStableGuid(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
