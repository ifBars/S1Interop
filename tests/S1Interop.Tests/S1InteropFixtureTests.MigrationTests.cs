internal sealed partial class S1InteropFixtureTests
{
    private void MsBuildOsPlatformConditionsAreEvaluated()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "OsConditionMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
                    <GameDir>C:\Schedule I</GameDir>
                  </PropertyGroup>
                  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Linux'))">
                    <GameDir>/home/user/Schedule I</GameDir>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="UnityEngine.CoreModule">
                      <HintPath>$(GameDir)\MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            string? hintPath = GetConfiguration(project, "Debug").References.Single().HintPath;

            if (OperatingSystem.IsWindows())
            {
                Assert(
                    hintPath?.StartsWith(@"C:\Schedule I\", StringComparison.OrdinalIgnoreCase) == true,
                    $"Windows OS condition should select the Windows GameDir, got {hintPath}.");
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert(
                    hintPath?.StartsWith("/home/user/Schedule I", StringComparison.OrdinalIgnoreCase) == true,
                    $"Linux OS condition should select the Linux GameDir, got {hintPath}.");
            }

            Assert(
                hintPath is null ||
                !hintPath.Contains(@"C:\home\user", StringComparison.OrdinalIgnoreCase),
                $"OS conditions should not combine Windows path semantics with the Linux GameDir, got {hintPath}.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void LocalPathDiagnosticsDetectAnyWindowsDriveLetter()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ArbitraryDrivePathMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <GameDir>Z:\Steam\steamapps\common\Schedule I</GameDir>
                    <ReleaseDir>Q:\code\UnityModding\Schedule_1_Modding\Hoverboard\BundleInfo\</ReleaseDir>
                  </PropertyGroup>
                </Project>
                """);

            ProjectAnalysis project = analyzer.Analyze(tempProject).Projects.Single();
            string[] evidence = project.Diagnostics
                .Where(diagnostic => diagnostic.RuleId == "local_path_in_project")
                .Select(diagnostic => diagnostic.Evidence ?? string.Empty)
                .ToArray();

            Assert(
                evidence.Contains(@"Z:\Steam\steamapps\common\Schedule I", StringComparer.OrdinalIgnoreCase),
                "Local path diagnostics should detect non-C/D Windows game roots.");
            Assert(
                evidence.Contains(@"Q:\code\UnityModding\Schedule_1_Modding\Hoverboard\BundleInfo\", StringComparer.OrdinalIgnoreCase),
                "Local path diagnostics should detect non-game absolute Windows paths too.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationPreservesLocalPropsUnderOsConditionedGameDir()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "OsConditionMigrationMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
                    <GameDir>C:\Schedule I</GameDir>
                  </PropertyGroup>
                  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Linux'))">
                    <GameDir>/home/user/Schedule I</GameDir>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="UnityEngine.CoreModule">
                      <HintPath>$(GameDir)\MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            WorkspaceAnalysis before = new WorkspaceAnalyzer().Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);

            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "local_path_in_project"),
                "OS-conditioned GameDir fixture should exercise local path migration.");
            Assert(File.Exists(Path.Combine(tempRoot, "local.build.props")), "local.build.props should be generated for OS-conditioned GameDir migration.");

            ProjectAnalysis after = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            string? hintPath = GetConfiguration(after, "Debug").References
                .Single(reference => reference.Include == "UnityEngine.CoreModule")
                .HintPath;
            if (OperatingSystem.IsWindows())
            {
                Assert(
                    hintPath?.StartsWith(@"C:\Schedule I\", StringComparison.OrdinalIgnoreCase) == true,
                    $"Migrated Windows OS-conditioned GameDir should still resolve through local.build.props, got {hintPath}.");
            }

            Assert(
                hintPath is null ||
                !hintPath.StartsWith(@"\MelonLoader", StringComparison.OrdinalIgnoreCase) &&
                !hintPath.StartsWith(@"C:\MelonLoader", StringComparison.OrdinalIgnoreCase),
                $"Migrated OS-conditioned GameDir should not be overwritten by an empty project property, got {hintPath}.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackScaffoldsLocalReferenceProperties()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "RootRelativeReferencesMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Debug'">
                    <GameDllPath>$(ManagedDllPath)</GameDllPath>
                    <MLPath>$(MelonLoaderNet6Path)</MLPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GameDllPath)\Assembly-CSharp.dll</HintPath>
                    </Reference>
                    <Reference Include="MelonLoader">
                      <HintPath>$(MLPath)\MelonLoader.dll</HintPath>
                    </Reference>
                    <Reference Include="S1API">
                      <HintPath>$(S1APIModsPath)\S1API.Mono.MelonLoader.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            WorkspaceAnalysis before = new WorkspaceAnalyzer().Analyze(tempProject);
            ProjectAnalysis beforeProject = before.Projects.Single();
            Assert(
                beforeProject.Diagnostics.Any(diagnostic => diagnostic.RuleId == "missing_local_reference_properties"),
                "Root-relative reference paths should report missing local reference properties before migration.");

            MigrationPlan plan = new MigrationPlanner().Plan(before);
            Assert(
                plan.Projects.Single().Operations.Any(operation => operation.RuleId == "missing_local_reference_properties"),
                "Migration plan should include local reference property scaffolding.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            string localPropsPath = Path.Combine(tempRoot, "local.build.props");
            string examplePropsPath = Path.Combine(tempRoot, "local.build.props.example");
            Assert(File.Exists(localPropsPath), "local.build.props was not created for root-relative reference paths.");
            Assert(File.Exists(examplePropsPath), "local.build.props.example was not created for root-relative reference paths.");
            Assert(CountProjectImports(tempProject, "local.build.props") == 1, "Project should import local.build.props exactly once.");

            string localProps = File.ReadAllText(localPropsPath);
            Assert(localProps.Contains("<ManagedDllPath>", StringComparison.Ordinal), "Scaffold should expose the terminal ManagedDllPath property.");
            Assert(localProps.Contains("<MelonLoaderNet6Path>", StringComparison.Ordinal), "Scaffold should expose the terminal MelonLoaderNet6Path property.");
            Assert(localProps.Contains("<S1APIModsPath>", StringComparison.Ordinal), "Scaffold should expose direct dependency path properties.");
            Assert(!localProps.Contains("<GameDllPath>", StringComparison.Ordinal), "Scaffold should avoid aliases that the project overwrites.");

            ProjectAnalysis afterProject = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            Assert(
                afterProject.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_local_reference_properties"),
                "Generated local reference property scaffolding should clear the scaffold diagnostic.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore the project file after scaffold migration.");
            Assert(rollbackResult.RemovedFiles.Contains(localPropsPath), "Rollback should remove generated local.build.props.");
            Assert(rollbackResult.RemovedFiles.Contains(examplePropsPath), "Rollback should remove generated local.build.props.example.");
            Assert(!File.Exists(localPropsPath), "Rollback did not remove local.build.props.");
            Assert(!File.Exists(examplePropsPath), "Rollback did not remove local.build.props.example.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationRepairsExistingLocalBuildPropsImport()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MissingLocalPropsImport.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net6.0</TargetFramework>
                    <Configurations>Il2cpp_Debug</Configurations>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Il2cpp_Debug'">
                    <DefineConstants>IL2CPP</DefineConstants>
                    <GamePath>$(Il2CppGamePath)</GamePath>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "local.build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <Il2CppGamePath>C:\Games\Schedule I_public</Il2CppGamePath>
                  </PropertyGroup>
                </Project>
                """);

            WorkspaceAnalysis before = new WorkspaceAnalyzer().Analyze(tempProject);
            Assert(
                before.Projects.Single().Diagnostics.Any(diagnostic => diagnostic.RuleId == "missing_local_build_props_import"),
                "Existing local.build.props without a project import should be diagnosed.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlanner().Plan(before));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "missing_local_build_props_import"),
                "Migration should repair a missing local.build.props import.");
            Assert(
                CountProjectImports(tempProject, "local.build.props") == 1,
                "Project should import local.build.props exactly once after repair.");

            WorkspaceAnalysis after = new WorkspaceAnalyzer().Analyze(tempProject);
            Assert(
                after.Projects.Single().Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_local_build_props_import"),
                "Repaired project should not retain the local.build.props import diagnostic.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationSupportsLegacyConfigurationPlatformConditions()
    {
        string projectPath = Path.Combine(WorkspaceRoot, @"MrsMingsAuthenticPets\Mrs_Mings_Authentic_Pets\Mrs_Mings_Authentic_Pets.csproj");
        ProjectAnalysis project = AnalyzeProject(@"MrsMingsAuthenticPets\Mrs_Mings_Authentic_Pets\Mrs_Mings_Authentic_Pets.csproj");

        AssertHasRuntime(project, "Debug", RuntimeKind.Il2Cpp);
        AssertHasRuntime(project, "Release", RuntimeKind.Il2Cpp);
        Assert(
            project.Configurations.All(configuration => !configuration.Name.Equals("=", StringComparison.Ordinal)),
            "Legacy Configuration|Platform conditions should not produce an '=' configuration name.");

        MigrationVerificationResult result = new MigrationVerifier().Verify(projectPath, new MigrationVerifierOptions(DualRuntime: true));

        Assert(result.Success, "Mrs Mings legacy Configuration|Platform project should pass sandboxed migration.");
        Assert(result.SandboxDeleted, "Mrs Mings verify-migration should delete its sandbox.");
        Assert(
            result.BeforeDiagnostics.Any(diagnostic =>
                diagnostic.RuleId == "wrong_target_framework" &&
                diagnostic.Configuration == "Debug"),
            "Mrs Mings should initially report a target-framework migration for Debug.");
        Assert(
            result.BeforeDiagnostics.Any(diagnostic =>
                diagnostic.RuleId == "wrong_target_framework" &&
                diagnostic.Configuration == "Release"),
            "Mrs Mings should initially report a target-framework migration for Release.");
        Assert(result.AfterDiagnostics.Count == 0, "Mrs Mings should have no residual diagnostics after sandboxed migration.");
    }

    private void BetterJukeboxReportsMissingRuntimeDefines()
    {
        ProjectAnalysis project = AnalyzeProject(@"BetterJukebox\BetterJukebox.csproj");

        AssertHasRuntime(project, "Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "IL2CPP", RuntimeKind.Il2Cpp);
        AssertHasDiagnostic(project, "missing_runtime_define", "Mono");
        AssertHasDiagnostic(project, "missing_runtime_define", "IL2CPP");
    }

    private void RuntimeDefineMigrationUsesDiagnosticDefineEvidenceForLegacyPlatformGroups()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "LegacyPlatformMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
                    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                  </PropertyGroup>
                  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                    <DefineConstants>DEBUG;TRACE</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">
                    <DefineConstants>DEBUG;TRACE;X64_ONLY</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                    </Reference>
                    <Reference Include="MelonLoader">
                      <HintPath>$(GamePath)\MelonLoader\net35\MelonLoader.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                  <ItemGroup>
                    <Compile Include="Core.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                #if MONO
                namespace LegacyPlatformMod;
                #endif
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectAnalysis beforeProject = before.Projects.Single();
            AssertHasDiagnostic(beforeProject, "missing_runtime_define", "Debug");
            InteropDiagnostic diagnostic = beforeProject.Diagnostics.First(diagnostic =>
                diagnostic.RuleId == "missing_runtime_define" &&
                diagnostic.Configuration == "Debug");
            Assert(
                diagnostic.Evidence?.Contains("DefineConstants=DEBUG;TRACE;X64_ONLY", StringComparison.Ordinal) == true,
                $"Expected diagnostic evidence to identify the x64 define list, got {diagnostic.Evidence ?? "<none>"}.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlanner().Plan(before));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "missing_runtime_define"),
                "Migration apply should add missing runtime defines for legacy platform groups.");

            XDocument document = XDocument.Load(tempProject);
            string anyCpuDefines = GetConditionedDefineConstants(document, "Debug|AnyCPU");
            string x64Defines = GetConditionedDefineConstants(document, "Debug|x64");
            Assert(
                string.Equals(anyCpuDefines, "DEBUG;TRACE;MONO", StringComparison.Ordinal),
                $"Missing runtime define should be applied to every platform group for the configuration, got {anyCpuDefines}.");
            Assert(
                string.Equals(x64Defines, "DEBUG;TRACE;X64_ONLY;MONO", StringComparison.Ordinal),
                $"Missing runtime define should be applied to the platform group matching diagnostic evidence, got {x64Defines}.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Legacy platform fixture should not retain missing runtime define diagnostics after migration.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeGeneratedUsingGuardsAddMonoDefinesForLegacyNames()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "AliasUsingMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
                    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                  </PropertyGroup>
                  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                    <DefineConstants>DEBUG;TRACE</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
                    <DefineConstants>TRACE</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                    </Reference>
                    <Reference Include="MelonLoader">
                      <HintPath>$(GamePath)\MelonLoader\net35\MelonLoader.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                  <ItemGroup>
                    <Compile Include="Core.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                using ConsoleUI = ScheduleOne.UI.ConsoleUI;

                namespace AliasUsingMod;

                public static class Core
                {
                    public static ConsoleUI? Current;
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Debug" &&
                    operation.Evidence?.Contains("MONO", StringComparison.Ordinal) == true),
                "Dual-runtime alias using migration should plan MONO for Debug even when the config name is not Mono.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Release" &&
                    operation.Evidence?.Contains("MONO", StringComparison.Ordinal) == true),
                "Dual-runtime alias using migration should plan MONO for Release even when the config name is not Mono.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Debug"),
                "Migration apply should add MONO to Debug for generated using guards.");
            Assert(
                applyResult.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Release"),
                "Migration apply should add MONO to Release for generated using guards.");

            XDocument document = XDocument.Load(tempProject);
            Assert(
                GetConditionedDefineConstants(document, "Debug|AnyCPU") == "DEBUG;TRACE;MONO",
                "Debug should define MONO after generated using guard migration.");
            Assert(
                GetConditionedDefineConstants(document, "Release|AnyCPU") == "TRACE;MONO",
                "Release should define MONO after generated using guard migration.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Generated using guard migration should not leave missing runtime define diagnostics.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationScaffoldsAnalyzerInferredDebugReleaseMonoProject()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MonoOnlyMod.csproj");
            string tempSolution = Path.Combine(tempRoot, "MonoOnlyMod.sln");
            const string projectGuid = "{11111111-2222-3333-4444-555555555555}";
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <Configurations>Debug;Release</Configurations>
                    <GamePath>$(MonoGamePath)</GamePath>
                    <DefineConstants>$(DefineConstants);MONO</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="MelonLoader">
                      <HintPath>$(GamePath)\MelonLoader\net35\MelonLoader.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="FishNet.Runtime">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\FishNet.Runtime.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="UnityEngine.CoreModule">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\UnityEngine.CoreModule.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                using ScheduleOne.PlayerScripts;

                namespace MonoOnlyMod;

                public static class Core
                {
                    public static Player? Current;
                }
                """);
            File.WriteAllText(
                tempSolution,
                $$"""
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MonoOnlyMod", "MonoOnlyMod.csproj", "{{projectGuid}}"
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Any CPU = Debug|Any CPU
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                        {{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                        {{projectGuid}}.Release|Any CPU.ActiveCfg = Release|Any CPU
                        {{projectGuid}}.Release|Any CPU.Build.0 = Release|Any CPU
                    EndGlobalSection
                EndGlobal
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectAnalysis beforeProject = before.Projects.Single();
            AssertHasRuntime(beforeProject, "Debug", RuntimeKind.Mono);
            AssertHasRuntime(beforeProject, "Release", RuntimeKind.Mono);

            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            MigrationOperation addIl2Cpp = projectPlan.Operations.Single(operation => operation.RuleId == "add_il2cpp_configuration");
            Assert(
                addIl2Cpp.Evidence?.Contains("mono_configurations=Debug;Release", StringComparison.Ordinal) == true,
                $"Dual-runtime plan should carry analyzer-inferred Mono config names, got {addIl2Cpp.Evidence ?? "<none>"}.");
            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "add_il2cpp_configuration"),
                "Migration apply should scaffold IL2CPP configs for analyzer-inferred Debug/Release Mono projects.");

            string projectText = File.ReadAllText(tempProject);
            string localPropsPath = Path.Combine(tempRoot, "local.build.props");
            string examplePropsPath = Path.Combine(tempRoot, "local.build.props.example");
            Assert(File.Exists(localPropsPath), "Dual-runtime migration should create local.build.props for runtime game paths.");
            Assert(File.Exists(examplePropsPath), "Dual-runtime migration should create local.build.props.example for runtime game paths.");
            string localPropsText = File.ReadAllText(localPropsPath);
            string examplePropsText = File.ReadAllText(examplePropsPath);
            Assert(localPropsText.Contains("<MonoGamePath>", StringComparison.Ordinal), "local.build.props should contain a MonoGamePath slot.");
            Assert(localPropsText.Contains("<Il2CppGamePath>", StringComparison.Ordinal), "local.build.props should contain an Il2CppGamePath slot.");
            Assert(examplePropsText.Contains("<MonoGamePath>", StringComparison.Ordinal), "local.build.props.example should show where MonoGamePath belongs.");
            Assert(examplePropsText.Contains("<Il2CppGamePath>", StringComparison.Ordinal), "local.build.props.example should show where Il2CppGamePath belongs.");
            Assert(projectText.Contains("<Configurations>Debug;Release;Il2cpp_Debug;Il2cpp_Release</Configurations>", StringComparison.Ordinal), "Project configurations should include inferred IL2CPP Debug/Release configs.");
            Assert(projectText.Contains("Condition=\"'$(Configuration)'=='Il2cpp_Debug'\"", StringComparison.Ordinal), "Project should include Il2cpp_Debug property group.");
            Assert(projectText.Contains("Condition=\"'$(Configuration)'=='Il2cpp_Release'\"", StringComparison.Ordinal), "Project should include Il2cpp_Release property group.");
            Assert(projectText.Contains("<TargetFramework>net6.0</TargetFramework>", StringComparison.Ordinal), "Generated IL2CPP configs should target net6.0.");
            Assert(projectText.Contains("<GamePath>$(Il2CppGamePath)</GamePath>", StringComparison.Ordinal), "Generated IL2CPP configs should use the IL2CPP game path property.");
            Assert(projectText.Contains("<S1Dir>$(GamePath)</S1Dir>", StringComparison.Ordinal), "Generated IL2CPP configs should preserve common S1Dir game-root aliases used by real mods.");
            Assert(projectText.Contains("<ManagedPath>$(GamePath)\\MelonLoader\\Il2CppAssemblies</ManagedPath>", StringComparison.Ordinal), "Generated IL2CPP configs should use generated wrapper references.");
            Assert(projectText.Contains("<S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>", StringComparison.Ordinal), "Mono configs should stamp the S1Interop generator runtime property.");
            Assert(projectText.Contains("<S1InteropTargetRuntime>Il2Cpp</S1InteropTargetRuntime>", StringComparison.Ordinal), "Generated IL2CPP configs should stamp the S1Interop generator runtime property.");
            Assert(projectText.Contains("Il2CppInterop.Runtime", StringComparison.Ordinal), "Generated IL2CPP configs should reference Il2CppInterop.Runtime.");
            Assert(projectText.Contains("<Reference Include=\"Il2CppScheduleOne.Core\">", StringComparison.Ordinal), "Generated IL2CPP configs should include split ScheduleOne core wrappers.");
            Assert(projectText.Contains("<Reference Include=\"Il2Cppcom.rlabrecque.steamworks.net\">", StringComparison.Ordinal), "Generated IL2CPP configs should reference the Steamworks.NET generated wrapper.");
            Assert(projectText.Contains("<Reference Include=\"UnityEngine.AudioModule\">", StringComparison.Ordinal), "Generated IL2CPP configs should include Unity audio module references for AudioClip/AudioSource APIs.");
            Assert(projectText.Contains("<Reference Include=\"UnityEngine.PhysicsModule\">", StringComparison.Ordinal), "Generated IL2CPP configs should include Unity physics module references for Collider APIs.");
            Assert(projectText.Contains("<Reference Include=\"UnityEngine.VehiclesModule\">", StringComparison.Ordinal), "Generated IL2CPP configs should include Unity vehicle module references for WheelCollider APIs.");
            Assert(projectText.Contains("<Reference Include=\"Il2CppFishNet.Runtime\">", StringComparison.Ordinal), "Generated IL2CPP configs should rewrite FishNet.Runtime references to Il2CppFishNet.Runtime.");
            Assert(projectText.Contains("<HintPath>$(GamePath)\\MelonLoader\\Il2CppAssemblies\\Il2CppFishNet.Runtime.dll</HintPath>", StringComparison.Ordinal), "Generated IL2CPP configs should rewrite FishNet.Runtime hint paths without double-prefixing Il2Cpp.");
            Assert(!projectText.Contains("Il2CppIl2CppFishNet.Runtime.dll", StringComparison.Ordinal), "Generated IL2CPP configs should not double-prefix FishNet.Runtime hint paths.");

            string solutionText = File.ReadAllText(tempSolution);
            Assert(solutionText.Contains("Il2cpp_Debug|Any CPU = Il2cpp_Debug|Any CPU", StringComparison.Ordinal), "Solution should expose Il2cpp_Debug for Visual Studio builds.");
            Assert(solutionText.Contains("Il2cpp_Release|Any CPU = Il2cpp_Release|Any CPU", StringComparison.Ordinal), "Solution should expose Il2cpp_Release for Visual Studio builds.");
            Assert(solutionText.Contains($"{projectGuid}.Il2cpp_Debug|Any CPU.Build.0 = Il2cpp_Debug|Any CPU", StringComparison.Ordinal), "Solution should build Il2cpp_Debug for the migrated project.");
            Assert(solutionText.Contains($"{projectGuid}.Il2cpp_Release|Any CPU.Build.0 = Il2cpp_Release|Any CPU", StringComparison.Ordinal), "Solution should build Il2cpp_Release for the migrated project.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            AssertHasRuntime(after.Projects.Single(), "Il2cpp_Debug", RuntimeKind.Il2Cpp);
            AssertHasRuntime(after.Projects.Single(), "Il2cpp_Release", RuntimeKind.Il2Cpp);

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore Debug/Release mono-only project file.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSolution), "Rollback should restore Debug/Release mono-only solution file.");
            Assert(!File.ReadAllText(tempProject).Contains("Il2cpp_Debug", StringComparison.Ordinal), "Rollback should remove scaffolded IL2CPP configs from project.");
            Assert(!File.ReadAllText(tempSolution).Contains("Il2cpp_Debug", StringComparison.Ordinal), "Rollback should remove scaffolded IL2CPP configs from solution.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationPreservesSpaceContainingMonoConfigurationNames()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "SpacedConfigMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Configurations>Debug Mono;Release Mono</Configurations>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug Mono'">
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <DefineConstants>MONO</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release Mono'">
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <DefineConstants>MONO;RELEASE</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(Configuration)' == 'Debug Mono' Or '$(Configuration)' == 'Release Mono'">
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectAnalysis beforeProject = before.Projects.Single();
            Assert(
                beforeProject.Configurations.Select(configuration => configuration.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).SequenceEqual(["Debug Mono", "Release Mono"]),
                "Fixture should start with only space-containing Mono configuration names.");

            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "add_il2cpp_configuration"),
                "Migration should scaffold IL2CPP configs for space-containing Mono config names.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            ProjectAnalysis afterProject = after.Projects.Single();
            string[] names = afterProject.Configurations.Select(configuration => configuration.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            Assert(
                names.SequenceEqual(["Debug Il2cpp", "Debug Mono", "Release Il2cpp", "Release Mono"]),
                $"Dual-runtime migration should preserve full configuration names without adding phantom Debug/Release configs. Names={string.Join(", ", names)}");
            AssertHasRuntime(afterProject, "Debug Mono", RuntimeKind.Mono);
            AssertHasRuntime(afterProject, "Release Mono", RuntimeKind.Mono);
            AssertHasRuntime(afterProject, "Debug Il2cpp", RuntimeKind.Il2Cpp);
            AssertHasRuntime(afterProject, "Release Il2cpp", RuntimeKind.Il2Cpp);

            string projectText = File.ReadAllText(tempProject);
            Assert(!projectText.Contains("<Configurations>Debug;Release", StringComparison.Ordinal), "Migration should not add whitespace-truncated Debug/Release configurations.");
            Assert(!projectText.Contains("Condition=\"'$(Configuration)'=='Debug'\"", StringComparison.Ordinal), "Migration should not add a phantom Debug property group.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore the spaced-config project file.");
            Assert(!File.ReadAllText(tempProject).Contains("Debug Il2cpp", StringComparison.Ordinal), "Rollback should remove generated IL2CPP configs.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationConditionsImportedMonoRuntimeFlag()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ImportedMonoPropsMod.csproj");
            string conditionsProps = Path.Combine(tempRoot, "build", "conditions.props");
            Directory.CreateDirectory(Path.GetDirectoryName(conditionsProps)!);
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <Configurations>Debug;Release</Configurations>
                    <DefineConstants>$(DefineConstants);MONO</DefineConstants>
                  </PropertyGroup>
                  <Import Project="build\conditions.props" />
                  <ItemGroup Condition="'$(IsMono)' == 'true'">
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                conditionsProps,
                """
                <Project>
                  <PropertyGroup>
                    <IsMono>true</IsMono>
                  </PropertyGroup>
                </Project>
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            string importedText = File.ReadAllText(conditionsProps);
            Assert(
                importedText.Contains("$(Configuration)", StringComparison.Ordinal) &&
                importedText.Contains("Il2cpp_Debug", StringComparison.Ordinal),
                "Dual-runtime migration should condition imported IsMono=true props on early configuration names.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            ProjectAnalysis project = after.Projects.Single();
            AssertHasRuntime(project, "Il2cpp_Debug", RuntimeKind.Il2Cpp);
            AssertHasRuntime(project, "Il2cpp_Release", RuntimeKind.Il2Cpp);

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(conditionsProps), "Rollback should restore imported conditions.props.");
            Assert(
                File.ReadAllText(conditionsProps).Contains("<IsMono>true</IsMono>", StringComparison.Ordinal),
                "Rollback should restore the original unconditioned IsMono value.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationInstallsGeneratorPackageWhenAttributesAreDeclared()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "GeneratorAwareMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <Configurations>Mono</Configurations>
                    <DefineConstants>MONO</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "GeneratorHints.cs"),
                """
                [assembly: S1Interop.S1InteropGenerateUnityEventBridge]
                [assembly: S1Interop.S1InteropGenerateDelegateEventBridge]

                namespace GeneratorAwareMod
                {
                    internal static class Core
                    {
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Projects declaring S1Interop bridge generator attributes should plan private generator package installation.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Projects declaring S1Interop bridge generator attributes should install the generator package during migration.");

            string projectText = File.ReadAllText(tempProject);
            Assert(
                projectText.Contains("<PackageReference Include=\"S1Interop.Generators\" Version=\"0.1.0-alpha.1\" PrivateAssets=\"all\" IncludeAssets=\"runtime; build; native; contentfiles; analyzers; buildtransitive\" />", StringComparison.Ordinal),
                "Generator-aware migration should install S1Interop.Generators as a private analyzer package.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore the generator-aware project file.");
            Assert(!File.ReadAllText(tempProject).Contains("S1Interop.Generators", StringComparison.Ordinal), "Rollback should remove the generator package reference.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationDoesNotReprefixExistingIl2CppNamedConfigurations()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MixedNamedConfigs.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <Configurations>MonoDebug;MonoRelease;Il2cppDebug;Il2cppRelease</Configurations>
                    <GamePath>$(MonoGamePath)</GamePath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="MelonLoader">
                      <HintPath>$(GamePath)\MelonLoader\net35\MelonLoader.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            ProjectAnalysis project = analyzer.Analyze(tempProject).Projects.Single();
            Assert(
                !DualRuntimeProjectScaffolder.NeedsIl2CppConfigurations(project),
                "Projects that already expose Il2cpp-named configurations should not be treated as Mono-only.");

            MigrationPlan plan = new MigrationPlanner().Plan(
                new WorkspaceAnalysis(tempProject, [project]),
                new MigrationPlannerOptions(DualRuntime: true));
            Assert(
                plan.Projects.Single().Operations.All(operation => operation.RuleId != "add_il2cpp_configuration"),
                "Dual-runtime migration should not create Il2cpp_Il2cpp... configurations for mixed named projects.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void S1FuelModInjectedTypesAreAnalyzed()
    {
        string projectPath = Path.Combine(WorkspaceRoot, @"S1FuelMod\S1FuelMod.csproj");
        SourceInteropAnalysis source = new SourceInteropAnalyzer().Analyze(projectPath);

        string[] expectedInjectedTypes =
        [
            "Equippable_GasolineCan",
            "FuelSignManager",
            "FuelSign",
            "FuelStation",
            "VehicleRefuelInteractable",
            "FuelVehicleData",
            "VehicleFuelSystem",
            "FuelTypeManager"
        ];

        Assert(
            expectedInjectedTypes.All(expected => source.InjectedTypes.Any(actual => actual.Name == expected)),
            "S1FuelMod source analysis should detect all expected injected types.");
        Assert(
            source.InjectedTypes.Count == expectedInjectedTypes.Length,
            $"S1FuelMod should have exactly {expectedInjectedTypes.Length} injected types in this fixture.");
        Assert(
            source.InjectedTypes.All(type => type.HasIntPtrConstructor),
            "Every S1FuelMod injected type should expose the required IntPtr constructor.");
        Assert(
            source.InjectedTypes.Where(type => type.HasDerivedConstructorPointer).All(type => type.HasDerivedConstructorBody),
            "ClassInjector constructor pointers should be paired with DerivedConstructorBody(this).");
        Assert(
            source.Il2CppGuardEvidence.Any(evidence => evidence.Contains("#if !MONO", StringComparison.Ordinal)),
            "S1FuelMod should be recognized as using #if !MONO for IL2CPP branches.");
        Assert(
            source.BridgeEvidence.Any(evidence => evidence.Contains("Il2CppSystem.Collections.Generic.List", StringComparison.Ordinal)),
            "S1FuelMod should expose compliant Il2CppSystem.Collections.Generic.List bridge evidence.");
        Assert(
            source.BridgeEvidence.Any(evidence => evidence.Contains("Il2CppStructArray", StringComparison.Ordinal)),
            "S1FuelMod should expose compliant Il2CppStructArray bridge evidence.");

        InteropDiagnostic[] hideDiagnostics = source.Diagnostics
            .Where(diagnostic => diagnostic.RuleId == "injected_member_requires_hidefromil2cpp")
            .ToArray();
        Assert(hideDiagnostics.Length == 1, "S1FuelMod should currently report one unhidden managed-surface diagnostic.");
        string hideEvidence = hideDiagnostics[0].Evidence ?? string.Empty;
        Assert(
            hideEvidence.Contains("FuelVehicleData.FromVehicleData", StringComparison.Ordinal) &&
            hideEvidence.Contains("FuelData", StringComparison.Ordinal),
            "The unhidden managed-surface diagnostic should point at FuelVehicleData.FromVehicleData(... FuelData ...).");

        ProjectAnalysis project = AnalyzeProject(@"S1FuelMod\S1FuelMod.csproj");
        Assert(
            project.Configurations.Select(configuration => configuration.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).SequenceEqual(
                ["Debug IL2CPP", "Debug Mono", "Release IL2CPP", "Release Mono"],
                StringComparer.OrdinalIgnoreCase),
            "S1FuelMod analysis should preserve configuration names that contain spaces.");
        AssertHasRuntime(project, "Debug Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "Release Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "Debug IL2CPP", RuntimeKind.Il2Cpp);
        AssertHasRuntime(project, "Release IL2CPP", RuntimeKind.Il2Cpp);
        AssertHasTargetFramework(project, "Debug Mono", "netstandard2.1");
        AssertHasTargetFramework(project, "Release Mono", "netstandard2.1");
        AssertHasTargetFramework(project, "Debug IL2CPP", "net6");
        AssertHasTargetFramework(project, "Release IL2CPP", "net6");
        ConfigurationAnalysis debugMono = GetConfiguration(project, "Debug Mono");
        ConfigurationAnalysis debugIl2Cpp = GetConfiguration(project, "Debug IL2CPP");
        Assert(
            debugMono.References.Any(reference =>
                reference.Imported &&
                (reference.SourcePath ?? string.Empty).EndsWith(@"build\references\MelonMono.targets", StringComparison.OrdinalIgnoreCase) &&
                (reference.HintPath ?? string.Empty).Contains(@"Schedule I_Data\Managed", StringComparison.OrdinalIgnoreCase)),
            "S1FuelMod Mono analysis should include imported managed references from MelonMono.targets.");
        Assert(
            debugIl2Cpp.References.Any(reference =>
                reference.Imported &&
                reference.Include.Equals("Il2CppInterop.Runtime", StringComparison.OrdinalIgnoreCase) &&
                (reference.HintPath ?? string.Empty).Contains(@"MelonLoader\net6\Il2CppInterop.Runtime.dll", StringComparison.OrdinalIgnoreCase)),
            "S1FuelMod IL2CPP analysis should include imported Il2CppInterop.Runtime from MelonIL2CPP.targets.");
        Assert(
            debugIl2Cpp.References.Any(reference =>
                reference.Imported &&
                (reference.HintPath ?? string.Empty).Contains(@"MelonLoader\Il2CppAssemblies", StringComparison.OrdinalIgnoreCase)),
            "S1FuelMod IL2CPP analysis should include generated-wrapper references from MelonIL2CPP.targets.");
        Assert(
            debugIl2Cpp.References.All(reference =>
                !(reference.HintPath ?? string.Empty).Contains(@"Schedule I_Data\Managed", StringComparison.OrdinalIgnoreCase)),
            "S1FuelMod IL2CPP imported references should not point at the Mono Managed folder.");
        Assert(
            project.Diagnostics.Any(diagnostic => diagnostic.RuleId == "injected_member_requires_hidefromil2cpp"),
            "Project analysis should include injected member HideFromIl2Cpp diagnostics.");
        Assert(
            project.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
            "S1FuelMod already defines MONO for Mono configurations and should not report missing runtime defines.");
        Assert(
            project.Diagnostics.All(diagnostic => diagnostic.RuleId != "wrong_target_framework"),
            "S1FuelMod imported build conditions should provide valid Mono and IL2CPP target frameworks.");
    }

    private void ExplicitIl2CppConfigurationNameWinsOverSharedMonoReferences()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MixedEvidenceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Configurations>Mono;Debug IL2CPP</Configurations>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="RefGen.Schedule-I.Mono" Version="0.4.5-f1" />
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GamePath)\Schedule I_Data\Managed\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Mono'">
                    <DefineConstants>$(DefineConstants);MONO</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug IL2CPP'">
                    <DefineConstants>$(DefineConstants);RELEASE</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();

            AssertHasRuntime(project, "Mono", RuntimeKind.Mono);
            AssertHasRuntime(project, "Debug IL2CPP", RuntimeKind.Il2Cpp);
            AssertHasDiagnostic(project, "wrong_target_framework", "Debug IL2CPP");
            AssertHasDiagnostic(project, "wrong_il2cpp_reference_surface", "Debug IL2CPP");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationTargetFrameworkOverrideWinsAfterImportedProps()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ImportedFrameworkMod.csproj");
            string importedProps = Path.Combine(tempRoot, "build.conditions.props");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Configurations>Debug IL2CPP</Configurations>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug IL2CPP'">
                    <DefineConstants>$(DefineConstants);IL2CPP</DefineConstants>
                  </PropertyGroup>
                  <Import Project="build.conditions.props" />
                </Project>
                """);
            File.WriteAllText(
                importedProps,
                """
                <Project>
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            WorkspaceAnalysis before = new WorkspaceAnalyzer().Analyze(tempProject);
            Assert(
                before.Diagnostics.Any(diagnostic =>
                    diagnostic.RuleId == "wrong_target_framework" &&
                    diagnostic.Configuration == "Debug IL2CPP"),
                "Imported target framework fixture should start with an IL2CPP framework diagnostic.");

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false));

            Assert(result.Success, "Migration should add a root project override that wins after imported TargetFramework props.");
            Assert(
                result.AfterDiagnostics.All(diagnostic => diagnostic.RuleId != "wrong_target_framework"),
                "Imported target framework fixture should have no target-framework diagnostic after migration.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackAddsHideFromIl2CppOnS1FuelModFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, "S1FuelMod");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "S1FuelMod.csproj");
            string tempFuelVehicleData = Path.Combine(tempRoot, "Systems", "FuelVehicleData.cs");
            string tempVehicleFuelSystem = Path.Combine(tempRoot, "Systems", "VehicleFuelSystem.cs");
            string originalSource = File.ReadAllText(tempFuelVehicleData);
            string originalVehicleFuelSystem = File.ReadAllText(tempVehicleFuelSystem);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            MigrationOperation hideOperation = projectPlan.Operations.Single(operation =>
                operation.RuleId == "injected_member_requires_hidefromil2cpp");
            Assert(
                projectPlan.Operations.All(operation => !operation.FilePath.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)),
                "S1FuelMod migration should not plan automatic edits against imported build targets.");
            Assert(
                projectPlan.Operations.All(operation => operation.RuleId != "wrong_target_framework" && operation.RuleId != "missing_il2cppinterop_reference"),
                "S1FuelMod imported build targets should prevent redundant target-framework and Il2CppInterop migration operations.");

            Assert(
                string.Equals(hideOperation.FilePath, tempFuelVehicleData, StringComparison.OrdinalIgnoreCase),
                "HideFromIl2Cpp migration should target the copied FuelVehicleData source file.");
            Assert(
                hideOperation.Evidence?.Contains("FuelVehicleData.FromVehicleData", StringComparison.Ordinal) == true,
                "HideFromIl2Cpp migration should retain evidence for the target member.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "injected_member_requires_hidefromil2cpp"),
                "Migration apply did not apply the HideFromIl2Cpp source operation.");
            MigrationFileChange fuelVehicleBackup = applyResult.FileChanges.Single(change =>
                string.Equals(change.FilePath, tempFuelVehicleData, StringComparison.OrdinalIgnoreCase));
            Assert(
                !fuelVehicleBackup.BackupPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
                "Rollback backups for C# source files should not keep the .cs extension, or SDK-style compile globs can compile them.");

            string migratedSource = File.ReadAllText(tempFuelVehicleData);
            int attributeIndex = migratedSource.IndexOf("[Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]", StringComparison.Ordinal);
            int methodIndex = migratedSource.IndexOf("public static FuelVehicleData FromVehicleData", StringComparison.Ordinal);
            Assert(attributeIndex >= 0, "Migrated source should contain the fully qualified HideFromIl2Cpp attribute.");
            Assert(methodIndex > attributeIndex, "Migrated HideFromIl2Cpp attribute should be inserted before FromVehicleData.");
            Assert(
                CountOccurrences(migratedSource, "HideFromIl2Cpp") == CountOccurrences(originalSource, "HideFromIl2Cpp") + 1,
                "Migration should add exactly one HideFromIl2Cpp attribute to FuelVehicleData.");
            string migratedVehicleFuelSystem = File.ReadAllText(tempVehicleFuelSystem);
            Assert(
                migratedVehicleFuelSystem.Contains("S1Interop.Generated.S1InteropMemberRegistry.GetvehicleName(_landVehicle)?.ToString()", StringComparison.Ordinal) &&
                !migratedVehicleFuelSystem.Contains("ReflectionUtils.TryGetFieldOrProperty(_landVehicle, \"vehicleName\")", StringComparison.Ordinal),
                "S1FuelMod migration should rewrite typed ReflectionUtils.TryGetFieldOrProperty call sites through generated member-cache helpers.");
            string generatedMemberTargets = File.ReadAllText(Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.MemberAccessTargets.g.cs"));
            Assert(
                generatedMemberTargets.Contains("[assembly: S1Interop.S1InteropMember(\"LandVehicle\", \"vehicleName\", Alias = \"vehicleName\")]", StringComparison.Ordinal),
                "S1FuelMod migration should keep generated member declarations for field-backed helper calls after source rewrites.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(after.Diagnostics.Count == 0, "Copied S1FuelMod fixture should have no diagnostics after migration apply.");
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "injected_member_requires_hidefromil2cpp"),
                "Copied S1FuelMod fixture should not retain HideFromIl2Cpp diagnostics after migration apply.");
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "wrong_target_framework"),
                "Copied S1FuelMod fixture should not retain target framework diagnostics after migration apply.");
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Copied S1FuelMod fixture should not retain missing runtime define diagnostics after migration apply.");
            ProjectAnalysis migratedProject = after.Projects.Single();
            AssertHasTargetFramework(migratedProject, "Debug IL2CPP", "net6");
            AssertHasTargetFramework(migratedProject, "Release IL2CPP", "net6");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempFuelVehicleData), "Rollback did not restore FuelVehicleData.cs.");
            Assert(rollbackResult.RestoredFiles.Contains(tempVehicleFuelSystem), "Rollback did not restore VehicleFuelSystem.cs.");
            Assert(
                string.Equals(File.ReadAllText(tempFuelVehicleData), originalSource, StringComparison.Ordinal),
                "Rollback should restore the original FuelVehicleData source.");
            Assert(
                string.Equals(File.ReadAllText(tempVehicleFuelSystem), originalVehicleFuelSystem, StringComparison.Ordinal),
                "Rollback should restore the original VehicleFuelSystem source.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyConditionalizesS1FuelModGameConstructor()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "FuelVehicleDataInterop.csproj");
            string tempSource = Path.Combine(tempRoot, "FuelVehicleData.cs");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                tempSource,
                """
                using System;
                using System.Collections.Generic;
                using UnityEngine;

                namespace S1FuelMod.Systems;

                public class FuelVehicleData : ScheduleOne.Persistence.Datas.VehicleData
                {
                    public FuelVehicleData(Guid guid, string code, Vector3 pos, Quaternion rot, EVehicleColor col, ItemSet vehicleContents, List<SpraySurfaceData> spraySurfaces, FuelData fuelData)
                        : base(guid, code, pos, rot, col, vehicleContents, spraySurfaces)
                    {
                    }

                    public static FuelVehicleData FromVehicleData(VehicleData vehicleData, FuelData fuelData)
                    {
                        var spraySurfaces = vehicleData.SpraySurfaces ?? new List<SpraySurfaceData>();
                        return new FuelVehicleData(
                            new Guid(vehicleData.GUID),
                            vehicleData.VehicleCode,
                            vehicleData.Position,
                            vehicleData.Rotation,
                            Enum.Parse<EVehicleColor>(vehicleData.Color),
                            vehicleData.VehicleContents,
                            spraySurfaces,
                            fuelData);
                    }
                }

                public class CustomGamePayload : ScheduleOne.Persistence.Datas.GameData
                {
                    public CustomGamePayload(Guid guid, List<SpraySurfaceData> spraySurfaces)
                        : base(guid, spraySurfaces)
                    {
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "game_constructor_requires_il2cpp_signature"),
                "Mono-only game constructor fixture should plan an IL2CPP signature migration.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlan(tempProject, [projectPlan]));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "game_constructor_requires_il2cpp_signature"),
                "Migration should apply the game constructor signature repair.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("public FuelVehicleData(Il2CppSystem.Guid guid", StringComparison.Ordinal) &&
                migratedSource.Contains("Il2CppSystem.Collections.Generic.List<SpraySurfaceData> spraySurfaces", StringComparison.Ordinal),
                "Migrated constructor should use IL2CPP Guid and List wrapper types under IL2CPP.");
            Assert(
                migratedSource.Contains("public CustomGamePayload(Il2CppSystem.Guid guid, Il2CppSystem.Collections.Generic.List<SpraySurfaceData> spraySurfaces)", StringComparison.Ordinal),
                "Migrated non-VehicleData game constructor should also use IL2CPP Guid and List wrapper types under IL2CPP.");
            Assert(
                migratedSource.Contains("new Il2CppSystem.Collections.Generic.List<SpraySurfaceData>()", StringComparison.Ordinal),
                "Migrated factory helper should use an IL2CPP list fallback under IL2CPP.");
            Assert(
                migratedSource.Contains("new Il2CppSystem.Guid(vehicleData.GUID)", StringComparison.Ordinal),
                "Migrated factory helper should construct Il2CppSystem.Guid under IL2CPP.");
            Assert(
                CountOccurrences(migratedSource, "#if IL2CPP") >= 3,
                "Migrated source should guard the constructor and helper conversions with IL2CPP branches.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "game_constructor_requires_il2cpp_signature"),
                "Migrated game constructor fixture should not retain the constructor signature diagnostic.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void BackendNeutralRegistryCompilesRealS1FuelModFacadeTargets()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "S1FuelMod");
        if (!Directory.Exists(sourceDirectory))
        {
            Console.WriteLine("Skipping S1FuelMod backend-neutral registry fixture because S1FuelMod is not available.");
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "S1FuelMod.csproj");
            WorkspaceAnalysis workspace = analyzer.Analyze(tempProject);
            SdkFacadePlan facadePlan = new SdkFacadeGenerator().Plan(workspace.Projects.Single());
            Assert(facadePlan.TypeAliases.Count > 0, "Copied S1FuelMod should expose real ScheduleOne facade aliases for backend-neutral registry generation.");

            string source = BuildBackendNeutralRegistrySource(facadePlan.TypeAliases.Take(12));
            string generated = RunTypeRegistryGenerator(source);

            Assert(
                generated.Contains("public static S1InteropRuntimeBackend Backend", StringComparison.Ordinal) &&
                generated.Contains("cachedBackend is null || cachedBackend == S1InteropRuntimeBackend.Unknown", StringComparison.Ordinal),
                "Copied S1FuelMod facade targets should compile through backend-neutral runtime detection.");
            Assert(
                facadePlan.TypeAliases.Take(12).All(alias =>
                    generated.Contains($"public const string {alias.Alias}MonoName = \"{alias.MonoType}\";", StringComparison.Ordinal) &&
                    generated.Contains($"public const string {alias.Alias}Il2CppName = \"{alias.Il2CppType}\";", StringComparison.Ordinal)),
                $"Backend-neutral registry should preserve both Mono and IL2CPP names for copied S1FuelMod aliases. Generated source:{Environment.NewLine}{generated}");
            Assert(
                facadePlan.TypeAliases.Take(12).All(alias =>
                    generated.Contains($"public static object? Create{alias.Alias}(params object?[] args) => Create({alias.Alias}Name, args);", StringComparison.Ordinal) &&
                    generated.Contains($"public static object? Get{alias.Alias}Static(string memberName) => S1InteropMemberRegistry.GetValue({alias.Alias}Name, memberName, null);", StringComparison.Ordinal)),
                $"Backend-neutral registry should emit object-based facade helpers for copied S1FuelMod aliases. Generated source:{Environment.NewLine}{generated}");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void BackendNeutralScaffoldBuildsRealS1FuelModFacadeTargetsAgainstBothReferenceSurfaces()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "S1FuelMod");
        string monoGamePath = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
        string il2CppGamePath = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
        string monoMelonLoader = Path.Combine(monoGamePath, "MelonLoader", "net35", "MelonLoader.dll");
        string il2CppMelonLoader = Path.Combine(il2CppGamePath, "MelonLoader", "net6", "MelonLoader.dll");
        if (!Directory.Exists(sourceDirectory) || !File.Exists(monoMelonLoader) || !File.Exists(il2CppMelonLoader))
        {
            Console.WriteLine("Skipping S1FuelMod backend-neutral scaffold build fixture because S1FuelMod or local game roots are not available.");
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceCopy = Path.Combine(tempRoot, "S1FuelModSource");
            Directory.CreateDirectory(sourceCopy);
            CopyFixtureDirectory(sourceDirectory, sourceCopy);
            string copiedProject = Path.Combine(sourceCopy, "S1FuelMod.csproj");
            WorkspaceAnalysis workspace = analyzer.Analyze(copiedProject);
            SdkFacadePlan facadePlan = new SdkFacadeGenerator().Plan(workspace.Projects.Single());
            MemberAccessTarget[] memberTargets = new MemberAccessTargetCatalog()
                .Discover(copiedProject)
                .Where(target =>
                    target.OwnerAlias.Equals("LandVehicle", StringComparison.Ordinal) &&
                    target.MemberName.Equals("vehicleName", StringComparison.Ordinal))
                .ToArray();
            SdkTypeAlias[] aliases = facadePlan.TypeAliases
                .Take(16)
                .Concat(memberTargets.Select(target => new SdkTypeAlias(target.OwnerAlias, target.OwnerTypeName, $"Il2Cpp{target.OwnerTypeName}", GenerateGlobalUsing: false)))
                .GroupBy(alias => alias.Alias, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            Assert(aliases.Length > 0, "Copied S1FuelMod should expose facade aliases for fresh backend-neutral scaffold validation.");
            Assert(
                memberTargets.Length > 0,
                "Copied S1FuelMod should expose LandVehicle.vehicleName as a generated member target from ReflectionUtils.TryGetFieldOrProperty usage.");
            Assert(
                memberTargets.All(target => target.OwnerTypeName.Equals("ScheduleOne.Vehicles.LandVehicle", StringComparison.Ordinal)),
                "Copied S1FuelMod helper-call discovery should resolve LandVehicle through the ScheduleOne.Vehicles namespace instead of leaving a simple runtime type name.");
            S1InteropMemberDeclaration[] memberDeclarations = memberTargets
                .Select(target => new S1InteropMemberDeclaration(
                    target.OwnerAlias,
                    target.MemberName,
                    "S1FuelVehicleName",
                    $"S1Interop.S1InteropMemberKind.{target.Kind}",
                    target.IsStatic))
                .ToArray();

            string scaffoldDirectory = Path.Combine(tempRoot, "FreshS1FuelFacadeMod");
            ProcessResult create = RunCli("new", scaffoldDirectory, "--apply");
            Assert(create.ExitCode == 0, $"s1interop new should create the backend-neutral scaffold for real facade alias validation. Output: {create.Output}");

            string scaffoldProject = Path.Combine(scaffoldDirectory, "FreshS1FuelFacadeMod.csproj");
            string starterPath = Path.Combine(scaffoldDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
            File.WriteAllText(starterPath, BuildBackendNeutralRegistrySource(aliases, memberDeclarations));
            string packageSource = CreateLocalGeneratorPackageSource(tempRoot);
            string packageCache = Path.Combine(tempRoot, "NuGetPackages");

            ProcessResult monoBuild = RunDotNet(
                "build",
                scaffoldProject,
                "--nologo",
                "-v:minimal",
                $"-p:MonoGamePath={monoGamePath}",
                $"-p:RestorePackagesPath={packageCache}",
                $"-p:RestoreAdditionalProjectSources={packageSource}");
            Assert(monoBuild.ExitCode == 0, $"Fresh backend-neutral scaffold with real S1FuelMod aliases should build against Mono references. Output: {monoBuild.Output}");

            ProcessResult il2CppBuild = RunDotNet(
                "build",
                scaffoldProject,
                "--nologo",
                "-v:minimal",
                "-p:S1InteropReferenceRuntime=Il2Cpp",
                $"-p:Il2CppGamePath={il2CppGamePath}",
                $"-p:RestorePackagesPath={packageCache}",
                $"-p:RestoreAdditionalProjectSources={packageSource}");
            Assert(il2CppBuild.ExitCode == 0, $"Fresh backend-neutral scaffold with real S1FuelMod aliases should build against IL2CPP references without source changes. Output: {il2CppBuild.Output}");

            string starterSource = File.ReadAllText(starterPath);
            Assert(
                aliases.All(alias =>
                    starterSource.Contains($"[assembly: S1Interop.S1InteropType(\"{EscapeCSharpString(alias.MonoType)}\", Alias = \"{EscapeCSharpString(alias.Alias)}\", Il2CppTypeName = \"{EscapeCSharpString(alias.Il2CppType)}\")]", StringComparison.Ordinal)),
                "Fresh backend-neutral scaffold should use real copied S1FuelMod aliases as assembly-level runtime-resolved declarations.");
            Assert(
                starterSource.Contains("[assembly: S1Interop.S1InteropMember(\"LandVehicle\", \"vehicleName\", Alias = \"S1FuelVehicleName\")]", StringComparison.Ordinal) &&
                starterSource.Contains("S1Interop.Generated.S1InteropMemberRegistry.GetS1FuelVehicleName(vehicle)", StringComparison.Ordinal) &&
                starterSource.Contains("S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue(vehicle, \"vehicleName\")", StringComparison.Ordinal),
                "Fresh backend-neutral scaffold should compile declared and dynamic generated member-cache helpers from real copied S1FuelMod reflection usage.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void SdkFacadeGeneratorDetectsBarsGraphicsBackendAliasPairs()
    {
        string projectPath = Path.Combine(WorkspaceRoot, "BarsGraphics", "BarsGraphics.csproj");
        if (!File.Exists(projectPath))
        {
            Console.WriteLine("Skipping BarsGraphics SDK facade alias fixture because BarsGraphics is not available.");
            return;
        }

        ProjectAnalysis project = analyzer.Analyze(projectPath).Projects.Single();
        var generator = new SdkFacadeGenerator();
        SdkFacadePlan facadePlan = generator.Plan(project);
        string source = generator.GenerateSource(facadePlan);

        Assert(
            facadePlan.TypeAliases.Any(alias =>
                alias.Alias == "GameHud" &&
                alias.MonoType == "ScheduleOne.UI.HUD" &&
                alias.Il2CppType == "Il2CppScheduleOne.UI.HUD" &&
                !alias.GenerateGlobalUsing),
            "BarsGraphics should expose GameHud as a backend-neutral registry alias pair without duplicating its local alias.");
        Assert(
            facadePlan.TypeAliases.Any(alias =>
                alias.Alias == "GamePlayerCamera" &&
                alias.MonoType == "ScheduleOne.PlayerScripts.PlayerCamera" &&
                alias.Il2CppType == "Il2CppScheduleOne.PlayerScripts.PlayerCamera" &&
                !alias.GenerateGlobalUsing),
            "BarsGraphics should expose GamePlayerCamera as a backend-neutral registry alias pair without duplicating its local alias.");
        Assert(
            !source.Contains("global using GameHud =", StringComparison.Ordinal) &&
            !source.Contains("global using GamePlayerCamera =", StringComparison.Ordinal),
            "BarsGraphics generated facade should not duplicate aliases already declared by source files.");
        Assert(
            source.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.UI.HUD\", Alias = \"GameHud\", Il2CppTypeName = \"Il2CppScheduleOne.UI.HUD\")]", StringComparison.Ordinal) &&
            source.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.PlayerScripts.PlayerCamera\", Alias = \"GamePlayerCamera\", Il2CppTypeName = \"Il2CppScheduleOne.PlayerScripts.PlayerCamera\")]", StringComparison.Ordinal),
            "BarsGraphics aliases should feed generated backend-neutral registry attributes.");
    }

    private void BackendNeutralScaffoldBuildsRealBarsGraphicsFacadeTargetsAgainstBothReferenceSurfaces()
    {
        string projectPath = Path.Combine(WorkspaceRoot, "BarsGraphics", "BarsGraphics.csproj");
        string monoGamePath = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
        string il2CppGamePath = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
        string monoMelonLoader = Path.Combine(monoGamePath, "MelonLoader", "net35", "MelonLoader.dll");
        string il2CppMelonLoader = Path.Combine(il2CppGamePath, "MelonLoader", "net6", "MelonLoader.dll");
        if (!File.Exists(projectPath) || !File.Exists(monoMelonLoader) || !File.Exists(il2CppMelonLoader))
        {
            Console.WriteLine("Skipping BarsGraphics backend-neutral scaffold build fixture because BarsGraphics or local game roots are not available.");
            return;
        }

        ProjectAnalysis project = analyzer.Analyze(projectPath).Projects.Single();
        SdkFacadePlan facadePlan = new SdkFacadeGenerator().Plan(project);
        SdkTypeAlias[] aliases = facadePlan.TypeAliases
            .Where(alias =>
                alias.Alias.Equals("GameHud", StringComparison.Ordinal) ||
                alias.Alias.Equals("GamePlayerCamera", StringComparison.Ordinal))
            .OrderBy(alias => alias.Alias, StringComparer.Ordinal)
            .ToArray();
        Assert(aliases.Length == 2, "BarsGraphics should expose GameHud and GamePlayerCamera aliases for backend-neutral scaffold validation.");

        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string scaffoldDirectory = Path.Combine(tempRoot, "FreshBarsGraphicsFacadeMod");
            ProcessResult create = RunCli("new", scaffoldDirectory, "--apply");
            Assert(create.ExitCode == 0, $"s1interop new should create the backend-neutral scaffold for BarsGraphics alias validation. Output: {create.Output}");

            string scaffoldProject = Path.Combine(scaffoldDirectory, "FreshBarsGraphicsFacadeMod.csproj");
            string starterPath = Path.Combine(scaffoldDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
            File.WriteAllText(starterPath, BuildBackendNeutralRegistrySource(aliases));
            string packageSource = CreateLocalGeneratorPackageSource(tempRoot);
            string packageCache = Path.Combine(tempRoot, "NuGetPackages");

            ProcessResult monoBuild = RunDotNet(
                "build",
                scaffoldProject,
                "--nologo",
                "-v:minimal",
                $"-p:MonoGamePath={monoGamePath}",
                $"-p:RestorePackagesPath={packageCache}",
                $"-p:RestoreAdditionalProjectSources={packageSource}");
            Assert(monoBuild.ExitCode == 0, $"Fresh backend-neutral scaffold with real BarsGraphics aliases should build against Mono references. Output: {monoBuild.Output}");

            ProcessResult il2CppBuild = RunDotNet(
                "build",
                scaffoldProject,
                "--nologo",
                "-v:minimal",
                "-p:S1InteropReferenceRuntime=Il2Cpp",
                $"-p:Il2CppGamePath={il2CppGamePath}",
                $"-p:RestorePackagesPath={packageCache}",
                $"-p:RestoreAdditionalProjectSources={packageSource}");
            Assert(il2CppBuild.ExitCode == 0, $"Fresh backend-neutral scaffold with real BarsGraphics aliases should build against IL2CPP references without source changes. Output: {il2CppBuild.Output}");

            string starterSource = File.ReadAllText(starterPath);
            Assert(
                starterSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.UI.HUD\", Alias = \"GameHud\", Il2CppTypeName = \"Il2CppScheduleOne.UI.HUD\")]", StringComparison.Ordinal) &&
                starterSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.PlayerScripts.PlayerCamera\", Alias = \"GamePlayerCamera\", Il2CppTypeName = \"Il2CppScheduleOne.PlayerScripts.PlayerCamera\")]", StringComparison.Ordinal) &&
                starterSource.Contains("S1Interop.Generated.S1InteropTypeRegistry.GetGameHud(instance, memberName)", StringComparison.Ordinal),
                "Fresh backend-neutral scaffold should compile generated helpers from real BarsGraphics aliases.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationSucceedsOnS1FuelModWithoutMutatingSource()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "S1FuelMod");
        string sourceProject = Path.Combine(sourceDirectory, "S1FuelMod.csproj");
        string sourceFuelVehicleData = Path.Combine(sourceDirectory, "Systems", "FuelVehicleData.cs");
        string originalProjectHash = ComputeSha256(sourceProject);
        string originalFuelVehicleDataHash = ComputeSha256(sourceFuelVehicleData);
        string generatedFacadeDirectory = Path.Combine(sourceDirectory, "S1Interop.Generated");
        string runsDirectory = Path.Combine(sourceDirectory, "s1interop-runs");
        string localProps = Path.Combine(sourceDirectory, "local.build.props");
        bool hadGeneratedFacadeDirectory = Directory.Exists(generatedFacadeDirectory);
        bool hadRunsDirectory = Directory.Exists(runsDirectory);
        bool hadLocalProps = File.Exists(localProps);

        MigrationVerificationResult result = new MigrationVerifier().Verify(sourceProject);

        Assert(result.Success, "S1FuelMod verify-migration should pass after sandboxed migration apply.");
        Assert(result.PlannedOperations >= 2, "S1FuelMod verify-migration should plan the HideFromIl2Cpp and SDK facade operations.");
        Assert(result.AppliedOperations >= 2, "S1FuelMod verify-migration should apply the planned automatic operations in the sandbox.");
        Assert(result.AfterDiagnostics.Count == 0, "S1FuelMod verify-migration should leave no residual diagnostics.");
        Assert(result.SandboxDeleted, "S1FuelMod verify-migration should delete its sandbox.");
        Assert(
            !Directory.Exists(Path.GetDirectoryName(result.SandboxProjectPath)!),
            "S1FuelMod verify-migration should remove the sandbox project directory.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "S1FuelMod verify-migration should not mutate the real project file.");
        Assert(
            string.Equals(ComputeSha256(sourceFuelVehicleData), originalFuelVehicleDataHash, StringComparison.Ordinal),
            "S1FuelMod verify-migration should not mutate real source files.");
        Assert(
            Directory.Exists(generatedFacadeDirectory) == hadGeneratedFacadeDirectory,
            "S1FuelMod verify-migration should not create or remove the real generated facade directory.");
        Assert(
            Directory.Exists(runsDirectory) == hadRunsDirectory,
            "S1FuelMod verify-migration should not create or remove real s1interop-runs.");
        Assert(
            File.Exists(localProps) == hadLocalProps,
            "S1FuelMod verify-migration should not create or remove real local.build.props.");
    }

    private void VerifyMigrationBuildGateConvertsMonoOnlyS1FuelModCopy()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "S1FuelMod");
        string sourceProject = Path.Combine(sourceDirectory, "S1FuelMod.csproj");
        string sourceFuelVehicleData = Path.Combine(sourceDirectory, "Systems", "FuelVehicleData.cs");
        string il2CppRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
        string monoRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
        string il2CppAssembly = Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll");
        string monoAssembly = Path.Combine(monoRoot, "Schedule I_Data", "Managed", "Assembly-CSharp.dll");
        if (!File.Exists(sourceProject) || !File.Exists(sourceFuelVehicleData) || !File.Exists(il2CppAssembly) || !File.Exists(monoAssembly))
        {
            Console.WriteLine("Skipping S1FuelMod Mono-only build-gated integration because local game roots are not available.");
            return;
        }

        string originalProjectHash = ComputeSha256(sourceProject);
        string originalFuelVehicleDataHash = ComputeSha256(sourceFuelVehicleData);
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "S1FuelMod.csproj");
            ReduceS1FuelModCopyToMonoOnly(tempProject);
            string monoOnlyProjectHash = ComputeSha256(tempProject);

            ProjectAnalysis monoOnlyProject = analyzer.Analyze(tempProject).Projects.Single();
            AssertHasRuntime(monoOnlyProject, "Debug Mono", RuntimeKind.Mono);
            AssertHasRuntime(monoOnlyProject, "Release Mono", RuntimeKind.Mono);
            Assert(
                monoOnlyProject.Configurations.All(configuration => configuration.Runtime != RuntimeKind.Il2Cpp),
                "The S1FuelMod test fixture must start as Mono-only before migration.");

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(
                    DualRuntime: true,
                    Build: true,
                    BuildTimeoutSeconds: 240,
                    IncludeSourceMigrations: true,
                    Il2CppGamePath: il2CppRoot,
                    MonoGamePath: monoRoot));

            Assert(result.PlannedOperations > 0, "Mono-only S1FuelMod migration should plan sandbox operations.");
            Assert(result.AppliedOperations > 0, "Mono-only S1FuelMod migration should apply sandbox operations.");
            Assert(result.SandboxDeleted, "S1FuelMod build-gated verification should delete its sandbox.");
            Assert(result.AfterDiagnostics.Count == 0, $"S1FuelMod migration should leave no analyzer diagnostics. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            if (IsDependencyNotReadyBuildGate(result))
            {
                Assert(
                    string.Equals(ComputeSha256(tempProject), monoOnlyProjectHash, StringComparison.Ordinal),
                    "verify-migration should not mutate the Mono-only S1FuelMod fixture project when build verification is blocked by local dependencies.");
                Assert(
                    string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
                    "S1FuelMod verify-migration should not mutate the real project file when build verification is blocked by local dependencies.");
                Assert(
                    string.Equals(ComputeSha256(sourceFuelVehicleData), originalFuelVehicleDataHash, StringComparison.Ordinal),
                    "S1FuelMod verify-migration should not mutate real source files when build verification is blocked by local dependencies.");
                Console.WriteLine($"Skipping S1FuelMod build-success assertions because local dependency references are not ready: {FormatBuildResults(result.BuildResults)}");
                return;
            }

            Assert(
                result.Success,
                $"Mono-only S1FuelMod copy should migrate to dual runtime and build for both runtimes. Build output: {FormatBuildResults(result.BuildResults)} Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            Assert(result.BuildResults is not null && result.BuildResults.Count == 4, $"S1FuelMod should build-check four configurations. Build output: {FormatBuildResults(result.BuildResults)}");
            IReadOnlyList<MigrationBuildResult> buildResults = result.BuildResults!;
            Assert(buildResults.All(build => build.Success), $"Every S1FuelMod migrated build should pass. Build output: {FormatBuildResults(buildResults)}");
            Assert(buildResults.Any(build => build.Configuration == "Debug Mono" && build.Runtime == RuntimeKind.Mono), "S1FuelMod build results should include Debug Mono.");
            Assert(buildResults.Any(build => build.Configuration == "Release Mono" && build.Runtime == RuntimeKind.Mono), "S1FuelMod build results should include Release Mono.");
            Assert(buildResults.Any(build => build.Configuration == "Debug Il2cpp" && build.Runtime == RuntimeKind.Il2Cpp), "S1FuelMod build results should include generated Debug Il2cpp.");
            Assert(buildResults.Any(build => build.Configuration == "Release Il2cpp" && build.Runtime == RuntimeKind.Il2Cpp), "S1FuelMod build results should include generated Release Il2cpp.");
            Assert(
                string.Equals(ComputeSha256(tempProject), monoOnlyProjectHash, StringComparison.Ordinal),
                "verify-migration should not mutate the Mono-only S1FuelMod fixture project.");
            Assert(
                string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
                "S1FuelMod verify-migration should not mutate the real project file.");
            Assert(
                string.Equals(ComputeSha256(sourceFuelVehicleData), originalFuelVehicleDataHash, StringComparison.Ordinal),
                "S1FuelMod verify-migration should not mutate real source files.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationCleansBigWillyPropertyBasedReferences()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "BigWillyMod");
        string sourceProject = Path.Combine(sourceDirectory, "BigWillyMod.csproj");
        string originalProjectHash = ComputeSha256(sourceProject);
        string runsDirectory = Path.Combine(sourceDirectory, "s1interop-runs");
        string localProps = Path.Combine(sourceDirectory, "local.build.props");
        bool hadRunsDirectory = Directory.Exists(runsDirectory);
        bool hadLocalProps = File.Exists(localProps);

        MigrationVerificationResult result = new MigrationVerifier().Verify(
            sourceProject,
            new MigrationVerifierOptions(DualRuntime: true));

        Assert(result.Success, $"BigWillyMod verify-migration should pass after fixing property-based generated-wrapper references. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(
            result.AppliedOperations > 0,
            "BigWillyMod verify-migration should apply build-surface operations in the sandbox.");
        Assert(
            result.AfterDiagnostics.All(diagnostic => diagnostic.RuleId != "reference_should_not_copy_local"),
            "BigWillyMod verify-migration should clear property-based Private=false reference diagnostics.");
        Assert(result.SandboxDeleted, "BigWillyMod verify-migration should delete its sandbox.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "BigWillyMod verify-migration should not mutate the real project file.");
        Assert(
            Directory.Exists(runsDirectory) == hadRunsDirectory,
            "BigWillyMod verify-migration should not create or remove real s1interop-runs.");
        Assert(
            File.Exists(localProps) == hadLocalProps,
            "BigWillyMod verify-migration should not create or remove real local.build.props.");
    }

    private void VerifyMigrationMovesBetterJukeboxAbsoluteHintPaths()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "BetterJukebox");
        string sourceProject = Path.Combine(sourceDirectory, "BetterJukebox.csproj");
        string originalProjectHash = ComputeSha256(sourceProject);
        string runsDirectory = Path.Combine(sourceDirectory, "s1interop-runs");
        string localProps = Path.Combine(sourceDirectory, "local.build.props");
        bool hadRunsDirectory = Directory.Exists(runsDirectory);
        bool hadLocalProps = File.Exists(localProps);

        MigrationVerificationResult result = new MigrationVerifier().Verify(
            sourceProject,
            new MigrationVerifierOptions(DualRuntime: true));

        Assert(result.Success, $"BetterJukebox verify-migration should clear automatic migration diagnostics. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(
            result.AfterDiagnostics.All(diagnostic => diagnostic.RuleId != "local_path_in_project"),
            $"BetterJukebox migration should move absolute HintPath values into local build props. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(
            result.AfterDiagnostics.All(diagnostic => diagnostic.RuleId != "stale_publicized_surface"),
            $"BetterJukebox migration should replace stale publicized references with build-time publicization. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(result.SandboxDeleted, "BetterJukebox verify-migration should delete its sandbox.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "BetterJukebox verify-migration should not mutate the real project file.");
        Assert(
            Directory.Exists(runsDirectory) == hadRunsDirectory,
            "BetterJukebox verify-migration should not create or remove real s1interop-runs.");
        Assert(
            File.Exists(localProps) == hadLocalProps,
            "BetterJukebox verify-migration should not create or remove real local.build.props.");
    }

    private void VerifyMigrationPreservesBguiMixedConfigurationPaths()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "bGUI");
        string sourceProject = Path.Combine(sourceDirectory, "bGUI.csproj");
        string originalProjectHash = ComputeSha256(sourceProject);
        string runsDirectory = Path.Combine(sourceDirectory, "s1interop-runs");
        string localProps = Path.Combine(sourceDirectory, "local.build.props");
        bool hadRunsDirectory = Directory.Exists(runsDirectory);
        bool hadLocalProps = File.Exists(localProps);

        MigrationVerificationResult result = new MigrationVerifier().Verify(
            sourceProject,
            new MigrationVerifierOptions(DualRuntime: true));

        Assert(result.Success, $"bGUI verify-migration should preserve mixed Debug/Mono/Il2cpp config inference. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(result.SandboxDeleted, "bGUI verify-migration should delete its sandbox.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "bGUI verify-migration should not mutate the real project file.");
        Assert(
            Directory.Exists(runsDirectory) == hadRunsDirectory,
            "bGUI verify-migration should not create or remove real s1interop-runs.");
        Assert(
            File.Exists(localProps) == hadLocalProps,
            "bGUI verify-migration should not create or remove real local.build.props.");
    }

    private void VerifyMigrationConvergesOnHoverboardWithoutMutatingSource()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "Hoverboard");
        string sourceProject = Path.Combine(sourceDirectory, "Hoverboard.csproj");
        string originalProjectHash = ComputeSha256(sourceProject);
        string runsDirectory = Path.Combine(sourceDirectory, "s1interop-runs");
        string localProps = Path.Combine(sourceDirectory, "local.build.props");
        bool hadRunsDirectory = Directory.Exists(runsDirectory);
        bool hadLocalProps = File.Exists(localProps);

        MigrationVerificationResult result = new MigrationVerifier().Verify(
            sourceProject,
            new MigrationVerifierOptions(DualRuntime: true));

        Assert(
            result.Success,
            $"Hoverboard verify-migration should converge on runtime defines, local paths, and reference copy-local cleanup. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(
            result.BeforeDiagnostics.Any(diagnostic => diagnostic.RuleId == "missing_runtime_define"),
            "Hoverboard fixture should exercise missing runtime define migration.");
        Assert(
            result.BeforeDiagnostics.Any(diagnostic => diagnostic.RuleId == "local_path_in_project"),
            "Hoverboard fixture should exercise local path migration.");
        Assert(
            result.BeforeDiagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
            "Hoverboard's effective LangVersion=latest should avoid unnecessary C# 10 migration.");
        Assert(result.SandboxDeleted, "Hoverboard verify-migration should delete its sandbox.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "Hoverboard verify-migration should not mutate the real project file.");
        Assert(
            Directory.Exists(runsDirectory) == hadRunsDirectory,
            "Hoverboard verify-migration should not create or remove real s1interop-runs.");
        Assert(
            File.Exists(localProps) == hadLocalProps,
            "Hoverboard verify-migration should not create or remove real local.build.props.");
    }

    private void VerifyMigrationBuildGateConvertsMonoOnlyBotanistFixCopy()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "BotanistFix");
        string sourceProject = Path.Combine(sourceDirectory, "BotanistFix.csproj");
        string il2CppRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
        string monoRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
        string il2CppAssembly = Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll");
        string monoAssembly = Path.Combine(monoRoot, "Schedule I_Data", "Managed", "Assembly-CSharp.dll");
        if (!File.Exists(sourceProject) || !File.Exists(il2CppAssembly) || !File.Exists(monoAssembly))
        {
            Console.WriteLine("Skipping BotanistFix Mono-only build-gated integration because local game roots are not available.");
            return;
        }

        string originalProjectHash = ComputeSha256(sourceProject);
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "BotanistFix.csproj");
            ReduceBotanistFixCopyToMonoOnly(tempProject);
            string monoOnlyProjectHash = ComputeSha256(tempProject);

            ProjectAnalysis monoOnlyProject = analyzer.Analyze(tempProject).Projects.Single();
            AssertHasRuntime(monoOnlyProject, "Mono", RuntimeKind.Mono);
            AssertHasRuntime(monoOnlyProject, "MonoRelease", RuntimeKind.Mono);
            Assert(
                monoOnlyProject.Configurations.All(configuration => configuration.Runtime != RuntimeKind.Il2Cpp),
                "The BotanistFix test fixture must start as Mono-only before migration.");

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(
                    DualRuntime: true,
                    Build: true,
                    BuildTimeoutSeconds: 180,
                    Il2CppGamePath: il2CppRoot,
                    MonoGamePath: monoRoot));

            Assert(
                result.Success,
                $"Mono-only BotanistFix copy should migrate to dual runtime and build for both runtimes. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "BotanistFix build-gated verification should delete its sandbox.");
            Assert(result.AfterDiagnostics.Count == 0, $"BotanistFix migration should leave no analyzer diagnostics. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            Assert(result.BuildResults is not null && result.BuildResults.Count == 4, $"BotanistFix should build-check four configurations. Build output: {FormatBuildResults(result.BuildResults)}");
            IReadOnlyList<MigrationBuildResult> buildResults = result.BuildResults!;
            Assert(buildResults.All(build => build.Success), $"Every BotanistFix migrated build should pass. Build output: {FormatBuildResults(buildResults)}");
            Assert(buildResults.Any(build => build.Configuration == "Mono" && build.Runtime == RuntimeKind.Mono), "BotanistFix build results should include Mono.");
            Assert(buildResults.Any(build => build.Configuration == "MonoRelease" && build.Runtime == RuntimeKind.Mono), "BotanistFix build results should include MonoRelease.");
            Assert(buildResults.Any(build => build.Configuration == "Il2cpp" && build.Runtime == RuntimeKind.Il2Cpp), "BotanistFix build results should include generated Il2cpp.");
            Assert(buildResults.Any(build => build.Configuration == "Il2cppRelease" && build.Runtime == RuntimeKind.Il2Cpp), "BotanistFix build results should include generated Il2cppRelease.");
            Assert(
                string.Equals(ComputeSha256(tempProject), monoOnlyProjectHash, StringComparison.Ordinal),
                "verify-migration should not mutate the Mono-only fixture project.");
            Assert(
                string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
                "BotanistFix verify-migration should not mutate the real project file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateConvertsMonoOnlyS1VoiceChatCopy()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "S1-VoiceChat");
        string sourceProject = Path.Combine(sourceDirectory, "src", "S1VoiceChat", "S1VoiceChat.csproj");
        string il2CppRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
        string monoRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
        string il2CppAssembly = Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll");
        string monoAssembly = Path.Combine(monoRoot, "Schedule I_Data", "Managed", "Assembly-CSharp.dll");
        if (!File.Exists(sourceProject) || !File.Exists(il2CppAssembly) || !File.Exists(monoAssembly))
        {
            Console.WriteLine("Skipping S1-VoiceChat Mono-only build-gated integration because local game roots are not available.");
            return;
        }

        string originalProjectHash = ComputeSha256(sourceProject);
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "src", "S1VoiceChat", "S1VoiceChat.csproj");
            ReduceS1VoiceChatCopyToMonoOnly(tempProject);
            string monoOnlyProjectHash = ComputeSha256(tempProject);

            ProjectAnalysis monoOnlyProject = analyzer.Analyze(tempProject).Projects.Single();
            AssertHasRuntime(monoOnlyProject, "MonoMelon", RuntimeKind.Mono);
            Assert(
                monoOnlyProject.Configurations.All(configuration => !configuration.Name.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase)),
                "The S1-VoiceChat test fixture must start without IL2CPP build configurations before migration.");

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(
                    DualRuntime: true,
                    Build: true,
                    BuildTimeoutSeconds: 180,
                    Il2CppGamePath: il2CppRoot,
                    MonoGamePath: monoRoot));

            Assert(
                result.Success,
                $"Mono-only S1-VoiceChat copy should migrate to dual runtime and build for both runtimes. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "S1-VoiceChat build-gated verification should delete its sandbox.");
            Assert(result.AfterDiagnostics.Count == 0, $"S1-VoiceChat migration should leave no analyzer diagnostics. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            Assert(result.BuildResults is not null && result.BuildResults.Count == 2, $"S1-VoiceChat should build-check two configurations. Build output: {FormatBuildResults(result.BuildResults)}");
            IReadOnlyList<MigrationBuildResult> buildResults = result.BuildResults!;
            Assert(buildResults.All(build => build.Success), $"Every S1-VoiceChat migrated build should pass. Build output: {FormatBuildResults(buildResults)}");
            Assert(buildResults.Any(build => build.Configuration == "MonoMelon" && build.Runtime == RuntimeKind.Mono), "S1-VoiceChat build results should include MonoMelon.");
            Assert(buildResults.Any(build => build.Configuration == "Il2cppMelon" && build.Runtime == RuntimeKind.Il2Cpp), "S1-VoiceChat build results should include generated Il2cppMelon.");
            Assert(
                string.Equals(ComputeSha256(tempProject), monoOnlyProjectHash, StringComparison.Ordinal),
                "verify-migration should not mutate the Mono-only S1-VoiceChat fixture project.");
            Assert(
                string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
                "S1-VoiceChat verify-migration should not mutate the real project file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateConvertsRealBarsGraphics()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "BarsGraphics");
        string sourceProject = Path.Combine(sourceDirectory, "BarsGraphics.csproj");
        string il2CppRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
        string monoRoot = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
        string il2CppAssembly = Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll");
        string monoAssembly = Path.Combine(monoRoot, "Schedule I_Data", "Managed", "Assembly-CSharp.dll");
        if (!File.Exists(sourceProject) || !File.Exists(il2CppAssembly) || !File.Exists(monoAssembly))
        {
            Console.WriteLine("Skipping BarsGraphics build-gated integration because local game roots are not available.");
            return;
        }

        string originalProjectHash = ComputeSha256(sourceProject);
        string runsDirectory = Path.Combine(sourceDirectory, "s1interop-runs");
        string localProps = Path.Combine(sourceDirectory, "local.build.props");
        bool hadRunsDirectory = Directory.Exists(runsDirectory);
        bool hadLocalProps = File.Exists(localProps);

        MigrationVerificationResult result = new MigrationVerifier().Verify(
            sourceProject,
            new MigrationVerifierOptions(
                DualRuntime: true,
                Build: true,
                BuildTimeoutSeconds: 120,
                Il2CppGamePath: il2CppRoot,
                MonoGamePath: monoRoot));

        Assert(result.Success, $"BarsGraphics should migrate and build both runtimes. Build output: {FormatBuildResults(result.BuildResults)}");
        Assert(result.AfterDiagnostics.Count == 0, $"BarsGraphics migration should clear analyzer diagnostics before build classification. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        IReadOnlyList<MigrationBuildResult> buildResults = result.BuildResults ?? [];
        Assert(buildResults.Count == 4, $"BarsGraphics should build-check four configurations, got {buildResults.Count}.");
        Assert(
            buildResults.All(build => build.Success),
            $"All BarsGraphics runtime configurations should build after migration. Build output: {FormatBuildResults(buildResults)}");
        Assert(buildResults.Any(build => build.Configuration == "MonoDevelopment" && build.Runtime == RuntimeKind.Mono), "BarsGraphics build results should include MonoDevelopment.");
        Assert(buildResults.Any(build => build.Configuration == "MonoStable" && build.Runtime == RuntimeKind.Mono), "BarsGraphics build results should include MonoStable.");
        Assert(buildResults.Any(build => build.Configuration == "Il2cppDevelopment" && build.Runtime == RuntimeKind.Il2Cpp), "BarsGraphics build results should include Il2cppDevelopment.");
        Assert(buildResults.Any(build => build.Configuration == "Il2cppStable" && build.Runtime == RuntimeKind.Il2Cpp), "BarsGraphics build results should include Il2cppStable.");
        Assert(result.SandboxDeleted, "BarsGraphics build-gated verification should delete its sandbox.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "BarsGraphics verify-migration should not mutate the real project file.");
        Assert(
            Directory.Exists(runsDirectory) == hadRunsDirectory,
            "BarsGraphics verify-migration should not create or remove real s1interop-runs.");
        Assert(
            File.Exists(localProps) == hadLocalProps,
            "BarsGraphics verify-migration should not create or remove real local.build.props.");
    }

    private void VerifyMigrationMovesGameRootModDependencyHintPaths()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "CasinoDirectDeposit");
        string sourceProject = Path.Combine(sourceDirectory, "CasinoDirectDeposit.csproj");
        string originalProjectHash = ComputeSha256(sourceProject);

        MigrationVerificationResult result = new MigrationVerifier().Verify(
            sourceProject,
            new MigrationVerifierOptions(DualRuntime: true));

        Assert(result.Success, $"CasinoDirectDeposit verify-migration should move game-root Mods dependency HintPath values. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
        Assert(result.SandboxDeleted, "CasinoDirectDeposit verify-migration should delete its sandbox.");
        Assert(
            string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
            "CasinoDirectDeposit verify-migration should not mutate the real project file.");
    }

    private void VerifyMigrationHandlesIterativeRuntimeDefineFixes()
    {
        string[] projectRelativePaths =
        [
            @"LocalLobby\LocalLobby.csproj",
            @"SteamGameServerMod\SteamGameServerMod\SteamGameServerMod.csproj"
        ];

        foreach (string projectRelativePath in projectRelativePaths)
        {
            string sourceProject = Path.Combine(WorkspaceRoot, projectRelativePath);
            string originalProjectHash = ComputeSha256(sourceProject);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                sourceProject,
                new MigrationVerifierOptions(DualRuntime: true));

            Assert(result.Success, $"{projectRelativePath} verify-migration should converge after iterative runtime define fixes. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            Assert(result.PlannedOperations > result.AppliedOperations, $"{projectRelativePath} should exercise multi-pass verification planning.");
            Assert(result.SandboxDeleted, $"{projectRelativePath} verify-migration should delete its sandbox.");
            Assert(
                string.Equals(ComputeSha256(sourceProject), originalProjectHash, StringComparison.Ordinal),
                $"{projectRelativePath} verify-migration should not mutate the real project file.");
        }
    }

    private void S1DockExportsCrossCompatIsNotForcedToIl2CppFramework()
    {
        ProjectAnalysis project = AnalyzeProject(@"S1DockExports\S1DockExports.csproj");

        AssertHasRuntime(project, "CrossCompat", RuntimeKind.CrossCompat);
        AssertHasTargetFramework(project, "CrossCompat", "netstandard2.1");
        Assert(
            project.Diagnostics.All(diagnostic =>
                diagnostic.RuleId != "wrong_target_framework" ||
                !string.Equals(diagnostic.Configuration, "CrossCompat", StringComparison.OrdinalIgnoreCase)),
            "S1DockExports CrossCompat config should not be forced to net6.0.");
    }

    private void VerifyMigrationMovesAbsoluteSiblingDllHintPaths()
    {
        string projectPath = Path.Combine(WorkspaceRoot, @"NPCPack\NPCPack.csproj");

        MigrationVerificationResult result = new MigrationVerifier().Verify(projectPath, new MigrationVerifierOptions(DualRuntime: true));

        Assert(result.Success, "NPCPack verify-migration should move absolute sibling DLL hint paths into local props.");
        Assert(result.SandboxDeleted, "NPCPack verify-migration should delete its sandbox.");
        Assert(
            result.BeforeDiagnostics.Any(diagnostic =>
                diagnostic.RuleId == "local_path_in_project" &&
                (diagnostic.Evidence ?? string.Empty).EndsWith(@"S1API.dll", StringComparison.OrdinalIgnoreCase)),
            "NPCPack should initially report the absolute S1API.dll hint path.");
        Assert(
            result.AfterDiagnostics.All(diagnostic => diagnostic.RuleId != "local_path_in_project"),
            "NPCPack should not retain local path diagnostics after sandboxed migration.");
    }

    private void VerifyMigrationSupportsWorkspaceDirectories()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string firstProjectDirectory = Path.Combine(tempRoot, "FirstMod");
            string secondProjectDirectory = Path.Combine(tempRoot, "SecondMod");
            Directory.CreateDirectory(firstProjectDirectory);
            Directory.CreateDirectory(secondProjectDirectory);
            File.WriteAllText(
                Path.Combine(firstProjectDirectory, "FirstMod.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(firstProjectDirectory, "Core.cs"),
                """
                namespace FirstMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);
            File.WriteAllText(
                Path.Combine(secondProjectDirectory, "SecondMod.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(secondProjectDirectory, "Core.cs"),
                """
                namespace SecondMod;

                public static class Core
                {
                    public static int Value => 2;
                }
                """);

            WorkspaceMigrationVerificationResult result = new MigrationVerifier().VerifyWorkspace(tempRoot);

            Assert(result.Success, "Workspace verify-migration should pass when every discovered project passes.");
            Assert(result.ProjectCount == 2, $"Workspace verify-migration should discover two projects, found {result.ProjectCount}.");
            Assert(result.Projects.All(project => project.Success), "Every workspace project should pass verification.");
            Assert(result.Projects.All(project => project.SandboxDeleted), "Every workspace project sandbox should be deleted.");
            Assert(
                result.Projects.All(project => !Directory.Exists(Path.GetDirectoryName(project.SandboxProjectPath)!)),
                "Workspace verify-migration should remove every sandbox project directory.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }
}
