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
    /// <param name="experimentalBackendNeutral">Whether to plan the experimental one-DLL facade project instead of the default dual-runtime project.</param>
    /// <returns>The planned project paths and generated project name.</returns>
    /// <exception cref="ArgumentException">Thrown when a valid project name cannot be inferred from <paramref name="targetDirectory"/>.</exception>
    public NewProjectPlan CreatePlan(string targetDirectory, bool experimentalBackendNeutral = false)
    {
        string fullTargetDirectory = Path.GetFullPath(targetDirectory);
        string projectName = SanitizeIdentifier(new DirectoryInfo(fullTargetDirectory).Name);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("Could not infer a valid project name from the target path.", nameof(targetDirectory));
        }

        // Dual-runtime projects only need a declaration file once they opt into facades; `s1interop init` adds it then.
        string? starterPath = experimentalBackendNeutral
            ? Path.Combine(fullTargetDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName)
            : null;
        return new NewProjectPlan(
            projectName,
            experimentalBackendNeutral,
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
    /// Writes the files described by a project plan.
    /// </summary>
    /// <param name="plan">The project plan to write to disk.</param>
    /// <remarks>
    /// Existing files at the planned paths are overwritten. Call <see cref="CreatePlan(string, bool)"/> first when callers need to review paths before writing.
    /// </remarks>
    public void Apply(NewProjectPlan plan)
    {
        bool experimentalBackendNeutral = plan.ExperimentalBackendNeutral;
        Directory.CreateDirectory(plan.TargetDirectory);
        File.WriteAllText(plan.SolutionPath, GenerateSolution(plan.ProjectName, experimentalBackendNeutral), Encoding.UTF8);
        File.WriteAllText(
            plan.ProjectPath,
            experimentalBackendNeutral
                ? GenerateBackendNeutralProject(plan.ProjectName)
                : GenerateDualRuntimeProject(plan.ProjectName),
            Encoding.UTF8);
        File.WriteAllText(
            plan.CorePath,
            experimentalBackendNeutral
                ? GenerateBackendNeutralCore(plan.ProjectName)
                : GenerateDualRuntimeCore(plan.ProjectName),
            Encoding.UTF8);
        if (plan.StarterPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.StarterPath)!);
            File.WriteAllText(plan.StarterPath, new BackendNeutralStarterGenerator().GenerateSource(), Encoding.UTF8);
        }

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
            <!-- S1Interop.Generators references MelonLoader plus the game and Unity assemblies of the selected runtime. -->
            <S1InteropGameReferences>true</S1InteropGameReferences>
          </PropertyGroup>

          <PropertyGroup Condition="'$(Configuration)'=='Debug Mono' Or '$(Configuration)'=='Release Mono'">
            <TargetFramework>netstandard2.1</TargetFramework>
            <S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>
            <DefineConstants>$(DefineConstants);MONO</DefineConstants>
            <BaseOutputPath>bin\Mono\</BaseOutputPath>
            <IntermediateOutputPath>obj\Mono\$(Configuration)\</IntermediateOutputPath>
          </PropertyGroup>

          <PropertyGroup Condition="'$(Configuration)'=='Debug Il2Cpp' Or '$(Configuration)'=='Release Il2Cpp'">
            <TargetFramework>net6.0</TargetFramework>
            <S1InteropTargetRuntime>Il2Cpp</S1InteropTargetRuntime>
            <DefineConstants>$(DefineConstants);IL2CPP</DefineConstants>
            <BaseOutputPath>bin\Il2Cpp\</BaseOutputPath>
            <IntermediateOutputPath>obj\Il2Cpp\$(Configuration)\</IntermediateOutputPath>
          </PropertyGroup>

          <PropertyGroup Condition="'$(Configuration)'=='Debug Mono' Or '$(Configuration)'=='Debug Il2Cpp'">
            <!-- Debug builds copy the DLL into the matching install's Mods folder. Close the game before building. -->
            <S1InteropDeployToGame>true</S1InteropDeployToGame>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="{S1InteropPackageInfo.GeneratorsPackageId}" Version="{S1InteropPackageInfo.GeneratorsPackageVersion}" PrivateAssets="{S1InteropPackageInfo.PrivateAssets}" IncludeAssets="{S1InteropPackageInfo.AnalyzerIncludeAssets}" />
          </ItemGroup>

          <ItemGroup>
            <!-- Imported in every file. IL2CPP builds import the Il2Cpp-prefixed namespace, so source needs no #if blocks. -->
            <S1InteropUsing Include="ScheduleOne.NPCs" />
            <S1InteropUsing Include="ScheduleOne.PlayerScripts" />
            <!-- Cross-runtime helpers: TryCast, AsEnumerable, ToManagedList, UnityEvent AddListener/Subscribe. -->
            <Using Include="S1Interop" />
          </ItemGroup>
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

    private static string GenerateDualRuntimeCore(string projectName) =>
        $$"""
        using MelonLoader;
        using UnityEngine;

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

            public override void OnUpdate()
            {
                // NPCManager is ScheduleOne.NPCs.NPCManager on Mono and Il2CppScheduleOne.NPCs.NPCManager on IL2CPP.
                // The S1InteropUsing items in the .csproj import the right namespace, so this file needs no #if blocks.
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    LoggerInstance.Msg($"{NPCManager.NPCRegistry.Count} NPCs are registered.");
                }
            }
        }
        """;

    private static string GenerateBackendNeutralCore(string projectName) =>
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

        One source tree builds a separate Mono DLL and IL2CPP DLL. S1Interop references the game for whichever runtime you build, imports the right game namespaces, and copies Debug builds into that install's `Mods` folder.

        ## First local setup

        Preview the detected inputs, then write only the ignored local configuration:

        ```powershell
        s1interop doctor .
        s1interop setup . --apply
        ```

        `setup` does not install software, change your project, or overwrite an existing `local.build.props`.

        ## Build, deploy, and run

        Use the configuration matching the install you want to test. The default and `beta` Steam branches are IL2CPP; `alternate` and `alternate-beta` are Mono.

        ```powershell
        dotnet build -c "Debug Il2Cpp"   # builds and copies the DLL into <Il2CppGamePath>\Mods
        dotnet run -c "Debug Il2Cpp"     # also starts that install
        dotnet build -c "Debug Mono"
        dotnet run -c "Debug Mono"
        ```

        Close the game before building; a running game locks the DLL in `Mods`. Release configurations build without deploying:

        ```text
        bin\Mono\Release Mono\netstandard2.1\{{projectName}}.dll
        bin\Il2Cpp\Release Il2Cpp\net6.0\{{projectName}}.dll
        ```

        After launch, the MelonLoader console shows `{{projectName}} loaded on Mono.` or `{{projectName}} loaded on Il2Cpp.`. Load a save and press F8 to log the NPC count.

        ## Writing code for both runtimes

        Game namespaces are `ScheduleOne.*` on Mono and `Il2CppScheduleOne.*` on IL2CPP. Instead of `#if` blocks around `using` directives, list the namespaces once in the `.csproj`:

        ```xml
        <S1InteropUsing Include="ScheduleOne.Employees" />
        ```

        The `S1Interop` namespace adds calls that compile the same way on both runtimes:

        ```csharp
        Employee? employee = npc.TryCast<Employee>();     // instead of `npc as Employee`
        var names = NPCManager.NPCRegistry.AsEnumerable().Select(n => n.FirstName);
        button.onClick.AddListener(() => LoggerInstance.Msg("Clicked"));
        ```

        Keep `#if MONO` / `#if IL2CPP` for the few places where the runtimes really differ.

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
