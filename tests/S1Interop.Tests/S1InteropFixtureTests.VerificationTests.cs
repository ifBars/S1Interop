internal sealed partial class S1InteropFixtureTests
{
    private void WorkspaceAnalysisSkipsEditorMetadataDirectories()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string realProjectDirectory = Path.Combine(tempRoot, "RealMod");
            string metadataProjectDirectory = Path.Combine(tempRoot, ".cursor", "skills", "Noise");
            string metadataSourceDirectory = Path.Combine(realProjectDirectory, ".cursor", "skills");
            string toolProjectDirectory = Path.Combine(tempRoot, "S1Interop", "tests", "S1Interop.Tests");
            Directory.CreateDirectory(realProjectDirectory);
            Directory.CreateDirectory(metadataProjectDirectory);
            Directory.CreateDirectory(metadataSourceDirectory);
            Directory.CreateDirectory(toolProjectDirectory);

            File.WriteAllText(
                Path.Combine(realProjectDirectory, "RealMod.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(realProjectDirectory, "Core.cs"),
                """
                namespace RealMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);
            File.WriteAllText(
                Path.Combine(metadataProjectDirectory, "Noise.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(metadataSourceDirectory, "NoiseComponent.cs"),
                """
                namespace RealMod.Metadata;

                [MelonLoader.RegisterTypeInIl2Cpp]
                public class NoiseComponent : UnityEngine.MonoBehaviour
                {
                }
                """);
            File.WriteAllText(
                Path.Combine(toolProjectDirectory, "S1Interop.Tests.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            WorkspaceAnalysis analysis = analyzer.Analyze(tempRoot);

            Assert(analysis.Projects.Count == 1, $"Workspace analysis should skip editor metadata and tool/test projects, found {analysis.Projects.Count}.");
            Assert(
                analysis.Projects.Single().ProjectPath.EndsWith(@"RealMod\RealMod.csproj", StringComparison.OrdinalIgnoreCase),
                "Workspace analysis should retain the real mod project.");
            Assert(
                analysis.Diagnostics.All(diagnostic => diagnostic.RuleId != "injected_type_missing_intptr_constructor"),
                "Workspace analysis should not inspect source files inside editor metadata directories.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateBuildsSandboxConfigurations()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "BuildableMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Configurations>Debug;Release</Configurations>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace BuildableMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(result.Success, $"Buildable project should pass sandboxed build verification. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Buildable project build verification should delete its sandbox.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run both configurations, got {result.BuildResults?.Count ?? 0}.");
            IReadOnlyList<MigrationBuildResult> buildResults = result.BuildResults!;
            Assert(
                buildResults.All(build => build.Success),
                $"Every sandbox build should pass. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                buildResults.All(build => build.FailureKind == "None"),
                $"Successful sandbox builds should be classified as None. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                buildResults.All(build => build.ReadinessStatus == "Ready" && build.Attribution == "None"),
                $"Successful sandbox builds should be classified as Ready/None. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                buildResults.Select(build => build.Configuration).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(["Debug", "Release"], StringComparer.OrdinalIgnoreCase),
                "Build verification should report Debug and Release configuration builds.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateStagesLocalS1InteropGeneratorPackage()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "GeneratorPackageMod.csproj");
            File.WriteAllText(
                tempProject,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{{S1InteropPackageInfo.GeneratorsPackageId}}" Version="{{S1InteropPackageInfo.GeneratorsPackageVersion}}" PrivateAssets="{{S1InteropPackageInfo.PrivateAssets}}" IncludeAssets="{{S1InteropPackageInfo.AnalyzerIncludeAssets}}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                [assembly: S1Interop.S1InteropType("System.String", Alias = "StringType")]

                namespace GeneratorPackageMod
                {
                    internal static class Core
                    {
                        internal static bool HasStringType => S1Interop.Generated.S1InteropTypeRegistry.StringType is not null;
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 120));

            Assert(result.Success, $"Generator package staging should let a sandbox build restore and compile. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.Command.Contains("RestoreAdditionalProjectSources", StringComparison.Ordinal) &&
                    build.Command.Contains("S1Interop.ExternalReferences", StringComparison.Ordinal)),
                $"Build commands should include sandbox-local S1Interop package source. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Generator package staging verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateDoesNotForwardUnexpandedLocalProperties()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "LocalPropsExpressionMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="local.build.props" Condition="Exists('local.build.props')" />
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "local.build.props"),
                $"""
                <Project>
                  <PropertyGroup>
                    <S1InteropGeneratorPackageSource>{Path.Combine(tempRoot, "feed")}</S1InteropGeneratorPackageSource>
                    <RestoreAdditionalProjectSources>$(S1InteropGeneratorPackageSource);$(RestoreAdditionalProjectSources)</RestoreAdditionalProjectSources>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace LocalPropsExpressionMod;

                internal static class Core
                {
                    internal static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(result.Success, $"Local props expression fixture should build. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    !build.Command.Contains("$(S1InteropGeneratorPackageSource)", StringComparison.Ordinal) &&
                    !build.Command.Contains("$(RestoreAdditionalProjectSources)", StringComparison.Ordinal)),
                $"Build command should not forward unexpanded MSBuild property expressions from local.build.props. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Local props expression verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGatePreservesAncestorNuGetConfig()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string packageFeed = Path.Combine(tempRoot, "local-feed");
            string projectDirectory = Path.Combine(tempRoot, "ModProject");
            Directory.CreateDirectory(packageFeed);
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(
                Path.Combine(tempRoot, "NuGet.config"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="LocalFixtureFeed" value="{packageFeed}" />
                  </packageSources>
                </configuration>
                """);

            string tempProject = Path.Combine(projectDirectory, "PackageSourceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Missing.ScheduleOne.Package" Version="1.2.3" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Core.cs"),
                """
                namespace PackageSourceMod;

                internal static class Core
                {
                    internal static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Missing package fixture should fail build verification.");
            Assert(
                result.BuildResults!.All(build =>
                    build.FailureKind == "PackageFeedMissing" &&
                    build.Issues.Any(issue =>
                        issue.RestoreSources?.Contains("LocalFixtureFeed", StringComparer.OrdinalIgnoreCase) == true)),
                $"Sandbox restore should preserve the ancestor NuGet.config package source as structured issue data. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.Issues.Any(issue =>
                        issue.Remediation?.Contains("Current restore sources:", StringComparison.OrdinalIgnoreCase) == true)),
                $"Package feed remediation should include current restore sources. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Ancestor NuGet.config verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void CliReporterPrintsPackageRestoreSources()
    {
        var result = new MigrationVerificationResult(
            SourceProjectPath: @"C:\Mods\PackageSourceMod\PackageSourceMod.csproj",
            SandboxProjectPath: @"C:\Temp\S1Interop.Verify.abc\PackageSourceMod.csproj",
            Success: false,
            SandboxDeleted: true,
            PlannedOperations: 0,
            AppliedOperations: 0,
            ManifestPath: null,
            BeforeDiagnostics: Array.Empty<InteropDiagnostic>(),
            AfterDiagnostics: Array.Empty<InteropDiagnostic>(),
            BuildResults:
            [
                new MigrationBuildResult(
                    "Debug",
                    RuntimeKind.Unknown,
                    Success: false,
                    TimedOut: false,
                    ExitCode: 1,
                    ReadinessStatus: "BlockedByPackageRestore",
                    Attribution: "DependencyNotReady",
                    FailureKind: "PackageFeedMissing",
                    Summary: "Package 'Missing.ScheduleOne.Package 1.2.3' is not available from the configured NuGet sources for Debug.",
                    Issues:
                    [
                        new MigrationBuildIssue(
                            "PackageFeedMissing",
                            "Package 'Missing.ScheduleOne.Package 1.2.3' is not available from the configured NuGet sources for Debug.",
                            Include: "Missing.ScheduleOne.Package",
                            Remediation: "Add the NuGet source that provides Missing.ScheduleOne.Package 1.2.3 and run restore again. Current restore sources: LocalFixtureFeed, NuGet.org.",
                            Version: "1.2.3",
                            RestoreSources: ["LocalFixtureFeed", "NuGet.org"])
                    ],
                    Command: "dotnet msbuild PackageSourceMod.csproj -restore",
                    Output: string.Empty)
            ]);

        TextWriter originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            CliReporter.PrintVerificationResult(result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = writer.ToString();
        Assert(
            output.Contains("restore sources: LocalFixtureFeed, NuGet.org", StringComparison.Ordinal),
            $"Text reporter should print structured package restore sources. Output:{Environment.NewLine}{output}");
        Assert(
            output.Contains("fix: Add the NuGet source", StringComparison.Ordinal),
            $"Text reporter should preserve package remediation. Output:{Environment.NewLine}{output}");
    }

    private void VerifyMigrationBuildGateFailsCompilerBrokenSandbox()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "CompilerBrokenMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace CompilerBrokenMod;

                public static class Core
                {
                    public static int Value => MissingSymbol;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Compiler-broken project should fail verify-migration when build verification is enabled.");
            Assert(result.AfterDiagnostics.Count == 0, "Compiler-broken project should be analyzer-clean so the failure is attributable to the build gate.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.Any(build => !build.Success && build.Output.Contains("MissingSymbol", StringComparison.Ordinal)),
                $"Build failure output should include the compiler error. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "CompileError"),
                $"Compiler-broken project should classify failures as CompileError. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.ReadinessStatus == "CompileFailed" &&
                    build.Attribution == "MigrationCompileFailure"),
                $"Compiler-broken project should classify attribution as MigrationCompileFailure. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Compiler-broken project build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateReportsMissingHintPathReadiness()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MissingReferenceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Missing.ScheduleOne.Dependency">
                      <HintPath>external\Missing.ScheduleOne.Dependency.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace MissingReferenceMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Missing reference project should fail verify-migration when build verification is enabled.");
            Assert(result.AfterDiagnostics.Count == 0, "Missing reference project should be analyzer-clean so the failure is attributable to build readiness.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "MissingReference"),
                $"Missing hint paths should classify failures as MissingReference. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.ReadinessStatus == "BlockedByLocalReferences" &&
                    build.Attribution == "DependencyNotReady"),
                $"Missing hint paths should classify attribution as DependencyNotReady. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build => build.Issues.Any(issue =>
                    issue.Kind == "MissingReference" &&
                    issue.Include == "Missing.ScheduleOne.Dependency" &&
                    issue.Path?.EndsWith(@"external\Missing.ScheduleOne.Dependency.dll", StringComparison.OrdinalIgnoreCase) == true)),
                $"Missing hint paths should be reported as structured build issues. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Missing reference build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateClassifiesExternalReferenceSurfaceFailures()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ReferenceSurfaceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "ScheduleOneStub.cs"),
                """
                namespace ScheduleOne;

                public static class ExistingNamespaceMarker
                {
                }
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                using ScheduleOne.Dialogue;

                namespace ReferenceSurfaceMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Missing external reference surface project should fail verify-migration when build verification is enabled.");
            Assert(result.AfterDiagnostics.Count == 0, "Missing external reference surface project should be analyzer-clean so the failure is attributable to build references.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "ReferenceSurfaceMissing"),
                $"Missing external namespaces should classify failures as ReferenceSurfaceMissing. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.ReadinessStatus == "BlockedByReferenceSurface" &&
                    build.Attribution == "DependencyNotReady"),
                $"Missing external namespaces should be attributed to dependency readiness. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build => build.Issues.Any(issue =>
                    issue.Kind == "ReferenceSurfaceMissing" &&
                    issue.Remediation?.Contains("runtime game root", StringComparison.OrdinalIgnoreCase) == true)),
                $"Missing external namespaces should include actionable remediation. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Missing external reference surface build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateClassifiesExternalMemberSurfaceFailures()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ExternalMemberSurfaceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace ExternalMemberSurfaceMod;

                public sealed class PlayerCamera
                {
                }

                public static class Core
                {
                    public static void Restore(PlayerCamera camera)
                    {
                        camera.CloseInterface(0f, true);
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Missing external member surface project should fail verify-migration when build verification is enabled.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "ReferenceSurfaceMissing"),
                $"Missing external member APIs should classify failures as ReferenceSurfaceMissing. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.ReadinessStatus == "BlockedByReferenceSurface" &&
                    build.Attribution == "DependencyNotReady"),
                $"Missing external member APIs should be attributed to dependency readiness. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Missing external member surface build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateClassifiesIl2CppMemberSurfaceFailuresAsMigrationIssues()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "Il2CppMemberSurfaceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net6.0</TargetFramework>
                    <Configurations>Il2cpp</Configurations>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Il2cpp'">
                    <DefineConstants>IL2CPP</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace Il2CppMemberSurfaceMod;

                public sealed class PlayerCamera
                {
                }

                public static class Core
                {
                    public static void Restore(PlayerCamera camera)
                    {
                        camera.CloseInterface(0f, true);
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "IL2CPP member surface mismatch should fail verify-migration when build verification is enabled.");
            Assert(result.BuildResults?.Count == 1, $"Build verification should run the Il2cpp configuration, got {result.BuildResults?.Count ?? 0}.");
            MigrationBuildResult build = result.BuildResults![0];
            Assert(build.Runtime == RuntimeKind.Il2Cpp, $"Expected IL2CPP runtime inference, got {build.Runtime}.");
            Assert(build.FailureKind == "Il2CppApiSurfaceMismatch", $"Expected IL2CPP API surface mismatch, got {FormatBuildResults(result.BuildResults)}.");
            Assert(build.ReadinessStatus == "CompileFailed", $"Expected compile-failed readiness, got {FormatBuildResults(result.BuildResults)}.");
            Assert(build.Attribution == "MigrationCompileFailure", $"Expected migration compile attribution, got {FormatBuildResults(result.BuildResults)}.");
            Assert(
                build.Issues.Any(issue =>
                    issue.Kind == "Il2CppApiSurfaceMismatch" &&
                    issue.Remediation?.Contains("IL2CPP-safe shim", StringComparison.OrdinalIgnoreCase) == true),
                $"IL2CPP API mismatch should include shim/facade remediation. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "IL2CPP member surface mismatch build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateReportsUnsetLocalReferenceProperties()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "UnsetLocalReferencePropertiesMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup>
                    <GameDllPath>$(ManagedDllPath)</GameDllPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>$(GameDllPath)\Assembly-CSharp.dll</HintPath>
                    </Reference>
                    <Reference Include="UnityEngine.CoreModule">
                      <HintPath>$(GameDllPath)\UnityEngine.CoreModule.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Unset local reference properties should fail verify-migration when build verification is enabled.");
            Assert(
                result.AppliedOperations > 0,
                "Build verification should first apply local reference property scaffolding in the sandbox.");
            Assert(
                result.AfterDiagnostics.All(diagnostic => diagnostic.RuleId != "missing_local_reference_properties"),
                $"Local reference property scaffold diagnostic should be cleared before build classification. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "LocalBuildPropertiesUnset"),
                $"Unset local reference properties should classify failures as LocalBuildPropertiesUnset. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.ReadinessStatus == "BlockedByLocalReferences" &&
                    build.Attribution == "DependencyNotReady"),
                $"Unset local reference properties should be attributed to local dependency readiness. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build => build.Issues.Any(issue =>
                    issue.Kind == "LocalBuildPropertiesUnset" &&
                    issue.Include?.Contains("references", StringComparison.OrdinalIgnoreCase) == true)),
                $"Unset local reference properties should be collapsed into structured build issues. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Unset local reference property build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateClassifiesSiblingBinReferencesAsModDependencies()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string modDirectory = Path.Combine(tempRoot, "ConsumerMod");
            Directory.CreateDirectory(modDirectory);
            string tempProject = Path.Combine(modDirectory, "ConsumerMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="bGUI">
                      <HintPath>..\bGUI\bin\Il2cppRelease\net6.0\bGUI.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="0Harmony">
                      <HintPath>il2cpp_libs\0Harmony.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "Core.cs"),
                """
                namespace ConsumerMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Missing sibling bin dependency should block build verification.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "ModDependencyMissing"),
                $"Missing sibling bin dependencies should classify as ModDependencyMissing. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build => build.Issues.Any(issue =>
                    issue.Kind == "ModDependencyMissing" &&
                    issue.Include == "bGUI")),
                $"Missing sibling bin dependencies should include the unresolved dependency reference. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Missing sibling bin dependency build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGatePreservesProjectLocalDependencyDlls()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string dependencyDirectory = Path.Combine(tempRoot, "mono_libs");
            Directory.CreateDirectory(dependencyDirectory);
            File.Copy(
                typeof(MigrationVerifier).Assembly.Location,
                Path.Combine(dependencyDirectory, "S1Interop.Core.dll"),
                overwrite: true);

            string tempProject = Path.Combine(tempRoot, "ProjectLocalDependencyMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="S1Interop.Core">
                      <HintPath>mono_libs\S1Interop.Core.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace ProjectLocalDependencyMod;

                public static class Core
                {
                    public static int Value => 1;
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(result.Success, $"Project-local dependency DLLs under mono_libs should be preserved in the sandbox. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.BuildResults!.All(build => build.Success), $"Project-local dependency build verification should pass. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Project-local dependency build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateClassifiesMissingTransitiveExternalAssembly()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string fishNetProjectDirectory = Path.Combine(tempRoot, "FishNet.Runtime");
            string scheduleOneProjectDirectory = Path.Combine(tempRoot, "ScheduleOne.Core");
            string modDirectory = Path.Combine(tempRoot, "MissingTransitiveReferenceMod");
            Directory.CreateDirectory(fishNetProjectDirectory);
            Directory.CreateDirectory(scheduleOneProjectDirectory);
            Directory.CreateDirectory(modDirectory);

            string fishNetProject = Path.Combine(fishNetProjectDirectory, "FishNet.Runtime.csproj");
            File.WriteAllText(
                fishNetProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <AssemblyName>FishNet.Runtime</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(fishNetProjectDirectory, "NetworkBehaviour.cs"),
                """
                namespace FishNet.Object
                {
                    public class NetworkBehaviour
                    {
                    }
                }
                """);

            string scheduleOneProject = Path.Combine(scheduleOneProjectDirectory, "ScheduleOne.Core.csproj");
            File.WriteAllText(
                scheduleOneProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <AssemblyName>ScheduleOne.Core</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="FishNet.Runtime">
                      <HintPath>{Path.Combine(fishNetProjectDirectory, "bin", "Release", "netstandard2.1", "FishNet.Runtime.dll")}</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(scheduleOneProjectDirectory, "Property.cs"),
                """
                using FishNet.Object;

                namespace ScheduleOne.Property
                {
                    public class Property : NetworkBehaviour
                    {
                    }
                }
                """);

            ProcessResult fishNetBuild = RunDotNet("build", fishNetProject, "-c", "Release", "--nologo", "-v:minimal");
            Assert(fishNetBuild.ExitCode == 0, $"Failed to build fake FishNet.Runtime fixture: {fishNetBuild.Output}");
            ProcessResult scheduleOneBuild = RunDotNet("build", scheduleOneProject, "-c", "Release", "--nologo", "-v:minimal");
            Assert(scheduleOneBuild.ExitCode == 0, $"Failed to build fake ScheduleOne.Core fixture: {scheduleOneBuild.Output}");

            string libDirectory = Path.Combine(modDirectory, "lib");
            Directory.CreateDirectory(libDirectory);
            File.Copy(
                Path.Combine(scheduleOneProjectDirectory, "bin", "Release", "netstandard2.1", "ScheduleOne.Core.dll"),
                Path.Combine(libDirectory, "ScheduleOne.Core.dll"),
                overwrite: true);

            string tempProject = Path.Combine(modDirectory, "MissingTransitiveReferenceMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="ScheduleOne.Core">
                      <HintPath>lib\ScheduleOne.Core.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "Core.cs"),
                """
                namespace MissingTransitiveReferenceMod
                {
                    public sealed class Core : ScheduleOne.Property.Property
                    {
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(!result.Success, "Missing transitive external assembly project should fail verify-migration when build verification is enabled.");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run default Debug and Release configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.FailureKind == "ReferenceSurfaceMissing"),
                $"CS0012 transitive external assemblies should classify as ReferenceSurfaceMissing. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                result.BuildResults!.All(build =>
                    build.ReadinessStatus == "BlockedByReferenceSurface" &&
                    build.Attribution == "DependencyNotReady"),
                $"CS0012 transitive external assemblies should be attributed to dependency readiness. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Missing transitive external assembly build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGatePassesRuntimeGameRootsToMsBuild()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "RuntimeRootBuildMod.csproj");
            string il2CppRoot = Path.Combine(tempRoot, "Schedule I_public");
            string monoRoot = Path.Combine(tempRoot, "Schedule I_alternate");
            Directory.CreateDirectory(il2CppRoot);
            Directory.CreateDirectory(monoRoot);
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Configurations>Mono;IL2CPP</Configurations>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace RuntimeRootBuildMod
                {
                    public static class Core
                    {
                        public static int Value => 1;
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(
                    DualRuntime: false,
                    Build: true,
                    BuildTimeoutSeconds: 60,
                    Il2CppGamePath: il2CppRoot,
                    MonoGamePath: monoRoot));

            Assert(result.Success, $"Runtime root build project should pass build verification. Build output: {FormatBuildResults(result.BuildResults)}");
            MigrationBuildResult monoBuild = result.BuildResults!.Single(build => build.Configuration == "Mono");
            MigrationBuildResult il2CppBuild = result.BuildResults!.Single(build => build.Configuration == "IL2CPP");
            Assert(
                monoBuild.Command.Contains($"-p:S1Dir={monoRoot}", StringComparison.Ordinal) &&
                monoBuild.Command.Contains($"-p:GamePath={monoRoot}", StringComparison.Ordinal) &&
                monoBuild.Command.Contains($"-p:MonoAssembliesPath={Path.Combine(monoRoot, "Schedule I_Data", "Managed")}", StringComparison.Ordinal) &&
                monoBuild.Command.Contains($"-p:MelonLoaderNet35Path={Path.Combine(monoRoot, "MelonLoader", "net35")}", StringComparison.Ordinal),
                $"Mono build command should receive Mono game roots. Command: {monoBuild.Command}");
            Assert(
                il2CppBuild.Command.Contains($"-p:S1Dir={il2CppRoot}", StringComparison.Ordinal) &&
                il2CppBuild.Command.Contains($"-p:GamePath={il2CppRoot}", StringComparison.Ordinal) &&
                il2CppBuild.Command.Contains($"-p:Il2CppAssembliesPath={Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies")}", StringComparison.Ordinal) &&
                il2CppBuild.Command.Contains($"-p:MelonLoaderNet6Path={Path.Combine(il2CppRoot, "MelonLoader", "net6")}", StringComparison.Ordinal),
                $"IL2CPP build command should receive IL2CPP game roots. Command: {il2CppBuild.Command}");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateHydratesModDependencyProperties()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string modDirectory = Path.Combine(tempRoot, "ConsumerMod");
            string s1ApiDirectory = Path.Combine(tempRoot, "S1API", "S1API", "bin", "Il2CppMelon", "net6.0");
            string staleS1ApiDirectory = Path.Combine(tempRoot, "S1API", "stale");
            string bguiDirectory = Path.Combine(tempRoot, "bGUI", "bin", "Il2cppRelease", "net6.0");
            Directory.CreateDirectory(modDirectory);
            Directory.CreateDirectory(s1ApiDirectory);
            Directory.CreateDirectory(staleS1ApiDirectory);
            Directory.CreateDirectory(bguiDirectory);
            File.Copy(
                typeof(MigrationVerifier).Assembly.Location,
                Path.Combine(s1ApiDirectory, "S1API.dll"),
                overwrite: true);
            File.Copy(
                typeof(MigrationVerifier).Assembly.Location,
                Path.Combine(bguiDirectory, "bGUI.dll"),
                overwrite: true);
            File.WriteAllText(
                Path.Combine(staleS1ApiDirectory, "S1API.Il2Cpp.MelonLoader.dll"),
                "not a valid assembly");

            string tempProject = Path.Combine(modDirectory, "ConsumerMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="local.build.props" Condition="Exists('local.build.props')" />
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Configurations>IL2CPP</Configurations>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="S1API">
                      <HintPath>$(S1APIModsPath)\S1API.Il2Cpp.MelonLoader.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="bGUI">
                      <HintPath>$(BguiPath)\bGUI.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "local.build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <S1APIModsPath></S1APIModsPath>
                    <BguiPath></BguiPath>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "Core.cs"),
                """
                namespace ConsumerMod
                {
                    public static class Core
                    {
                        public static int Value => 1;
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(result.Success, $"Workspace sibling dependency should be staged into the sandbox under the expected reference filename. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.BuildResults!.Single().ReadinessStatus == "Ready", "Staged workspace dependency should make the sandbox build readiness check pass.");
            Assert(result.SandboxDeleted, "Workspace dependency staging build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateStagesConfigurationScopedFileDependencyProperties()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string modDirectory = Path.Combine(tempRoot, "BarsGraphicsLikeMod");
            string monoBguiDirectory = Path.Combine(tempRoot, "bGUI", "bin", "MonoRelease", "netstandard2.1");
            string il2CppBguiDirectory = Path.Combine(tempRoot, "bGUI", "bin", "Il2cppRelease", "net6.0");
            Directory.CreateDirectory(modDirectory);
            Directory.CreateDirectory(monoBguiDirectory);
            Directory.CreateDirectory(il2CppBguiDirectory);
            File.Copy(
                typeof(MigrationVerifier).Assembly.Location,
                Path.Combine(monoBguiDirectory, "bGUI.dll"),
                overwrite: true);
            File.Copy(
                typeof(MigrationVerifier).Assembly.Location,
                Path.Combine(il2CppBguiDirectory, "bGUI.dll"),
                overwrite: true);

            string tempProject = Path.Combine(modDirectory, "BarsGraphicsLikeMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Configurations>MonoStable;Il2cppStable</Configurations>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='MonoStable'">
                    <DefineConstants>MONO</DefineConstants>
                    <BguiPath Condition="'$(BguiPath)'==''">..\bGUI\bin\MonoRelease\netstandard2.1\bGUI.dll</BguiPath>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Il2cppStable'">
                    <TargetFramework>net6.0</TargetFramework>
                    <DefineConstants>IL2CPP</DefineConstants>
                    <BguiPath Condition="'$(BguiPath)'==''">..\bGUI\bin\Il2cppRelease\net6.0\bGUI.dll</BguiPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="bGUI">
                      <HintPath>$(BguiPath)</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "Core.cs"),
                """
                namespace BarsGraphicsLikeMod
                {
                    public static class Core
                    {
                        public static int Value => 1;
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, Build: true, BuildTimeoutSeconds: 60));

            Assert(result.Success, $"Configuration-scoped file dependency properties should be staged into the sandbox. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run both runtime configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(
                result.BuildResults!.All(build => build.ReadinessStatus == "Ready"),
                $"Staged configuration-scoped dependencies should pass readiness. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Configuration-scoped dependency staging should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateStagesProjectLocalRuntimeReferenceFolders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string modDirectory = Path.Combine(tempRoot, "RuntimeLibFolderMod");
            string il2CppRoot = Path.Combine(tempRoot, "Schedule I_public");
            string monoRoot = Path.Combine(tempRoot, "Schedule I_alternate");
            string il2CppAssemblies = Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies");
            string il2CppMelonLoader = Path.Combine(il2CppRoot, "MelonLoader", "net6");
            string monoManaged = Path.Combine(monoRoot, "Schedule I_Data", "Managed");
            string monoMelonLoader = Path.Combine(monoRoot, "MelonLoader", "net35");
            Directory.CreateDirectory(modDirectory);
            Directory.CreateDirectory(il2CppAssemblies);
            Directory.CreateDirectory(il2CppMelonLoader);
            Directory.CreateDirectory(monoManaged);
            Directory.CreateDirectory(monoMelonLoader);
            foreach (string destination in new[]
                     {
                         Path.Combine(il2CppAssemblies, "Assembly-CSharp.dll"),
                         Path.Combine(il2CppAssemblies, "UnityEngine.CoreModule.dll"),
                         Path.Combine(il2CppMelonLoader, "MelonLoader.dll"),
                         Path.Combine(monoManaged, "Assembly-CSharp.dll"),
                         Path.Combine(monoManaged, "UnityEngine.CoreModule.dll"),
                         Path.Combine(monoMelonLoader, "MelonLoader.dll")
                     })
            {
                File.Copy(typeof(MigrationVerifier).Assembly.Location, destination, overwrite: true);
            }

            string tempProject = Path.Combine(modDirectory, "RuntimeLibFolderMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Configurations>Mono;IL2CPP</Configurations>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Mono'">
                    <DefineConstants>MONO</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='IL2CPP'">
                    <TargetFramework>net6.0</TargetFramework>
                    <DefineConstants>IL2CPP</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(Configuration)'=='Mono'">
                    <Reference Include="MelonLoader">
                      <HintPath>mono_libs\MelonLoader.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>mono_libs\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="UnityEngine.CoreModule">
                      <HintPath>mono_libs\UnityEngine.CoreModule.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                  <ItemGroup Condition="'$(Configuration)'=='IL2CPP'">
                    <Reference Include="MelonLoader">
                      <HintPath>il2cpp_libs\MelonLoader.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="Assembly-CSharp">
                      <HintPath>il2cpp_libs\Assembly-CSharp.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="UnityEngine.CoreModule">
                      <HintPath>il2cpp_libs\UnityEngine.CoreModule.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "Core.cs"),
                """
                namespace RuntimeLibFolderMod
                {
                    public static class Core
                    {
                        public static int Value => 1;
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(
                    DualRuntime: false,
                    Build: true,
                    BuildTimeoutSeconds: 60,
                    Il2CppGamePath: il2CppRoot,
                    MonoGamePath: monoRoot));

            Assert(result.Success, $"Project-local runtime reference folders should be staged from configured game roots. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.BuildResults?.Count == 2, $"Build verification should run both runtime configurations, got {result.BuildResults?.Count ?? 0}.");
            Assert(result.BuildResults!.All(build => build.ReadinessStatus == "Ready"), $"Staged runtime lib folders should pass readiness. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Project-local runtime reference folder staging should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationBuildGateCollapsesStagedIl2CppWrapperReferences()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string modDirectory = Path.Combine(tempRoot, "Il2CppConsumerMod");
            string il2CppRoot = Path.Combine(tempRoot, "Schedule I_public");
            string il2CppAssemblies = Path.Combine(il2CppRoot, "MelonLoader", "Il2CppAssemblies");
            Directory.CreateDirectory(modDirectory);
            Directory.CreateDirectory(il2CppAssemblies);
            File.Copy(
                typeof(MigrationVerifier).Assembly.Location,
                Path.Combine(il2CppAssemblies, "Il2CppExisting.Dependency.dll"),
                overwrite: true);

            string tempProject = Path.Combine(modDirectory, "Il2CppConsumerMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="local.build.props" Condition="Exists('local.build.props')" />
                  <PropertyGroup>
                    <TargetFramework>net6.0</TargetFramework>
                    <Configurations>IL2CPP</Configurations>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Il2CppExisting.Dependency">
                      <HintPath>$(Il2CppManagedDllPath)\Il2CppExisting.Dependency.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="Il2CppMissing.One">
                      <HintPath>$(Il2CppManagedDllPath)\Il2CppMissing.One.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                    <Reference Include="Il2CppMissing.Two">
                      <HintPath>$(Il2CppManagedDllPath)\Il2CppMissing.Two.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "local.build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <Il2CppManagedDllPath></Il2CppManagedDllPath>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modDirectory, "Core.cs"),
                """
                namespace Il2CppConsumerMod
                {
                    public static class Core
                    {
                        public static int Value => 1;
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(
                    DualRuntime: false,
                    Build: true,
                    BuildTimeoutSeconds: 60,
                    Il2CppGamePath: il2CppRoot));

            Assert(!result.Success, "Missing staged IL2CPP wrapper references should block build verification.");
            MigrationBuildResult build = result.BuildResults!.Single();
            Assert(build.FailureKind == "GeneratedIl2CppAssembliesMissing", $"Expected generated wrapper failure, got {FormatBuildResults(result.BuildResults)}");
            Assert(build.Issues.Count == 1, $"Staged generated wrapper misses should collapse into one issue. Build output: {FormatBuildResults(result.BuildResults)}");
            MigrationBuildIssue issue = build.Issues[0];
            Assert(issue.Include == "2 references", $"Collapsed issue should summarize the missing reference count. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                issue.Message.Contains("Il2CppMissing.One", StringComparison.Ordinal) &&
                issue.Message.Contains("Il2CppMissing.Two", StringComparison.Ordinal),
                $"Collapsed issue should include sample missing reference names. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(
                issue.Path?.Contains(@"S1Interop.ExternalReferences\Il2CppManagedDllPath", StringComparison.OrdinalIgnoreCase) == true,
                $"Collapsed issue should group by the staged IL2CPP overlay directory. Build output: {FormatBuildResults(result.BuildResults)}");
            Assert(result.SandboxDeleted, "Staged IL2CPP wrapper build verification should delete its sandbox.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationVerifierSkipsWindowsReservedDeviceNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert(MigrationVerifier.IsReservedWindowsDevicePath(@"C:\Mods\SchizoGoblinMod\nul"), "Windows NUL device files should be skipped.");
        Assert(MigrationVerifier.IsReservedWindowsDevicePath(@"C:\Mods\SchizoGoblinMod\NUL.txt"), "Windows NUL extension aliases should be skipped.");
        Assert(MigrationVerifier.IsReservedWindowsDevicePath(@"C:\Mods\SchizoGoblinMod\COM1.log"), "Windows COM device aliases should be skipped.");
        Assert(MigrationVerifier.IsReservedWindowsDevicePath(@"C:\Mods\SchizoGoblinMod\LPT9"), "Windows LPT device aliases should be skipped.");
        Assert(!MigrationVerifier.IsReservedWindowsDevicePath(@"C:\Mods\SchizoGoblinMod\null.txt"), "Ordinary files with similar names should not be skipped.");
        Assert(!MigrationVerifier.IsReservedWindowsDevicePath(@"C:\Mods\SchizoGoblinMod\COM10.log"), "Only DOS COM1-COM9 device aliases should be skipped.");
    }

    private void MigrationApplyReplacesStalePublicizedReferenceWithPublicizer()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, "BiggerLobbies");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "BiggerLobbies.csproj");
            string originalProject = File.ReadAllText(tempProject);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true)).Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "stale_publicized_surface"),
                "BiggerLobbies should plan stale publicized-surface migration.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlan(tempProject, [projectPlan]));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "stale_publicized_surface"),
                "Migration apply should rewrite stale publicized references.");

            string migratedProject = File.ReadAllText(tempProject);
            Assert(
                !migratedProject.Contains("Assembly-CSharp-publicized", StringComparison.OrdinalIgnoreCase),
                "Migrated project should not keep stale Assembly-CSharp-publicized references.");
            Assert(
                migratedProject.Contains("PackageReference Include=\"Krafs.Publicizer\"", StringComparison.Ordinal),
                "Migrated project should add Krafs.Publicizer for build-time publicization.");
            Assert(
                migratedProject.Contains("Publicize Include=\"Assembly-CSharp\"", StringComparison.Ordinal),
                "Migrated project should publicize the current Assembly-CSharp reference at build time.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "stale_publicized_surface"),
                "Migrated BiggerLobbies fixture should not retain stale publicized-surface diagnostics.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the BiggerLobbies project file.");
            Assert(
                string.Equals(File.ReadAllText(tempProject), originalProject, StringComparison.Ordinal),
                "Rollback should restore the original publicized reference shape.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationReportsResidualDiagnosticsOnBrokenInjectedType()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "BrokenInjectedType.csproj");
            string tempSource = Path.Combine(tempRoot, "BrokenComponent.cs");
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
                namespace SyntheticMod;

                [MelonLoader.RegisterTypeInIl2Cpp]
                public class BrokenComponent
                {
                    public BrokenComponent()
                    {
                    }
                }
                """);

            MigrationVerificationResult result = new MigrationVerifier().Verify(tempProject);

            Assert(!result.Success, "Broken injected type verify-migration should fail with residual diagnostics.");
            Assert(result.SandboxDeleted, "Broken injected type verify-migration should delete its sandbox.");
            Assert(
                result.AfterDiagnostics.Any(diagnostic => diagnostic.RuleId == "injected_type_missing_intptr_constructor"),
                "Broken injected type verify-migration should report the residual IntPtr constructor diagnostic.");
            Assert(File.Exists(tempProject), "verify-migration should not mutate or delete the source project under test.");
            Assert(File.Exists(tempSource), "verify-migration should not mutate or delete the source file under test.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAddsIntPtrConstructorToMonoBehaviourInjectedType()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "InjectedOverlay.csproj");
            string tempSource = Path.Combine(tempRoot, "Overlay.cs");
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
                namespace UnityEngine
                {
                    public class MonoBehaviour
                    {
                        public MonoBehaviour()
                        {
                        }

                        public MonoBehaviour(System.IntPtr ptr)
                        {
                        }
                    }
                }

                namespace SyntheticMod;

                [MelonLoader.RegisterTypeInIl2Cpp]
                public class Overlay : UnityEngine.MonoBehaviour
                {
                    public Overlay()
                    {
                    }

                    public void Render()
                    {
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "injected_type_missing_intptr_constructor"),
                "Injected MonoBehaviour fixture should plan an IntPtr constructor migration.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlan(tempProject, [projectPlan]));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "injected_type_missing_intptr_constructor"),
                "Migration apply should add the IntPtr constructor.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("#if IL2CPP", StringComparison.Ordinal) &&
                migratedSource.Contains("public Overlay(System.IntPtr ptr) : base(ptr) { }", StringComparison.Ordinal),
                "Migrated source should contain a guarded System.IntPtr constructor.");
            Assert(
                migratedSource.Contains("public Overlay()", StringComparison.Ordinal),
                "Migrated source should preserve the existing parameterless constructor.");
            Assert(
                !migratedSource.Contains("ClassInjector.DerivedConstructorPointer<Overlay>()", StringComparison.Ordinal),
                "Migration should not add a duplicate managed-instantiation constructor when one already exists.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "injected_type_missing_intptr_constructor"),
                "Migrated injected MonoBehaviour fixture should not retain IntPtr constructor diagnostics.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the injected overlay source file.");
            Assert(
                !File.ReadAllText(tempSource).Contains("public Overlay(System.IntPtr ptr)", StringComparison.Ordinal),
                "Rollback should remove the generated IntPtr constructor.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyRegistersMonoOnlyInjectedComponentTypes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MonoOnlyInjectedComponent.csproj");
            string tempSource = Path.Combine(tempRoot, "EquippableGasolineCan.cs");
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
                namespace ScheduleOne.Equipping
                {
                    public class Equippable
                    {
                        public Equippable()
                        {
                        }

                        public Equippable(System.IntPtr ptr)
                        {
                        }
                    }
                }

                namespace SyntheticMod;

                public class Equippable_GasolineCan : ScheduleOne.Equipping.Equippable
                {
                    public void BeginRefuelInteraction()
                    {
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "injected_type_missing_registertype"),
                "Mono-only Equippable fixture should plan a RegisterTypeInIl2Cpp migration.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "injected_type_missing_intptr_constructor"),
                "Mono-only Equippable fixture should plan an IntPtr constructor migration.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlan(tempProject, [projectPlan]));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "injected_type_missing_registertype"),
                "Migration apply should add the RegisterTypeInIl2Cpp attribute.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "injected_type_missing_intptr_constructor"),
                "Migration apply should add the IntPtr constructor.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("[MelonLoader.RegisterTypeInIl2Cpp]", StringComparison.Ordinal),
                "Migrated source should contain a guarded RegisterTypeInIl2Cpp attribute.");
            Assert(
                migratedSource.Contains("public Equippable_GasolineCan(System.IntPtr ptr) : base(ptr) { }", StringComparison.Ordinal),
                "Migrated source should contain a guarded System.IntPtr constructor.");
            Assert(
                migratedSource.Contains(
                    "public Equippable_GasolineCan() : base(Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<Equippable_GasolineCan>())",
                    StringComparison.Ordinal) &&
                migratedSource.Contains(
                    "Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);",
                    StringComparison.Ordinal),
                "Migrated source should contain a guarded managed-instantiation constructor for IL2CPP.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            ProjectMigrationPlan secondPlan = new MigrationPlanner().Plan(after).Projects.Single();
            Assert(
                secondPlan.Operations.All(operation =>
                    operation.RuleId != "injected_type_missing_registertype" &&
                    operation.RuleId != "injected_type_missing_intptr_constructor"),
                "A second migration plan should not duplicate injected type registration or constructor operations.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the mono-only injected component source file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void BuildHookInstallsReversibleValidationTarget()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "CleanBuildHook.csproj");
            string tempSource = Path.Combine(tempRoot, "Core.cs");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                tempSource,
                """
                namespace CleanBuildHook;

                public static class Core
                {
                    public static int Value => 42;
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(
                before,
                new MigrationPlannerOptions(DualRuntime: false, BuildHook: true));
            Assert(
                plan.Projects.Single().Operations.Any(operation => operation.RuleId == "install_build_validation_hook"),
                "Expected build hook migration operation for clean synthetic fixture.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            string targetsPath = Path.Combine(tempRoot, "S1Interop.Build.targets");
            string localPropsPath = Path.Combine(tempRoot, "S1Interop.Build.local.props");
            string gitIgnorePath = Path.Combine(tempRoot, ".gitignore");
            Assert(File.Exists(targetsPath), "Build hook apply should create S1Interop.Build.targets.");
            Assert(File.Exists(localPropsPath), "Build hook apply should create ignored local command props.");
            Assert(File.Exists(gitIgnorePath), "Build hook apply should create a .gitignore for local command props.");
            Assert(File.ReadAllText(tempProject).Contains("S1Interop.Build.targets", StringComparison.Ordinal), "Build hook apply should import S1Interop.Build.targets.");
            Assert(
                File.ReadAllText(targetsPath).Contains("S1Interop validating $(MSBuildProjectFile)", StringComparison.Ordinal),
                "Build hook target should include the validation message.");
            Assert(
                File.ReadAllText(targetsPath).Contains("BeforeTargets=\"ResolveReferences\"", StringComparison.Ordinal),
                "Build hook target should run before reference resolution.");
            Assert(
                File.ReadAllText(targetsPath).Contains("--configuration &quot;$(Configuration)&quot;", StringComparison.Ordinal),
                "Build hook target should validate the active MSBuild configuration.");
            Assert(
                File.ReadAllText(targetsPath).Contains("<S1InteropCommand Condition=\"'$(S1InteropCommand)' == ''\">s1interop</S1InteropCommand>", StringComparison.Ordinal),
                "Build hook target should keep the committed command default portable.");
            Assert(
                File.ReadAllText(localPropsPath).Contains("dotnet run --project", StringComparison.Ordinal),
                "Local command props should point at the local S1Interop CLI project.");
            Assert(
                File.ReadAllLines(gitIgnorePath).Any(line => string.Equals(line.Trim(), "S1Interop.Build.local.props", StringComparison.OrdinalIgnoreCase)),
                "Build hook apply should ignore local command props.");

            MigrationApplyResult idempotentApplyResult = new MigrationApplier().Apply(new MigrationPlanner().Plan(
                analyzer.Analyze(tempProject),
                new MigrationPlannerOptions(DualRuntime: false, BuildHook: true)));
            Assert(idempotentApplyResult.Operations.Count == 0, "Repeated build hook apply should not report changed operations.");
            Assert(
                CountProjectImports(tempProject, "S1Interop.Build.targets") == 1,
                "Repeated build hook apply should not duplicate the project import.");

            ProcessResult buildResult = RunDotNet("build", tempProject, "--nologo", "-v:minimal");
            Assert(buildResult.ExitCode == 0, $"Clean project with S1Interop build hook should build. Output: {buildResult.Output}");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(ContainsPath(rollbackResult.RestoredFiles, tempProject), $"Build hook rollback should restore the project file. Restored: {string.Join(", ", rollbackResult.RestoredFiles)} Removed: {string.Join(", ", rollbackResult.RemovedFiles)}");
            Assert(ContainsPath(rollbackResult.RemovedFiles, targetsPath), "Build hook rollback should remove S1Interop.Build.targets.");
            Assert(ContainsPath(rollbackResult.RemovedFiles, localPropsPath), "Build hook rollback should remove local command props.");
            Assert(ContainsPath(rollbackResult.RemovedFiles, gitIgnorePath), "Build hook rollback should remove generated .gitignore.");
            Assert(!File.Exists(targetsPath), "Build hook rollback should delete S1Interop.Build.targets.");
            Assert(!File.Exists(localPropsPath), "Build hook rollback should delete local command props.");
            Assert(!File.Exists(gitIgnorePath), "Build hook rollback should delete generated .gitignore.");
            Assert(
                !File.ReadAllText(tempProject).Contains("S1Interop.Build.targets", StringComparison.Ordinal),
                "Build hook rollback should remove the project import.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void BuildHookFailsBuildForResidualInteropDiagnostics()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "BrokenBuildHook.csproj");
            string tempSource = Path.Combine(tempRoot, "BrokenComponent.cs");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                tempSource,
                """
                namespace MelonLoader
                {
                    [System.AttributeUsage(System.AttributeTargets.Class)]
                    public sealed class RegisterTypeInIl2CppAttribute : System.Attribute
                    {
                    }
                }

                namespace BrokenBuildHook
                {
                    [MelonLoader.RegisterTypeInIl2Cpp]
                    public sealed class BrokenComponent
                    {
                        public BrokenComponent()
                        {
                        }
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(
                before,
                new MigrationPlannerOptions(DualRuntime: false, BuildHook: true));
            new MigrationApplier().Apply(plan);

            ProcessResult buildResult = RunDotNet("build", tempProject, "--nologo", "-v:minimal");
            Assert(buildResult.ExitCode != 0, "Broken project with S1Interop build hook should fail the build.");
            Assert(
                buildResult.Output.Contains("injected_type_missing_intptr_constructor", StringComparison.Ordinal),
                $"Build hook failure should include the residual S1Interop diagnostic. Output: {buildResult.Output}");

            ProcessResult disabledBuildResult = RunDotNet(
                "build",
                tempProject,
                "--nologo",
                "-v:minimal",
                "-p:S1InteropBuildValidationEnabled=false");
            Assert(disabledBuildResult.ExitCode == 0, $"Disabled S1Interop build validation should let the synthetic project compile. Output: {disabledBuildResult.Output}");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void BuildHookValidatesOnlyActiveConfiguration()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "MixedConfigBuildHook.csproj");
            string tempSource = Path.Combine(tempRoot, "Core.cs");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Configurations>Mono;IL2CPP</Configurations>
                    <LangVersion>10.0</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Mono'">
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <DefineConstants>MONO</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='IL2CPP'">
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <DefineConstants>IL2CPP</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                tempSource,
                """
                namespace MixedConfigBuildHook;

                public static class Core
                {
                    public static int Value => 42;
                }
                """);

            MigrationPlan plan = new MigrationPlanner().Plan(
                analyzer.Analyze(tempProject),
                new MigrationPlannerOptions(DualRuntime: false, BuildHook: true));
            MigrationPlan hookOnlyPlan = plan with
            {
                Projects = plan.Projects
                    .Select(project => project with
                    {
                        Operations = project.Operations
                            .Where(operation => operation.RuleId == "install_build_validation_hook")
                            .ToArray()
                    })
                    .ToArray()
            };
            new MigrationApplier().Apply(hookOnlyPlan);

            ProcessResult monoBuild = RunDotNet("build", tempProject, "--nologo", "-v:minimal", "-p:Configuration=Mono");
            Assert(monoBuild.ExitCode == 0, $"Mono build should ignore unrelated IL2CPP diagnostics. Output: {monoBuild.Output}");

            ProcessResult il2CppBuild = RunDotNet("build", tempProject, "--nologo", "-v:minimal", "-p:Configuration=IL2CPP");
            Assert(il2CppBuild.ExitCode != 0, $"IL2CPP build should fail its own active configuration diagnostics. Output: {il2CppBuild.Output}");
            Assert(
                il2CppBuild.Output.Contains("wrong_target_framework", StringComparison.Ordinal),
                $"IL2CPP build should report the active configuration diagnostic. Output: {il2CppBuild.Output}");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void SourceInteropAnalyzerIgnoresGeneratedAndToolDirectories()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ExcludedSources.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                namespace ExcludedSources;

                public static class Core
                {
                    public static int Value => 42;
                }
                """);

            foreach (string directoryName in new[] { "artifacts", "AssetRipperExport", "Il2CppAssemblies", "MelonLoader" })
            {
                string directory = Path.Combine(tempRoot, directoryName);
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, directoryName == "artifacts" ? "Il2CppScheduleOne.Core.decompiled.cs" : "Poison.cs"),
                    """
                    namespace ExcludedSources;

                    [MelonLoader.RegisterTypeInIl2Cpp]
                    public sealed class Poison
                    {
                    }
                    """);
            }

            SourceInteropAnalysis source = new SourceInteropAnalyzer().Analyze(tempProject);

            Assert(source.Diagnostics.Count == 0, "Source interop analyzer should ignore poison files under generated/tool directories.");
            Assert(source.InjectedTypes.Count == 0, "Source interop analyzer should not report injected types under generated/tool directories.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }
}
