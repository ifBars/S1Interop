using System.Security.Cryptography;
using System.Text;

namespace S1Interop.Core.Scaffolding;

/// <summary>
/// Creates the default dual-runtime or experimental backend-neutral project shape used by the S1Interop CLI.
/// </summary>
public sealed class BackendNeutralProjectScaffolder
{
    /// <summary>
    /// Builds a file plan for a new Schedule One mod project.
    /// </summary>
    /// <param name="targetDirectory">The directory that should contain the generated project.</param>
    /// <returns>The planned project paths and generated project name.</returns>
    /// <exception cref="ArgumentException">Thrown when a valid project name cannot be inferred from <paramref name="targetDirectory"/>.</exception>
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

    /// <summary>
    /// Writes the files described by a project plan using the legacy experimental backend-neutral shape.
    /// </summary>
    /// <param name="plan">The project plan to write to disk.</param>
    /// <remarks>
    /// Existing files at the planned paths are overwritten. Call <see cref="CreatePlan(string)"/> first when callers need to review paths before writing.
    /// </remarks>
    public void Apply(NewProjectPlan plan)
    {
        Apply(plan, experimentalBackendNeutral: true);
    }

    /// <summary>
    /// Writes either the default dual-runtime starter or the explicitly requested experimental backend-neutral starter.
    /// </summary>
    /// <param name="plan">The project plan to write.</param>
    /// <param name="experimentalBackendNeutral">Whether to create the experimental one-DLL facade-oriented project instead of the default dual-runtime project.</param>
    public void Apply(NewProjectPlan plan, bool experimentalBackendNeutral)
    {
        Directory.CreateDirectory(plan.TargetDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.StarterPath)!);
        File.WriteAllText(plan.SolutionPath, GenerateSolution(plan.ProjectName, experimentalBackendNeutral), Encoding.UTF8);
        File.WriteAllText(
            plan.ProjectPath,
            experimentalBackendNeutral
                ? GenerateBackendNeutralProject(plan.ProjectName)
                : GenerateDualRuntimeProject(plan.ProjectName),
            Encoding.UTF8);
        File.WriteAllText(plan.CorePath, GenerateCore(plan.ProjectName), Encoding.UTF8);
        File.WriteAllText(plan.StarterPath, new BackendNeutralStarterGenerator().GenerateSource(), Encoding.UTF8);
        File.WriteAllText(plan.LocalPropsExamplePath, GenerateLocalPropsExample(), Encoding.UTF8);
        File.WriteAllText(plan.GitignorePath, GenerateGitignore(), Encoding.UTF8);
        File.WriteAllText(
            plan.ReadmePath,
            experimentalBackendNeutral
                ? GenerateBackendNeutralReadme(plan.ProjectName)
                : GenerateDualRuntimeReadme(plan.ProjectName),
            Encoding.UTF8);
    }

    private static string GenerateBackendNeutralProject(string projectName) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="local.build.props" Condition="Exists('local.build.props')" />

          <PropertyGroup>
            <TargetFramework>netstandard2.1</TargetFramework>
            <LangVersion>10.0</LangVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <Configurations>Debug;Release</Configurations>
            <RootNamespace>{projectName}</RootNamespace>
            <AssemblyName>{projectName}</AssemblyName>
            <Version>0.1.0</Version>
            <S1InteropTargetRuntime Condition="'$(S1InteropTargetRuntime)'==''">Unknown</S1InteropTargetRuntime>
            <S1InteropReferenceRuntime Condition="'$(S1InteropReferenceRuntime)'==''">Mono</S1InteropReferenceRuntime>
            <BaseIntermediateOutputPath Condition="'$(BaseIntermediateOutputPath)'=='' and '$(S1InteropTargetRuntime)'=='Unknown'">obj\Single\</BaseIntermediateOutputPath>
            <BaseIntermediateOutputPath Condition="'$(BaseIntermediateOutputPath)'==''">obj\$(S1InteropReferenceRuntime)\</BaseIntermediateOutputPath>
            <IntermediateOutputPath Condition="'$(S1InteropTargetRuntime)'=='Unknown'">obj\Single\$(Configuration)\$(TargetFramework)\</IntermediateOutputPath>
            <IntermediateOutputPath Condition="'$(IntermediateOutputPath)'==''">obj\$(S1InteropReferenceRuntime)\$(Configuration)\$(TargetFramework)\</IntermediateOutputPath>
            <BaseOutputPath Condition="'$(BaseOutputPath)'=='' and '$(S1InteropTargetRuntime)'=='Unknown'">bin\Single\</BaseOutputPath>
            <BaseOutputPath Condition="'$(BaseOutputPath)'==''">bin\$(S1InteropReferenceRuntime)\</BaseOutputPath>
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
            <Error Text="Backend-neutral single-assembly builds must use S1InteropTargetRuntime=Unknown with S1InteropReferenceRuntime=Mono. IL2CPP reference builds are validation-only; pass -p:S1InteropTargetRuntime=Il2Cpp with -p:S1InteropReferenceRuntime=Il2Cpp when you intentionally want that check." Condition="'$(S1InteropTargetRuntime)'=='Unknown' and '$(S1InteropReferenceRuntime)'!='Mono'" />
            <Error Text="Missing MelonLoader at $(MelonLoaderPath). Copy local.build.props.example to local.build.props and set MonoGamePath, or pass -p:MonoGamePath=..." Condition="'$(MelonLoaderPath)'=='' or !Exists('$(MelonLoaderPath)\MelonLoader.dll')" />
            <Error Text="Missing Unity assemblies at $(ManagedPath). Copy local.build.props.example to local.build.props and set MonoGamePath, or pass -p:MonoGamePath=..." Condition="'$(ManagedPath)'=='' or !Exists('$(ManagedPath)\UnityEngine.CoreModule.dll')" />
            <Error Text="Missing Schedule One game assembly at $(ManagedPath). Copy local.build.props.example to local.build.props and set MonoGamePath/Il2CppGamePath, or pass the game path as an MSBuild property." Condition="'$(ManagedPath)'=='' or !Exists('$(ManagedPath)\Assembly-CSharp.dll')" />
            <Error Text="Missing ScheduleOne.Core at $(ManagedPath). Copy local.build.props.example to local.build.props and set MonoGamePath/Il2CppGamePath, or pass the game path as an MSBuild property." Condition="'$(S1InteropReferenceRuntime)'!='Il2Cpp' and ('$(ManagedPath)'=='' or !Exists('$(ManagedPath)\ScheduleOne.Core.dll'))" />
            <Error Text="Missing Il2CppScheduleOne.Core at $(ManagedPath). Copy local.build.props.example to local.build.props and set Il2CppGamePath, or pass the game path as an MSBuild property." Condition="'$(S1InteropReferenceRuntime)'=='Il2Cpp' and ('$(ManagedPath)'=='' or !Exists('$(ManagedPath)\Il2CppScheduleOne.Core.dll'))" />
          </Target>

        </Project>
        """;

    private static string GenerateDualRuntimeProject(string projectName) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="local.build.props" Condition="Exists('local.build.props')" />

          <PropertyGroup>
            <TargetFramework>netstandard2.1</TargetFramework>
            <LangVersion>10.0</LangVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <Configurations>Debug Mono;Release Mono;Debug Il2Cpp;Release Il2Cpp</Configurations>
            <RootNamespace>{projectName}</RootNamespace>
            <AssemblyName>{projectName}</AssemblyName>
            <Version>0.1.0</Version>
          </PropertyGroup>

          <PropertyGroup Condition="'$(Configuration)'=='Debug Mono' Or '$(Configuration)'=='Release Mono'">
            <TargetFramework>netstandard2.1</TargetFramework>
            <S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>
            <S1InteropReferenceRuntime>Mono</S1InteropReferenceRuntime>
            <DefineConstants>$(DefineConstants);MONO</DefineConstants>
            <GamePath>$(MonoGamePath)</GamePath>
            <ManagedPath>$(GamePath)\Schedule I_Data\Managed</ManagedPath>
            <MelonLoaderPath>$(GamePath)\MelonLoader\net35</MelonLoaderPath>
            <BaseOutputPath>bin\Mono\</BaseOutputPath>
            <IntermediateOutputPath>obj\Mono\$(Configuration)\</IntermediateOutputPath>
          </PropertyGroup>

          <PropertyGroup Condition="'$(Configuration)'=='Debug Il2Cpp' Or '$(Configuration)'=='Release Il2Cpp'">
            <TargetFramework>net6.0</TargetFramework>
            <S1InteropTargetRuntime>Il2Cpp</S1InteropTargetRuntime>
            <S1InteropReferenceRuntime>Il2Cpp</S1InteropReferenceRuntime>
            <DefineConstants>$(DefineConstants);IL2CPP</DefineConstants>
            <GamePath>$(Il2CppGamePath)</GamePath>
            <ManagedPath>$(GamePath)\MelonLoader\Il2CppAssemblies</ManagedPath>
            <MelonLoaderPath>$(GamePath)\MelonLoader\net6</MelonLoaderPath>
            <BaseOutputPath>bin\Il2Cpp\</BaseOutputPath>
            <IntermediateOutputPath>obj\Il2Cpp\$(Configuration)\</IntermediateOutputPath>
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
            <Error Text="Choose one of the supported configurations: Debug Mono, Release Mono, Debug Il2Cpp, or Release Il2Cpp." Condition="'$(S1InteropTargetRuntime)'==''" />
            <Error Text="Missing MelonLoader at $(MelonLoaderPath). Run s1interop doctor, then s1interop setup . --apply." Condition="'$(MelonLoaderPath)'=='' or !Exists('$(MelonLoaderPath)\MelonLoader.dll')" />
            <Error Text="Missing game references at $(ManagedPath). Run s1interop doctor and verify the selected branch has completed MelonLoader setup." Condition="'$(ManagedPath)'=='' or !Exists('$(ManagedPath)\Assembly-CSharp.dll')" />
            <Error Text="Missing ScheduleOne.Core at $(ManagedPath)." Condition="'$(S1InteropTargetRuntime)'=='Mono' and !Exists('$(ManagedPath)\ScheduleOne.Core.dll')" />
            <Error Text="Missing Il2CppScheduleOne.Core at $(ManagedPath)." Condition="'$(S1InteropTargetRuntime)'=='Il2Cpp' and !Exists('$(ManagedPath)\Il2CppScheduleOne.Core.dll')" />
          </Target>
        </Project>
        """;

    private static string GenerateSolution(string projectName, bool experimentalBackendNeutral)
    {
        string projectGuid = CreateStableGuid($"{projectName}.csproj").ToString("B").ToUpperInvariant();
        const string projectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        string[] configurations = experimentalBackendNeutral
            ? ["Debug", "Release"]
            : ["Debug Mono", "Release Mono", "Debug Il2Cpp", "Release Il2Cpp"];
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
                LoggerInstance.Msg($"{ModName} loaded on {S1Interop.Generated.S1InteropRuntime.Backend}.");
            }
        }
        """;

    private static string GenerateLocalPropsExample() =>
        $"""
        <Project>
          <PropertyGroup>
            <!-- Local-only game paths. Copy this file to local.build.props and keep that file out of source control. -->
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

    private static string GenerateBackendNeutralReadme(string projectName) =>
        $$"""
        # {{projectName}}

        Experimental backend-neutral Schedule One mod scaffold created by S1Interop.

        > [!WARNING]
        > The one-DLL facade path is experimental and fragile. Prefer the default dual-runtime `s1interop new` scaffold for a first mod or production fallback. Validate this project against both reference surfaces and in both game branches before distributing it.

        This scaffold is the one-DLL path. Existing mods can also use S1Interop for diagnostics-only adoption, dual-runtime migration, or a few generated helpers without using this full shape.

        ## First local setup

        Diagnose and preview the local inputs before writing anything:

        ```powershell
        s1interop doctor .
        s1interop setup .
        s1interop setup . --apply
        ```

        Pass `--mono-game-path` or `--il2cpp-game-path` when detection needs help. `setup` writes only the ignored `local.build.props`, never installs software, and never overwrites an existing file. Mono is enough for the first build; add the IL2CPP path before treating the experiment as validated.

        Do not copy game assemblies, generated IL2CPP wrappers, decompiled dumps, prefabs, scenes, textures, or exported Unity projects into this repository.

        Open `{{projectName}}.sln` in Visual Studio or Rider. `Debug` and `Release` produce the same kind of DLL you ship: one backend-neutral assembly built against Mono references with runtime backend detection.

        ```powershell
        dotnet build .\{{projectName}}.sln -c Debug
        ```

        A successful build writes the shipping DLL to `bin\Single\Debug\netstandard2.1\{{projectName}}.dll`.

        If you intentionally want a compile-only IL2CPP reference check while developing S1Interop declarations, pass explicit properties and do not ship that output:

        ```powershell
        dotnet build .\{{projectName}}.sln -c Debug -p:S1InteropReferenceRuntime=Il2Cpp -p:S1InteropTargetRuntime=Il2Cpp
        ```

        You should not need separate Mono and IL2CPP implementations for ordinary backend-neutral code.

        This is still a normal MelonLoader mod. Add S1API, MAPI, SteamNetworkLib, bGUI, or dedicated server references when those libraries fit your mod. Use S1Interop for the direct `ScheduleOne.*` / `Il2CppScheduleOne.*` calls that would otherwise need backend-specific conditionals.

        ## Writing your first game-facing code

        Add game type declarations in `S1Interop.Generated/S1Interop.BackendNeutral.cs` as your mod touches Schedule I APIs.
        Leave the file empty if you only want diagnostics and built-in helper generation. Prefer `S1InteropType` declarations and generated SDK output when you want facade access. Use explicit member declarations only for private members, ambiguous overloads, or migration-specific overrides.

        To seed broad type registration from your local game references, run:

        ```powershell
        s1interop sdkgen . --full-sdk --apply
        ```

        ```csharp
        [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
        ```

        Build after adding a declaration. The generator then creates the matching facade under `S1Interop.ScheduleOne.*` so one assembly can resolve the Mono or IL2CPP game type at runtime.

        ## Useful next commands

        ```powershell
        s1interop analyze .
        s1interop lint .
        s1interop sdkgen . --apply
        s1interop sdkgen . --full-sdk --apply
        ```

        Use `analyze` and `lint` whenever you want feedback without file edits. Use `sdkgen . --apply` once your source references the game types it needs. Use `--full-sdk` for an exploratory blank project, then keep type/member declarations narrow as the mod settles.
        """;

    private static string GenerateDualRuntimeReadme(string projectName) =>
        $$"""
        # {{projectName}}

        Schedule One MelonLoader mod scaffold created by S1Interop.

        This default starter keeps Mono and IL2CPP builds explicit. S1Interop supplies compile-time diagnostics and runtime reporting without requiring the experimental backend-neutral facade path.

        ## First local setup

        Preview the detected inputs:

        ```powershell
        s1interop doctor .
        s1interop setup .
        ```

        When every required check is ready, write only the ignored local configuration:

        ```powershell
        s1interop setup . --apply
        ```

        `setup` does not install software, change your project, or overwrite an existing `local.build.props`.

        ## Build and success check

        Build the branch you have installed:

        ```powershell
        dotnet build .\{{projectName}}.sln -c "Debug Mono"
        dotnet build .\{{projectName}}.sln -c "Debug Il2Cpp"
        ```

        The DLLs are written under:

        ```text
        bin\Mono\Debug Mono\netstandard2.1\{{projectName}}.dll
        bin\Il2Cpp\Debug Il2Cpp\net6.0\{{projectName}}.dll
        ```

        Copy only the DLL matching the active game branch into that install's `Mods` folder. After launch, expect:

        ```text
        {{projectName}} loaded on Mono.
        ```

        or:

        ```text
        {{projectName}} loaded on Il2Cpp.
        ```

        ## Common tasks

        ```powershell
        s1interop analyze .
        s1interop lint .
        s1interop verify-migration . --dual-runtime --build
        ```

        These commands provide the stable early value: compile-time help, diagnostics, safe migration planning, and explicit validation for both runtimes.

        ## Experimental backend-neutral facades

        Backend-neutral facades are opt-in and still fragile. Keep the dual-runtime build as the safe fallback until your exact mod has sustained real-world validation on both branches. Read the S1Interop backend-neutral documentation before adding declarations or running `sdkgen`.

        Do not commit `local.build.props`, game assemblies, generated IL2CPP wrappers, decompiled output, or game assets.
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
