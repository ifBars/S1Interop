internal sealed partial class S1InteropFixtureTests
{
    private void CliVersionPrintsPackageVersionWithoutAnalyzingWorkspace()
    {
        ProcessResult version = RunCli("--version");

        Assert(version.ExitCode == 0, $"s1interop --version should succeed. Output: {version.Output}");
        Assert(
            version.Output.Contains("S1Interop", StringComparison.Ordinal) &&
            version.Output.Contains(S1InteropPackageInfo.CliPackageVersion, StringComparison.Ordinal),
            $"s1interop --version should print the package version. Output: {version.Output}");
    }

    private void CliRejectsInvalidOptionsBeforeDispatch()
    {
        ProcessResult unknownOption = RunCli("analyze", "--aply");
        Assert(
            unknownOption.ExitCode == 2 &&
            unknownOption.Output.Contains("Unknown option '--aply'", StringComparison.Ordinal),
            $"Unknown CLI options should fail before analysis or migration runs. Output: {unknownOption.Output}");

        ProcessResult missingValue = RunCli("verify-migration", "--build-timeout-seconds");
        Assert(
            missingValue.ExitCode == 2 &&
            missingValue.Output.Contains("Missing value for --build-timeout-seconds", StringComparison.Ordinal),
            $"Options that require values should fail when the value is missing. Output: {missingValue.Output}");

        ProcessResult invalidFormat = RunCli("analyze", "--format", "xml");
        Assert(
            invalidFormat.ExitCode == 2 &&
            invalidFormat.Output.Contains("Invalid value for --format", StringComparison.Ordinal),
            $"Invalid format values should fail before command dispatch. Output: {invalidFormat.Output}");

        ProcessResult dryRun = RunCli("init", "--dry-run", "--path", RepositoryRoot);
        Assert(
            dryRun.ExitCode == 0 &&
            dryRun.Output.Contains("S1Interop migration dry-run", StringComparison.Ordinal),
            $"The documented --dry-run flag should be accepted as the default non-apply mode. Output: {dryRun.Output}");
    }

    private void CliHelpUsageLinesAreDocumented()
    {
        ProcessResult help = RunCli("--help");
        Assert(help.ExitCode == 0, $"s1interop --help should succeed. Output: {help.Output}");

        string commandReference = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "docfx", "articles", "commands.md"));
        foreach (string usageLine in GetHelpUsageLines(help.Output))
        {
            Assert(
                commandReference.Contains(usageLine, StringComparison.Ordinal),
                $"Command reference should document CLI usage line '{usageLine}'.");
        }
    }

    private void PackageInfoMatchesPackageProjects()
    {
        string cliProject = Path.Combine(RepositoryRoot, "src", "S1Interop.Cli", "S1Interop.Cli.csproj");
        string generatorProject = Path.Combine(RepositoryRoot, "src", "S1Interop.Generators", "S1Interop.Generators.csproj");
        XDocument cliDocument = XDocument.Load(cliProject);
        XDocument generatorDocument = XDocument.Load(generatorProject);
        XElement cliPropertyGroup = cliDocument.Root!.Elements().First(element => element.Name.LocalName == "PropertyGroup");
        XElement generatorPropertyGroup = generatorDocument.Root!.Elements().First(element => element.Name.LocalName == "PropertyGroup");

        Assert(
            string.Equals(cliPropertyGroup.Element("PackageId")?.Value, S1InteropPackageInfo.CliPackageId, StringComparison.Ordinal),
            "S1InteropPackageInfo.CliPackageId should match the CLI package project.");
        Assert(
            string.Equals(cliPropertyGroup.Element("Version")?.Value, S1InteropPackageInfo.CliPackageVersion, StringComparison.Ordinal),
            "S1InteropPackageInfo.CliPackageVersion should match the CLI package project.");

        Assert(
            string.Equals(generatorPropertyGroup.Element("PackageId")?.Value, S1InteropPackageInfo.GeneratorsPackageId, StringComparison.Ordinal),
            "S1InteropPackageInfo.GeneratorsPackageId should match the generator package project.");
        Assert(
            string.Equals(generatorPropertyGroup.Element("Version")?.Value, S1InteropPackageInfo.GeneratorsPackageVersion, StringComparison.Ordinal),
            "S1InteropPackageInfo.GeneratorsPackageVersion should match the generator package project.");
        Assert(
            S1InteropPackageInfo.CreateLocalGeneratorsPackageVersion(new DateTimeOffset(2026, 7, 4, 1, 2, 3, 456, TimeSpan.Zero))
                .StartsWith($"{S1InteropPackageInfo.GeneratorsPackageVersion}.local.", StringComparison.Ordinal),
            "Local generator package versions should be derived from the public generator package version.");
    }

    private static IEnumerable<string> GetHelpUsageLines(string helpOutput)
    {
        foreach (string line in helpOutput.Split([Environment.NewLine], StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("s1interop ", StringComparison.Ordinal))
            {
                yield return trimmed;
            }
        }
    }

    private void AlwaysJackpotHasDualRuntimeShapeAndIl2CppFrameworkDiagnostic()
    {
        ProjectAnalysis project = AnalyzeProject(@"AlwaysJackpot\AlwaysJackpot.csproj");

        AssertHasRuntime(project, "Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "IL2CPP", RuntimeKind.Il2Cpp);
        AssertHasDiagnostic(project, "wrong_target_framework", "IL2CPP");
    }

    private void JackpotEveryTimeReportsManagedIl2CppSurface()
    {
        ProjectAnalysis project = AnalyzeProject(@"JackpotEveryTime\JackpotEveryTime.csproj");

        AssertHasRuntime(project, "Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "Il2Cpp", RuntimeKind.Il2Cpp);
        AssertHasDiagnostic(project, "wrong_il2cpp_reference_surface", "Il2Cpp");
        AssertHasDiagnostic(project, "wrong_target_framework", "Il2Cpp");
    }

    private void GunsAlwaysAccurateIsRecognizedAsCleanDualRuntimeBaseline()
    {
        ProjectAnalysis project = AnalyzeProject(@"GunsAlwaysAccurate\GunsAlwaysAccurate.csproj");

        AssertHasRuntime(project, "Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "IL2CPP", RuntimeKind.Il2Cpp);

        bool hasIl2CppFrameworkError = project.Diagnostics.Any(diagnostic =>
            diagnostic.RuleId == "wrong_target_framework" &&
            diagnostic.Configuration == "IL2CPP" &&
            diagnostic.Severity == CoreDiagnosticSeverity.Error);
        Assert(!hasIl2CppFrameworkError, "GunsAlwaysAccurate IL2CPP config should not report wrong_target_framework.");
    }

    private void DedicatedServerModEffectiveConfigurationConditionsAreEvaluated()
    {
        ProjectAnalysis project = AnalyzeProject(@"DedicatedServerMod\DedicatedServerMod.csproj");

        AssertHasRuntime(project, "Mono_Client", RuntimeKind.Mono);
        AssertHasRuntime(project, "Mono_Server", RuntimeKind.Mono);
        AssertHasRuntime(project, "Il2cpp_Client", RuntimeKind.Il2Cpp);
        AssertHasRuntime(project, "Il2cpp_Server", RuntimeKind.Il2Cpp);
        AssertHasTargetFramework(project, "Mono_Client", "netstandard2.1");
        AssertHasTargetFramework(project, "Mono_Server", "netstandard2.1");
        AssertHasTargetFramework(project, "Il2cpp_Client", "net6.0");
        AssertHasTargetFramework(project, "Il2cpp_Server", "net6.0");
        Assert(
            project.Diagnostics.All(diagnostic => diagnostic.RuleId != "wrong_target_framework"),
            "DedicatedServerMod EffectiveConfiguration conditions should not produce false target-framework diagnostics.");
    }

    private void OverTheCounterStartsWithConditionsAreEvaluated()
    {
        ProjectAnalysis project = AnalyzeProject(@"snl-consumers\OTC-S1-Mod\OverTheCounter\OverTheCounter.csproj");

        AssertHasRuntime(project, "Debug", RuntimeKind.Il2Cpp);
        AssertHasRuntime(project, "Release", RuntimeKind.Il2Cpp);
        AssertHasRuntime(project, "MonoDebug", RuntimeKind.Mono);
        AssertHasRuntime(project, "MonoRelease", RuntimeKind.Mono);
        AssertHasTargetFramework(project, "Debug", "net6.0");
        AssertHasTargetFramework(project, "Release", "net6.0");
        AssertHasTargetFramework(project, "MonoDebug", "netstandard2.1");
        AssertHasTargetFramework(project, "MonoRelease", "netstandard2.1");
        Assert(
            project.Diagnostics.All(diagnostic => diagnostic.RuleId != "wrong_target_framework"),
            "OverTheCounter StartsWith conditions should not produce false target-framework diagnostics.");
    }

    private void OverTheCounterReportsHarmonyTranspilerRisk()
    {
        ProjectAnalysis project = AnalyzeProject(@"snl-consumers\OTC-S1-Mod\OverTheCounter\OverTheCounter.csproj");

        Assert(
            project.SourceInterop?.SourceRisks.Any(risk =>
                risk.Kind == "HarmonyTranspiler" &&
                risk.FilePath.EndsWith(@"Patches\NpcTypeDiscoveryPatch.cs", StringComparison.OrdinalIgnoreCase)) == true,
            "OverTheCounter should report its NpcTypeDiscoveryPatch transpiler as an IL2CPP source risk.");
    }

    private void SourceInteropAnalyzerReportsIl2CppSourceRisks()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "RiskySourceMod.csproj");
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
                Path.Combine(tempRoot, "Risky.cs"),
                """
                using System;
                using System.Collections.Generic;
                using HarmonyLib;
                using UnityEngine.UI;

                namespace RiskySourceMod;

                public static class Risky
                {
                    public static void Bind(Button button)
                    {
                        button.onClick.AddListener(OnClicked);
                        SomeEvent = (Action)Delegate.Combine(SomeEvent, new Action(OnClicked));
                        GUI.Window(0, windowRect, DrawWindow, "");
                        SteamNetworking.ReadP2PPacket(data, packetSize, out bytesRead, out remoteId, channel);
                        string? messageType = MiniMessageSerializer.GetMessageType(data);
                        var nativeNames = vehicleData.Names ?? new Il2CppSystem.Collections.Generic.List<string>();
                        if (pipelineAsset is UniversalRenderPipelineAsset typedAsset)
                        {
                            _ = typedAsset;
                        }
                    }

                    public static void BindInternalPostfix(object __instance,
                        List<EntityConfiguration> configs)
                    {
                    }

                    [HarmonyTranspiler]
                    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => instructions;

                    private static event Action? SomeEvent;
                    private static byte[] data = [];
                    private static uint packetSize;
                    private static uint bytesRead;
                    private static object remoteId = new();
                    private static int channel;
                    private static VehicleData vehicleData = new();
                    private static object windowRect = new();
                    private static object pipelineAsset = new();
                    private static void OnClicked() { }
                    private static void DrawWindow(int id) { }

                    private static class MiniMessageSerializer
                    {
                        public static string? GetMessageType(byte[] data) => data.Length == 0 ? null : "fuel";
                    }

                    private sealed class VehicleData
                    {
                        public Il2CppSystem.Collections.Generic.List<string>? Names { get; set; }
                    }
                }

                namespace Il2CppSystem.Collections.Generic
                {
                    public sealed class List<T>
                    {
                    }
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceInteropAnalysis source = project.SourceInterop!;

            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "HarmonyTranspiler"),
                "Source analyzer should report Harmony transpiler risk.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "DirectUnityEventListener"),
                "Source analyzer should report direct UnityEvent listener risk.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "DirectDelegateCombine"),
                "Source analyzer should report direct delegate combine risk.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "DirectDelegateArgumentInterop"),
                "Source analyzer should report direct delegate argument interop risk.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "Il2CppByteBufferInterop"),
                "Source analyzer should report IL2CPP byte buffer marshalling risk.");
            Assert(
                source.SourceRisks.Where(risk => risk.Kind == "Il2CppByteBufferInterop").All(risk => !risk.Evidence.Contains("GetMessageType", StringComparison.Ordinal)),
                "Source analyzer should not treat byte[] serializer/parser getters as native byte-buffer fill calls.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "ManagedCollectionSignatureInterop"),
                "Source analyzer should report managed collection signature interop risk.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "Il2CppListNullCoalesce"),
                "Source analyzer should report IL2CPP list null-coalescing risk.");
            Assert(
                source.SourceRisks.Any(risk => risk.Kind == "Il2CppObjectCastInterop"),
                "Source analyzer should report IL2CPP object cast risk.");

            MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(tempProject, [project]));
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "generate_unity_event_bridge" &&
                    operation.Automatic),
                "Migration plan should generate a UnityEvent bridge for safely rewritable listener risks.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "rewrite_unity_event_listeners" &&
                    operation.Automatic),
                "Migration plan should rewrite safely rewritable UnityEvent listener risks.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "rewrite_delegate_arguments" &&
                    operation.Automatic),
                "Migration plan should rewrite safely rewritable direct delegate argument risks.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "source_risk_harmony_transpiler" &&
                    !operation.Automatic),
                "Migration plan should surface Harmony transpiler risks as non-automatic guidance.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "source_risk_il2_cpp_byte_buffer_interop" &&
                    !operation.Automatic),
                "Migration plan should surface IL2CPP byte buffer risks as non-automatic guidance.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "source_risk_managed_collection_signature_interop" &&
                    !operation.Automatic),
                "Migration plan should surface managed collection signature risks as non-automatic guidance.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "rewrite_il2cpp_list_null_coalescing" &&
                    operation.Automatic),
                "Migration plan should rewrite IL2CPP list null-coalescing risks automatically.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "rewrite_il2cpp_object_casts" &&
                    operation.Automatic),
                "Migration plan should rewrite simple IL2CPP object cast risks through the generated object cast helper.");
            Assert(
                plan.Projects.Single().Operations.Any(operation =>
                    operation.RuleId == "install_s1interop_generator_package" &&
                    operation.Automatic),
                "Migration plan should install the S1Interop generator package when rewriting object casts through generated helpers.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void SourceInteropAnalyzerReportsHarmonyOverloadBindingRisk()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "HarmonyBindingMod.csproj");
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
                Path.Combine(tempRoot, "MoveItemPatch.cs"),
                """
                using HarmonyLib;
                using ScheduleOne.ItemFramework;
                using ScheduleOne.Management;
                using ScheduleOne.NPCs.Behaviour;

                namespace HarmonyBindingMod;

                public static class MoveItemPatch
                {
                    public static void Patch(Harmony harmony)
                    {
                        var method = AccessTools.Method(
                            typeof(MoveItemBehaviour),
                            nameof(MoveItemBehaviour.IsDestinationValid),
                            [
                                typeof(TransitRoute),
                                typeof(ItemInstance),
                                typeof(string).MakeByRefType()
                            ]
                        );

                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(MoveItemPatch), nameof(Prefix)));
                    }

                    private static bool Prefix(ref string invalidReason) => true;
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceRisk[] risks = project.SourceInterop!.SourceRisks.ToArray();
            Assert(
                risks.Any(risk =>
                    risk.Kind == "HarmonyOverloadBinding" &&
                    risk.Remediation.Contains("S1InteropMember", StringComparison.Ordinal) &&
                    risk.Remediation.Contains("ParameterTypeNames", StringComparison.Ordinal)),
                "Source analyzer should report AccessTools.Method overload binding as generated method-target guidance.");

            MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(tempProject, [project]));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "generate_harmony_method_targets" &&
                    operation.Automatic),
                "Migration plan should generate Harmony method target attributes for safely parsed overload bindings.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "rewrite_harmony_overload_bindings" &&
                    operation.Automatic),
                "Migration plan should rewrite safely parsed Harmony overload bindings.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void SourceInteropAnalyzerReportsFieldPropertyReflectionFallbackRisk()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ReflectionFallbackMod.csproj");
            string tempSource = Path.Combine(tempRoot, "ReflectionFallback.cs");
            string targetPath = Path.Combine(tempRoot, "S1Interop.Generated", MemberAccessTargetGenerator.SourceFileName);
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
                tempSource,
                """
                using System.Reflection;
                using GameOffenceNotice = ScheduleOne.UI.OffenceNoticeUI;

                namespace ReflectionFallbackMod;

                public static class ReflectionFallback
                {
                    public static GameObject? GetNoticeContainer(GameOffenceNotice notice)
                    {
                        FieldInfo? field = typeof(GameOffenceNotice).GetField("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null && field.GetValue(notice) is GameObject fieldValue)
                        {
                            return fieldValue;
                        }

                        PropertyInfo? property = typeof(GameOffenceNotice).GetProperty("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        return property == null ? null : property.GetValue(notice) as GameObject;
                    }

                    public static object? ReadContainer(object notice)
                    {
                        FieldInfo? field = notice.GetType().GetField("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            return field.GetValue(notice);
                        }

                        PropertyInfo? property = notice.GetType().GetProperty("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        return property?.GetValue(notice);
                    }

                    public static object? ReadPhoneScreen(ScheduleOne.DevUtilities.Phone phone)
                    {
                        FieldInfo? field = phone.GetType().GetField("homeScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            return field.GetValue(phone);
                        }

                        PropertyInfo? property = phone.GetType().GetProperty("homeScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        return property?.GetValue(phone);
                    }

                    public static object? ReadRenderScale(object pipeline)
                    {
                        PropertyInfo? property = pipeline.GetType().GetProperty("renderScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (property != null)
                        {
                            return property.GetValue(pipeline);
                        }

                        FieldInfo? field = pipeline.GetType().GetField("m_RenderScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        return field?.GetValue(pipeline);
                    }

                    public static string? ReadMelonFilePath(object fileObj)
                    {
                        FieldInfo? field = fileObj.GetType().GetField("FilePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            return field.GetValue(fileObj) as string;
                        }

                        PropertyInfo? property = fileObj.GetType().GetProperty("FilePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        return property?.GetValue(fileObj) as string;
                    }

                    public static object? RuntimeSpecific(object notice)
                    {
                #if IL2CPP
                        FieldInfo? field = notice.GetType().GetField("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        PropertyInfo? property = notice.GetType().GetProperty("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        return property?.GetValue(notice) ?? field?.GetValue(notice);
                #else
                        return null;
                #endif
                    }
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceRisk[] risks = project.SourceInterop!.SourceRisks.ToArray();
            Assert(
                risks.Count(risk => risk.Kind == "FieldPropertyReflectionFallback") == 2,
                $"Source analyzer should report typed reflection fallbacks while ignoring runtime-guarded and arbitrary fixed-name object fallbacks. Risks:{Environment.NewLine}{string.Join(Environment.NewLine, risks.Where(risk => risk.Kind == "FieldPropertyReflectionFallback").Select(risk => risk.Evidence))}");
            Assert(
                risks.Where(risk => risk.Kind == "FieldPropertyReflectionFallback").All(risk => !risk.Evidence.Contains("FilePath", StringComparison.Ordinal)),
                "Reflection fallback risk should not report fixed-name fallback against an arbitrary object receiver such as MelonPreferences internals.");
            Assert(
                risks.Where(risk => risk.Kind == "FieldPropertyReflectionFallback").All(risk => risk.Remediation.Contains("S1InteropMember", StringComparison.Ordinal)),
                "Reflection fallback risk should point developers toward generated S1InteropMember accessors.");

            MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(tempProject, [project]));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "rewrite_member_access_fallbacks" &&
                    operation.Automatic),
                "Migration plan should automatically rewrite simple typed field/property fallback getters.");
            Assert(
                !projectPlan.Operations.Any(operation => operation.RuleId == "source_risk_field_property_reflection_fallback"),
                "Automatically rewritable field/property fallback risks should not remain as manual source-risk guidance.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "generate_member_access_targets" &&
                    operation.Automatic),
                "Migration plan should generate member-access target attributes for typed field/property reflection fallbacks.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "rewrite_member_access_fallbacks" &&
                    operation.Automatic),
                "Migration plan should rewrite simple typed field/property fallback getters.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "install_s1interop_generator_package" &&
                    operation.Automatic),
                "Migration plan should install the S1Interop generator package when member-access target attributes are generated.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "generate_member_access_targets"),
                "Migration apply should create generated member-access target declarations.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_member_access_fallbacks"),
                "Migration apply should rewrite simple typed member-access fallback getters.");
            Assert(
                applyResult.Operations.All(operation => operation.RuleId != "generate_source_risk_report"),
                "Migration apply should not create a source-risk report when all reflection fallback risks are automatically handled.");
            Assert(File.Exists(targetPath), "Generated member-access target declarations were not written.");

            string generatedTargets = File.ReadAllText(targetPath);
            Assert(
                generatedTargets.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.UI.OffenceNoticeUI\", Alias = \"GameOffenceNotice\")]", StringComparison.Ordinal) &&
                generatedTargets.Contains("[assembly: S1Interop.S1InteropMember(\"GameOffenceNotice\", \"container\", Alias = \"container\")]", StringComparison.Ordinal),
                "Generated member-access targets should include typed owner/member declarations.");

        string migratedSource = File.ReadAllText(tempSource);
        Assert(
                migratedSource.Contains("return S1Interop.ScheduleOne.UI.OffenceNoticeUI.GetContainer<GameObject>(notice);", StringComparison.Ordinal) &&
                !migratedSource.Contains("typeof(GameOffenceNotice).GetField(\"container\"", StringComparison.Ordinal),
                "Simple typed fallback getter should be rewritten through the generated type-scoped facade.");
            Assert(
                migratedSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue(notice, \"container\");", StringComparison.Ordinal) &&
                !migratedSource.Contains("notice.GetType().GetField(\"container\"", StringComparison.Ordinal),
                "Simple dynamic object fallback getter should be rewritten through the backend-neutral instance member registry.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore the generator package reference change.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback should restore the rewritten source file.");
            Assert(rollbackResult.RemovedFiles.Contains(targetPath), "Rollback should remove generated member-access target declarations.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MemberAccessTargetCatalogDiscoversTypedFieldPropertyHelperCalls()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempSource = Path.Combine(tempRoot, "FuelReflection.cs");
            File.WriteAllText(
                tempSource,
                """
                using ScheduleOne.Vehicles;
                using ScheduleOne.DevUtilities;

                namespace ReflectionFallbackMod;

                public sealed class FuelReflection
                {
                    private LandVehicle? _landVehicle;

                    public object? ReadVehicleName()
                    {
                        return ReflectionUtils.TryGetFieldOrProperty(_landVehicle, "vehicleName");
                    }

                    public static void CutThrottle(LandVehicle vehicle)
                    {
                        ReflectionUtils.TrySetFieldOrProperty(vehicle, "currentThrottle", 0f);
                    }
                }
                """);

            MemberAccessTarget[] targets = new MemberAccessTargetCatalog()
                .DiscoverFileTargets(tempSource)
                .ToArray();

            Assert(
                targets.Any(target =>
                    target.OwnerAlias == "LandVehicle" &&
                    target.OwnerTypeName == "ScheduleOne.Vehicles.LandVehicle" &&
                    target.MemberName == "vehicleName" &&
                    target.Kind == MemberAccessKind.FieldOrProperty &&
                    !target.IsStatic),
                "Member-access discovery should infer typed ReflectionUtils.TryGetFieldOrProperty targets from ScheduleOne field declarations.");
            Assert(
                targets.Any(target =>
                    target.OwnerAlias == "LandVehicle" &&
                    target.OwnerTypeName == "ScheduleOne.Vehicles.LandVehicle" &&
                    target.MemberName == "currentThrottle" &&
                    target.Kind == MemberAccessKind.FieldOrProperty &&
                    !target.IsStatic),
                "Member-access discovery should infer typed ReflectionUtils.TrySetFieldOrProperty targets from ScheduleOne method parameters.");

            string source = File.ReadAllText(tempSource);
        string rewritten = new MemberAccessFallbackRewriter().RewriteSource(source, tempSource, targets);
        Assert(
                rewritten.Contains("return S1Interop.ScheduleOne.Vehicles.LandVehicle.GetVehicleName(_landVehicle);", StringComparison.Ordinal),
                "Member-access fallback rewriter should replace typed helper getter calls with type-scoped facade getters.");
        Assert(
                rewritten.Contains("S1Interop.ScheduleOne.Vehicles.LandVehicle.TrySetCurrentThrottle(vehicle, 0f);", StringComparison.Ordinal),
                "Member-access fallback rewriter should replace typed helper setter calls with type-scoped facade setters.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationRewritesDynamicInstanceReflectionFallbackWithoutTargets()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "DynamicReflectionMod.csproj");
            string tempSource = Path.Combine(tempRoot, "DynamicReflection.cs");
            string targetPath = Path.Combine(tempRoot, "S1Interop.Generated", MemberAccessTargetGenerator.SourceFileName);
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
                tempSource,
                """
                using System.Reflection;

                namespace DynamicReflectionMod;

                public static class DynamicReflection
                {
                    public static object? ReadMember(object? target, string memberName)
                    {
                        if (target == null)
                        {
                            return null;
                        }

                        Type type = target.GetType();
                        PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                        {
                            return property.GetValue(target, null);
                        }

                        FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            return field.GetValue(target);
                        }

                        return null;
                    }

                    public static bool TrySetMember(object? target, string memberName, object? value)
                    {
                        if (target == null)
                        {
                            return false;
                        }

                        Type type = target.GetType();
                        PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                        {
                            property.SetValue(target, value, null);
                            return true;
                        }

                        FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            field.SetValue(target, value);
                            return true;
                        }

                        return false;
                    }
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceRisk[] risks = project.SourceInterop!.SourceRisks.ToArray();
            Assert(
                risks.Count(risk => risk.Kind == "FieldPropertyReflectionFallback") == 2,
                $"Source analyzer should report the dynamic field/property getter and setter fallbacks. Risks:{Environment.NewLine}{string.Join(Environment.NewLine, risks.Select(risk => $"{risk.Kind}: {risk.Evidence}"))}");

            MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(tempProject, [project]));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package" && operation.Automatic),
                "Dynamic instance fallback rewrites should install the S1Interop generator package for the generated member registry.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_member_access_fallbacks" && operation.Automatic),
                "Dynamic instance fallback rewrites should be automatic when the same literal member is checked as field and property.");
            Assert(
                !projectPlan.Operations.Any(operation => operation.RuleId == "generate_member_access_targets"),
                "Dynamic instance fallback rewrites should not generate owner-specific member target declarations.");
            Assert(
                !projectPlan.Operations.Any(operation => operation.RuleId == "source_risk_field_property_reflection_fallback" && !operation.Automatic),
                "Automatically rewritten dynamic instance fallback should not remain as manual source-risk guidance.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_member_access_fallbacks"),
                "Migration apply should rewrite dynamic instance reflection fallback helpers.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Migration apply should install the generator package for dynamic instance fallback helpers.");
            Assert(!File.Exists(targetPath), "Dynamic instance fallback migration should not create member-access target declarations.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue(target, memberName);", StringComparison.Ordinal) &&
                migratedSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry.TrySetInstanceValue(target, memberName, value);", StringComparison.Ordinal) &&
                !migratedSource.Contains("type.GetField(memberName", StringComparison.Ordinal) &&
                !migratedSource.Contains("type.GetProperty(memberName", StringComparison.Ordinal),
                $"Dynamic instance fallback should be rewritten through backend-neutral instance helpers. Source:{Environment.NewLine}{migratedSource}");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore the generator package reference change.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback should restore the rewritten dynamic reflection source file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void SourceInteropAnalyzerReportsDirectMemberReflectionLookupRisk()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "DirectReflectionMod.csproj");
            string tempSource = Path.Combine(tempRoot, "DirectReflection.cs");
            string targetPath = Path.Combine(tempRoot, "S1Interop.Generated", MemberAccessTargetGenerator.SourceFileName);
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
                tempSource,
                """
                using System.Reflection;

                namespace DirectReflectionMod;

                public static class DirectReflection
                {
                    public static FieldInfo? GetHomeScreenField()
                    {
                        return typeof(PhoneApp).GetField("_homeScreenInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    public static PropertyInfo? GetUserDataDirectoryProperty()
                    {
                        var property = typeof(MelonEnvironment).GetProperty("UserDataDirectory",
                            BindingFlags.Public | BindingFlags.Static);
                        return property;
                    }

                    public static MethodInfo? GetDeviceIdGetter()
                    {
                        var originalMethod = typeof(SystemInfo).GetProperty("deviceUniqueIdentifier").GetMethod;
                        return originalMethod;
                    }

                    public static FieldInfo? GetTeleportField(Il2CppScheduleOne.PlayerScripts.Player targetPlayer)
                    {
                        var teleportField = targetPlayer.GetType().GetField("teleport", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        return teleportField;
                    }

                    public static PropertyInfo? GetEnumeratorCurrent(object enumerator)
                    {
                        var current = enumerator.GetType().GetProperty("Current");
                        return current;
                    }
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceRisk[] risks = project.SourceInterop!.SourceRisks.ToArray();
            Assert(
                risks.Count(risk => risk.Kind == "DirectMemberReflectionLookup") == 3,
                $"Source analyzer should report direct typeof(...).GetField/GetProperty lookups as generated member-target guidance. Risks:{Environment.NewLine}{string.Join(Environment.NewLine, risks.Select(risk => $"{risk.Kind}: {risk.Evidence}"))}");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("GetEnumeratorCurrent", StringComparison.Ordinal) &&
                                  !risk.Evidence.Contains("GetProperty(\"Current\")", StringComparison.Ordinal)),
                "Direct member reflection lookup risk should skip untyped runtime collection/enumerator reflection that cannot produce a backend-neutral game member declaration.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("MelonEnvironment", StringComparison.Ordinal)),
                "Direct member reflection lookup risk should skip MelonLoader-owned reflection internals.");
            Assert(
                risks.Where(risk => risk.Kind == "DirectMemberReflectionLookup").All(risk => risk.Remediation.Contains("S1InteropMember", StringComparison.Ordinal)),
                "Direct member reflection lookup risk should point developers toward generated S1InteropMember declarations.");

            MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(tempProject, [project]));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_member_access_targets" && operation.Automatic),
                "Migration plan should generate member-access target attributes for direct member reflection lookups.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package" && operation.Automatic),
                "Migration plan should install the S1Interop generator package when direct member-access target attributes are generated.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_direct_member_reflection_lookups" && operation.Automatic),
                "Migration plan should rewrite safe direct member reflection lookup statements through generated typed accessors.");
            Assert(
                !projectPlan.Operations.Any(operation => operation.RuleId == "source_risk_direct_member_reflection_lookup" && !operation.Automatic),
                "Migration plan should not keep automatically rewritten direct member reflection lookups as manual guidance.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "generate_member_access_targets"),
                "Migration apply should create generated member-access target declarations for direct reflection lookups.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_direct_member_reflection_lookups"),
                "Migration apply should rewrite direct member reflection lookup statements.");
            Assert(File.Exists(targetPath), "Generated direct member-access target declarations were not written.");

            string generatedTargets = File.ReadAllText(targetPath);
            Assert(
                generatedTargets.Contains("[assembly: S1Interop.S1InteropMember(\"PhoneApp\", \"_homeScreenInstance\", Alias = \"_homeScreenInstance\")]", StringComparison.Ordinal) &&
                !generatedTargets.Contains("MelonEnvironment", StringComparison.Ordinal) &&
                generatedTargets.Contains("[assembly: S1Interop.S1InteropMember(\"SystemInfo\", \"deviceUniqueIdentifier\", Alias = \"deviceUniqueIdentifier\", Kind = S1Interop.S1InteropMemberKind.Property)]", StringComparison.Ordinal) &&
                generatedTargets.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.PlayerScripts.Player\", Alias = \"Player\")]", StringComparison.Ordinal) &&
                generatedTargets.Contains("[assembly: S1Interop.S1InteropMember(\"Player\", \"teleport\", Alias = \"teleport\")]", StringComparison.Ordinal),
                "Generated member-access targets should include direct member reflection declarations and typed GetType receiver declarations while skipping MelonLoader internals.");
            string rewrittenSource = File.ReadAllText(tempSource);
            Assert(
                rewrittenSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry._homeScreenInstanceFieldInfo;", StringComparison.Ordinal) &&
                rewrittenSource.Contains("var property = typeof(MelonEnvironment).GetProperty(\"UserDataDirectory\",", StringComparison.Ordinal) &&
                rewrittenSource.Contains("var originalMethod = S1Interop.Generated.S1InteropMemberRegistry.deviceUniqueIdentifierPropertyInfo!.GetMethod;", StringComparison.Ordinal) &&
                rewrittenSource.Contains("var teleportField = S1Interop.Generated.S1InteropMemberRegistry.teleportFieldInfo;", StringComparison.Ordinal) &&
                !rewrittenSource.Contains("typeof(SystemInfo).GetProperty(\"deviceUniqueIdentifier\").GetMethod", StringComparison.Ordinal) &&
                !rewrittenSource.Contains("targetPlayer.GetType().GetField(\"teleport\"", StringComparison.Ordinal),
                $"Direct member reflection lookups should be rewritten to generated typed metadata accessors. Source:{Environment.NewLine}{rewrittenSource}");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback should restore the rewritten direct reflection source file.");
            Assert(rollbackResult.RemovedFiles.Contains(targetPath), "Rollback should remove generated direct member-access target declarations.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MemberAccessFallbackRewriterRewritesSimpleTypedGetter()
    {
        string source =
            """
            using System.Reflection;

            namespace ReflectionFallbackMod;

            public static class ReflectionFallback
            {
                public static GameObject? GetNoticeContainer(GameOffenceNotice notice)
                {
                    FieldInfo? field = typeof(GameOffenceNotice).GetField("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.GetValue(notice) is GameObject fieldValue)
                    {
                        return fieldValue;
                    }

                    PropertyInfo? property = typeof(GameOffenceNotice).GetProperty("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    return property == null ? null : property.GetValue(notice) as GameObject;
                }
            }
            """;
        string sourcePath = Path.Combine(Path.GetTempPath(), "ReflectionFallback.cs");
        var target = new MemberAccessTarget(
            sourcePath,
            9,
            "GameOffenceNotice",
            "GameOffenceNotice",
            "container",
            "container",
            IsStatic: false);

        string rewritten = new MemberAccessFallbackRewriter().RewriteSource(source, sourcePath, [target]);
        Assert(
            rewritten.Contains("return S1Interop.GameOffenceNotice.GetContainer<GameObject>(notice);", StringComparison.Ordinal),
            $"Simple typed fallback getter should rewrite through the generated type-scoped facade. Rewritten source:{Environment.NewLine}{rewritten}");
    }

    private void MemberAccessFallbackRewriterRewritesNullableValueGetter()
    {
        string source =
            """
            using System.Reflection;

            namespace ReflectionFallbackMod;

            public static class ReflectionFallback
            {
                public static float? GetRenderScale(RenderPipelineAsset asset)
                {
                    FieldInfo? field = typeof(RenderPipelineAsset).GetField("renderScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.GetValue(asset) is float fieldValue)
                    {
                        return fieldValue;
                    }

                    PropertyInfo? property = typeof(RenderPipelineAsset).GetProperty("renderScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    return property == null ? null : property.GetValue(asset) as float?;
                }
            }
            """;
        string sourcePath = Path.Combine(Path.GetTempPath(), "ReflectionFallback.cs");
        var target = new MemberAccessTarget(
            sourcePath,
            9,
            "RenderPipelineAsset",
            "UnityEngine.Rendering.RenderPipelineAsset",
            "renderScale",
            "renderScale",
            IsStatic: false);

        string rewritten = new MemberAccessFallbackRewriter().RewriteSource(source, sourcePath, [target]);
        Assert(
            rewritten.Contains("return S1Interop.UnityEngine.Rendering.RenderPipelineAsset.GetRenderScaleValue<float>(asset);", StringComparison.Ordinal),
            $"Nullable value fallback getter should rewrite through the generated type-scoped facade value accessor. Rewritten source:{Environment.NewLine}{rewritten}");
    }

    private void MemberAccessFallbackRewriterRewritesDynamicNamedHelpers()
    {
        string source =
            """
            using System.Reflection;

            namespace ReflectionFallbackMod;

            public static class ReflectionFallback
            {
                private static object? ReadMember(object? target, string memberName)
                {
                    if (target == null)
                    {
                        return null;
                    }

                    Type type = target.GetType();
                    PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.CanRead)
                    {
                        return property.GetValue(target);
                    }

                    FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    return field?.GetValue(target);
                }

                private static bool TrySetMember(object? target, string memberName, object? value)
                {
                    if (target == null)
                    {
                        return false;
                    }

                    Type type = target.GetType();
                    PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(target, value);
                        return true;
                    }

                    FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        field.SetValue(target, value);
                        return true;
                    }

                    return false;
                }
            }
            """;

        string rewritten = new MemberAccessFallbackRewriter().RewriteSource(
            source,
            Path.Combine(Path.GetTempPath(), "ReflectionFallback.cs"),
            Array.Empty<MemberAccessTarget>());
        Assert(
            rewritten.Contains("return S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue(target, memberName);", StringComparison.Ordinal) &&
            rewritten.Contains("return S1Interop.Generated.S1InteropMemberRegistry.TrySetInstanceValue(target, memberName, value);", StringComparison.Ordinal) &&
            !rewritten.Contains("type.GetProperty(memberName", StringComparison.Ordinal) &&
            !rewritten.Contains("type.GetField(memberName", StringComparison.Ordinal),
            $"Dynamic named member helpers should rewrite through backend-neutral instance helpers. Rewritten source:{Environment.NewLine}{rewritten}");
    }

    private void DirectMemberReflectionLookupRewriterRewritesSplitAndCachedAssignments()
    {
        string source =
            """
            using System.Reflection;

            namespace DirectReflectionMod;

            public sealed class ReflectionCache
            {
                private FieldInfo? _cachedFileField;

                public void Initialize()
                {
                    var homeScreenField =
                        typeof(PhoneApp).GetField("_homeScreenInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                    _cachedFileField = typeof(MelonPreferences_Category).GetField("File",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            """;
        string sourcePath = Path.Combine(Path.GetTempPath(), "DirectReflection.cs");
        var homeScreenTarget = new MemberAccessTarget(
            sourcePath,
            11,
            "PhoneApp",
            "PhoneApp",
            "_homeScreenInstance",
            "_homeScreenInstance",
            IsStatic: false,
            MemberAccessKind.Field);
        var fileTarget = new MemberAccessTarget(
            sourcePath,
            13,
            "MelonPreferences_Category",
            "MelonPreferences_Category",
            "File",
            "File",
            IsStatic: false,
            MemberAccessKind.FieldOrProperty);

        string rewritten = new DirectMemberReflectionLookupRewriter().RewriteSource(source, sourcePath, [homeScreenTarget, fileTarget]);
        Assert(
            rewritten.Contains("var homeScreenField = S1Interop.Generated.S1InteropMemberRegistry._homeScreenInstanceFieldInfo;", StringComparison.Ordinal) &&
            rewritten.Contains("_cachedFileField = S1Interop.Generated.S1InteropMemberRegistry.FileFieldInfo;", StringComparison.Ordinal) &&
            !rewritten.Contains(".GetField(\"_homeScreenInstance\"", StringComparison.Ordinal) &&
            !rewritten.Contains(".GetField(\"File\"", StringComparison.Ordinal),
            $"Direct member lookup rewriter should handle split assignments and cached FieldInfo assignments. Rewritten source:{Environment.NewLine}{rewritten}");
    }

    private void MigrationApplyAndRollbackRewritesHarmonyOverloadBindings()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "HarmonyBindingMod.csproj");
            string tempSource = Path.Combine(tempRoot, "MoveItemPatch.cs");
            string targetSource = Path.Combine(tempRoot, "S1Interop.Generated", HarmonyMethodTargetGenerator.SourceFileName);
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
                tempSource,
                """
                using HarmonyLib;
                using ScheduleOne.ItemFramework;
                using ScheduleOne.Management;
                using ScheduleOne.NPCs.Behaviour;

                namespace HarmonyBindingMod;

                public static class MoveItemPatch
                {
                    public static void Patch(Harmony harmony)
                    {
                        var method = AccessTools.Method(
                            typeof(MoveItemBehaviour),
                            nameof(MoveItemBehaviour.IsDestinationValid),
                            [
                                typeof(TransitRoute),
                                typeof(ItemInstance),
                                typeof(string).MakeByRefType()
                            ]
                        );

                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(MoveItemPatch), nameof(Prefix)));
                    }

                    private static bool Prefix(ref string invalidReason) => true;
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Harmony method target migration should install the S1Interop generator package.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_harmony_method_targets"),
                "Harmony method target migration should generate assembly attributes.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_harmony_overload_bindings"),
                "Harmony method target migration should rewrite AccessTools.Method blocks.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(File.Exists(targetSource), "Harmony method target source was not generated.");
            string generated = File.ReadAllText(targetSource);
            Assert(
                generated.Contains("[assembly: S1Interop.S1InteropType(\"MoveItemBehaviour\", Alias = \"MoveItemBehaviour\")]", StringComparison.Ordinal) &&
                generated.Contains("[assembly: S1Interop.S1InteropMember(\"MoveItemBehaviour\", \"IsDestinationValid\", Alias = \"IsDestinationValid\", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { \"TransitRoute\", \"ItemInstance\", \"string&\" })]", StringComparison.Ordinal),
                $"Generated Harmony target attributes are incomplete. Generated source:{Environment.NewLine}{generated}");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("var method = S1Interop.Generated.S1InteropMemberRegistry.IsDestinationValidMethod;", StringComparison.Ordinal),
                $"Harmony AccessTools.Method block should be rewritten to generated MethodInfo target. Migrated source:{Environment.NewLine}{migratedSource}");
            Assert(
                !migratedSource.Contains("AccessTools.Method", StringComparison.Ordinal),
                "Rewritten Harmony source should not keep the old AccessTools.Method block.");
            Assert(
                File.ReadAllText(tempProject).Contains("S1Interop.Generators", StringComparison.Ordinal),
                "Project should reference the S1Interop generator package after migration.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the project file.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the rewritten source file.");
            Assert(rollbackResult.RemovedFiles.Contains(targetSource), "Rollback did not remove generated Harmony method targets.");
            Assert(!File.Exists(targetSource), "Generated Harmony method target source should be removed by rollback.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void SourceInteropAnalyzerDoesNotReportRuntimeGuardedSourceRisks()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "GuardedSourceMod.csproj");
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
                Path.Combine(tempRoot, "Guarded.cs"),
                """
                using System;
                using HarmonyLib;
                using UnityEngine.Events;
                using UnityEngine.UI;

                namespace GuardedSourceMod;

                public static class Guarded
                {
                    public static void Bind(Button button)
                    {
                #if MONO
                        button.onClick.AddListener(OnClicked);
                        SomeEvent = (Action)Delegate.Combine(SomeEvent, OnClicked);
                #elif IL2CPP
                        button.onClick.AddListener(new System.Action(OnClicked));
                        Il2CppSystem.Delegate combined = Il2CppSystem.Delegate.Combine(SomeEvent, SomeEvent);
                        SteamNetworking.ReadP2PPacket(il2CppBuffer, packetSize, out bytesRead, out remoteId, channel);
                        if (pipelineAsset is Il2CppObjectBase il2CppObject)
                        {
                            UniversalRenderPipelineAsset? asset = il2CppObject.TryCast<UniversalRenderPipelineAsset>();
                        }
                #endif
                    }

                    public static void BindInternalPostfix(object __instance,
                #if MONO
                        System.Collections.Generic.List<EntityConfiguration> configs)
                #else
                        Il2CppSystem.Collections.Generic.List<EntityConfiguration> configs)
                #endif
                    {
                    }

                    public static void BadIl2CppPostfix(object __instance,
                #if IL2CPP
                        System.Collections.Generic.List<EntityConfiguration> badConfigs)
                #else
                        System.Collections.Generic.List<EntityConfiguration> monoConfigs)
                #endif
                    {
                    }

                    public static void BadIl2CppBuffers(uint packetSize, out uint bytesRead, out object remoteId, int channel, VehicleData vehicleData)
                    {
                #if IL2CPP
                        byte[] managedBuffer = new byte[packetSize];
                        SteamNetworking.ReadP2PPacket(managedBuffer, packetSize, out bytesRead, out remoteId, channel);
                        var spraySurfaces = vehicleData.SpraySurfaces ?? new Il2CppSystem.Collections.Generic.List<SpraySurfaceData>();
                #else
                        byte[] managedBuffer = new byte[packetSize];
                        bytesRead = 0;
                        remoteId = new object();
                #endif
                    }

                    public static void BindSafe(Button button, UnityAction callback)
                    {
                        button.onClick.AddListener(callback);
                        button.onClick.AddListener(Utils.ToUnityAction(OnClicked));
                    }

                    [HarmonyTranspiler]
                    public static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions) => instructions;

                    private static event Action? SomeEvent;
                    private static void OnClicked() { }
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceRisk[] risks = project.SourceInterop!.SourceRisks.ToArray();

            Assert(
                risks.All(risk => !risk.Evidence.Contains("SomeEvent = (Action)Delegate.Combine", StringComparison.Ordinal)),
                "Runtime-guarded Mono Delegate.Combine code should not be reported as unhandled migration risk.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("Il2CppSystem.Delegate.Combine", StringComparison.Ordinal)),
                "Explicit Il2CppSystem.Delegate code should not be reported as direct Mono delegate risk.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("button.onClick.AddListener(OnClicked)", StringComparison.Ordinal)),
                "Runtime-guarded Mono listener code should not be reported as unhandled migration risk.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("button.onClick.AddListener(callback)", StringComparison.Ordinal)),
                "UnityAction listener parameters should not be reported as unhandled migration risk.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("Utils.ToUnityAction", StringComparison.Ordinal)),
                "Explicit ToUnityAction helper calls should not be reported as unhandled migration risk.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("il2CppBuffer", StringComparison.Ordinal)),
                "Runtime-guarded IL2CPP packet buffers that already use wrapper buffers should not be reported.");
            Assert(
                risks.All(risk => !risk.Evidence.Contains("Il2CppSystem.Collections.Generic.List<EntityConfiguration> configs", StringComparison.Ordinal)),
                "Runtime-guarded collection signatures that already use IL2CPP wrapper lists should not be reported.");
            Assert(
                risks.All(risk => risk.Kind != "Il2CppObjectCastInterop"),
                "Runtime-guarded IL2CPP object TryCast branches should not be reported.");
            Assert(
                risks.Any(risk => risk.Kind == "Il2CppByteBufferInterop" && risk.Evidence.Contains("managedBuffer", StringComparison.Ordinal)),
                "Runtime-guarded IL2CPP branches that still pass managed byte[] buffers should be reported.");
            Assert(
                risks.Any(risk => risk.Kind == "ManagedCollectionSignatureInterop" && risk.Evidence.Contains("badConfigs", StringComparison.Ordinal)),
                "Runtime-guarded IL2CPP branches that still expose managed collection signatures should be reported.");
            Assert(
                risks.Any(risk => risk.Kind == "Il2CppListNullCoalesce" && risk.Evidence.Contains("SpraySurfaces", StringComparison.Ordinal)),
                "Runtime-guarded IL2CPP branches that null-coalesce IL2CPP list wrappers should be reported.");
            Assert(
                risks.Any(risk => risk.Kind == "HarmonyTranspiler"),
                "Unguarded Harmony transpiler should still be reported.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackRewritesUnityEventListeners()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ListenerMod.csproj");
            string tempSource = Path.Combine(tempRoot, "ListenerUi.cs");
            string bridgePath = Path.Combine(tempRoot, "S1Interop.Generated", UnityEventBridgeGenerator.SourceFileName);
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
                tempSource,
                """
                using System;
                using UnityEngine.Events;
                using UnityEngine.UI;

                namespace ListenerMod;

                public static class ListenerUi
                {
                    public static void Bind(Button button, InputField input, Button otherButton, Button systemButton, Button multilineButton, Button delegateButton, Button cachedActionButton, Button unityActionButton, Button wrappedActionButton, Action listener)
                    {
                        button.onClick.AddListener(OnClicked);
                        input.onValueChanged.AddListener(text => OnText(text));
                        otherButton.onClick.AddListener(callback);
                        systemButton.onClick.AddListener(new System.Action(OnClicked));
                        cachedActionButton.onClick.AddListener(cachedAction);
                        unityActionButton.onClick.AddListener(cachedUnityAction);
                        multilineButton.onClick.AddListener(new Action(() =>
                        {
                            OnClicked();
                        }));
                        delegateButton.onClick.AddListener(delegate()
                        {
                            OnClicked();
                        });
                        var uAction = new UnityAction(listener);
                        wrappedActionButton.onClick.AddListener(uAction);
                    }

                    private static System.Action callback = () => { };
                    private static readonly Action cachedAction = OnClicked;
                    private static readonly UnityAction cachedUnityAction = new UnityAction(OnClicked);
                    private static void OnClicked() { }
                    private static void OnText(string text) { }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_unity_event_bridge"),
                "Expected UnityEvent bridge generation operation.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_unity_event_listeners"),
                "Expected UnityEvent listener rewrite operation.");
            Assert(
                projectPlan.Operations.All(operation => operation.RuleId != "source_risk_direct_unity_event_listener"),
                "Known Action listeners should be automatic, and known UnityAction listeners should be treated as already runtime-safe.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(File.Exists(bridgePath), "UnityEvent bridge source was not generated.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_unity_event_listeners"),
                "Migration apply should rewrite UnityEvent listeners.");

            string bridgeSource = File.ReadAllText(bridgePath);
            Assert(
                bridgeSource.Contains("namespace S1Interop.Generated", StringComparison.Ordinal) &&
                !bridgeSource.Contains("namespace S1Interop.Generated;", StringComparison.Ordinal) &&
                !bridgeSource.Contains("#nullable", StringComparison.Ordinal) &&
                !bridgeSource.Contains("= new();", StringComparison.Ordinal),
                "Generated UnityEvent bridge should avoid C# 10-only syntax.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(button.onClick, new System.Action(OnClicked));", StringComparison.Ordinal),
                "Method-group onClick listener should be rewritten through the bridge with an explicit System.Action.");
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(input.onValueChanged, text => OnText(text));", StringComparison.Ordinal),
                "Lambda value-change listener should be rewritten through the bridge.");
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(otherButton.onClick, callback);", StringComparison.Ordinal),
                "Known System.Action variable listener should be rewritten automatically.");
            Assert(
                migratedSource.Contains("systemButton.onClick.AddListener(new System.Action(OnClicked));", StringComparison.Ordinal),
                "Already runtime-specific System.Action listener should not be rewritten automatically.");
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(cachedActionButton.onClick, cachedAction);", StringComparison.Ordinal),
                "Known cached System.Action listener should be rewritten through the bridge.");
            Assert(
                migratedSource.Contains("unityActionButton.onClick.AddListener(cachedUnityAction);", StringComparison.Ordinal),
                "Known UnityAction listener should not be rewritten automatically.");
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(wrappedActionButton.onClick, listener);", StringComparison.Ordinal),
                "Local UnityAction wrapper variables should be rewritten through the bridge using their original Action listener.");
            Assert(
                !migratedSource.Contains("var uAction = new UnityAction(listener);", StringComparison.Ordinal),
                "Dead local UnityAction wrapper declarations should be removed after bridge rewrites.");
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(multilineButton.onClick, new Action(() =>", StringComparison.Ordinal),
                "Multi-line Action listener should rewrite its opening line through the bridge.");
            Assert(
                !migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(multilineButton.onClick, new Action(() =>)", StringComparison.Ordinal),
                "Multi-line Action listener rewrite should not close the bridge call before the lambda body.");
            Assert(
                migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(delegateButton.onClick, delegate()", StringComparison.Ordinal),
                "Multi-line delegate listener should rewrite its opening line through the bridge.");
            Assert(
                !migratedSource.Contains("S1Interop.Generated.S1InteropUnityEventBridge.Add(delegateButton.onClick, delegate())", StringComparison.Ordinal),
                "Multi-line delegate listener rewrite should not close the bridge call before the delegate body.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            SourceRisk[] directListenerRisks = after.Projects.Single().SourceInterop!.SourceRisks
                .Where(risk => risk.Kind == "DirectUnityEventListener")
                .ToArray();
            Assert(
                directListenerRisks.Length == 0,
                "No direct listener risks should remain after automatic rewrites and runtime-safe listener detection.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the rewritten source file.");
            Assert(rollbackResult.RemovedFiles.Contains(bridgePath), "Rollback did not remove the generated UnityEvent bridge.");
            Assert(!File.Exists(bridgePath), "Generated UnityEvent bridge should be removed by rollback.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackGeneratesSourceRiskReport()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "RiskySourceMod.csproj");
            string reportPath = Path.Combine(tempRoot, "S1Interop.Generated", SourceRiskReportGenerator.ReportFileName);
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
                Path.Combine(tempRoot, "Risky.cs"),
                """
                using System;
                using System.Collections.Generic;
                using HarmonyLib;
                using UnityEngine.UI;

                namespace RiskySourceMod;

                public static class Risky
                {
                    public static void Bind(Button button)
                    {
                        button.onClick.AddListener(OnClicked);
                        SomeEvent = (Action)Delegate.Combine(OtherEvent, new Action(OnClicked));
                    }

                    [HarmonyTranspiler]
                    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => instructions;

                    private static event Action? SomeEvent;
                    private static event Action? OtherEvent;
                    private static void OnClicked() { }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();

            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_source_risk_report" && operation.Automatic),
                "Migration plan should generate a source-risk report when source risks exist.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "generate_source_risk_report"),
                "Migration apply should create the source-risk report.");
            Assert(File.Exists(reportPath), "Source-risk report was not written.");

            string report = File.ReadAllText(reportPath);
            Assert(report.Contains("Harmony Transpiler", StringComparison.Ordinal), "Report should group Harmony transpiler risks.");
            Assert(!report.Contains("Direct Unity Event Listener", StringComparison.Ordinal), "Automatically rewritable listener risks should not remain in the manual report.");
            Assert(report.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal), "Report should include delegate conversion remediation.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(
                rollbackResult.RemovedFiles.Contains(reportPath),
                "Rollback should report removing the generated source-risk report.");
            Assert(!File.Exists(reportPath), "Rollback should remove the generated source-risk report.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackRewritesSimpleDelegateAssignments()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "DelegateSourceMod.csproj");
            string tempSource = Path.Combine(tempRoot, "DelegateBindings.cs");
            string bridgePath = Path.Combine(tempRoot, "S1Interop.Generated", DelegateEventBridgeGenerator.SourceFileName);
            string reportPath = Path.Combine(tempRoot, "S1Interop.Generated", SourceRiskReportGenerator.ReportFileName);
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
                tempSource,
                """
                using System;

                namespace DelegateSourceMod;

                public static class DelegateBindings
                {
                    private static Action? SomeEvent;
                    private static Action? OtherEvent;
                    private static Action? ManualEvent;
                    private static Action? TernaryEvent;
                    private static Action? ExistingEvent;
                    private static bool Flag;

                    public static void Bind()
                    {
                        SomeEvent = (Action?)Delegate.Combine(SomeEvent, new Action(OnClicked));
                        SomeEvent = (Action?)Delegate.Remove(SomeEvent, new Action(OnClicked));
                        OtherEvent = (Action?)System.Delegate.Combine(OtherEvent, new Action(OnClicked)); // keep comment
                        ManualEvent = (Action?)Delegate.Combine(OtherEvent, new Action(OnClicked));
                        TernaryEvent = (Action?)Delegate.Combine(Flag ? TernaryEvent : OtherEvent, new Action(OnClicked));
                        ExistingEvent = (Action?)S1Interop.Generated.S1InteropDelegateEventBridge.Combine(ExistingEvent, new Action(OnClicked));
                    }

                    private static void OnClicked() { }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();

            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_delegate_event_bridge"),
                "Migration plan should generate the delegate event bridge for safe delegate rewrites.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_delegate_assignments"),
                "Migration plan should rewrite safe delegate assignments.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_source_risk_report"),
                "Migration plan should still report delegate cases that are not safe to rewrite.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "generate_delegate_event_bridge"),
                "Migration apply should create the delegate bridge.");
            Assert(File.Exists(bridgePath), "Delegate bridge was not written.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("SomeEvent = (Action?)S1Interop.Generated.S1InteropDelegateEventBridge.Combine(SomeEvent, new Action(OnClicked));", StringComparison.Ordinal),
                "Safe Delegate.Combine self-assignment was not rewritten.");
            Assert(
                migratedSource.Contains("SomeEvent = (Action?)S1Interop.Generated.S1InteropDelegateEventBridge.Remove(SomeEvent, new Action(OnClicked));", StringComparison.Ordinal),
                "Safe Delegate.Remove self-assignment was not rewritten.");
            Assert(
                migratedSource.Contains("OtherEvent = (Action?)S1Interop.Generated.S1InteropDelegateEventBridge.Combine(OtherEvent, new Action(OnClicked)); // keep comment", StringComparison.Ordinal),
                "System.Delegate.Combine self-assignment was not rewritten while preserving the comment.");
            Assert(
                migratedSource.Contains("ManualEvent = (Action?)Delegate.Combine(OtherEvent, new Action(OnClicked));", StringComparison.Ordinal),
                "Non-self delegate assignment should stay manual.");
            Assert(
                migratedSource.Contains("TernaryEvent = (Action?)Delegate.Combine(Flag ? TernaryEvent : OtherEvent, new Action(OnClicked));", StringComparison.Ordinal),
                "Complex delegate expression should stay manual.");
            Assert(
                migratedSource.Contains("ExistingEvent = (Action?)S1Interop.Generated.S1InteropDelegateEventBridge.Combine(ExistingEvent, new Action(OnClicked));", StringComparison.Ordinal),
                "Existing bridge usage should stay intact.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            SourceRisk[] remainingDelegateRisks = after.Projects.Single().SourceInterop!.SourceRisks
                .Where(risk => risk.Kind.Equals("DirectDelegateCombine", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert(remainingDelegateRisks.Length == 2, "Only the non-rewritable delegate risks should remain after migration.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the rewritten delegate source file.");
            Assert(rollbackResult.RemovedFiles.Contains(bridgePath), "Rollback did not remove the generated delegate bridge.");
            Assert(rollbackResult.RemovedFiles.Contains(reportPath), "Rollback did not remove the generated source-risk report.");
            Assert(!File.Exists(bridgePath), "Generated delegate bridge should be removed by rollback.");
            Assert(!File.Exists(reportPath), "Generated source-risk report should be removed by rollback.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackRewritesDelegateArguments()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "DelegateArgumentMod.csproj");
            string tempSource = Path.Combine(tempRoot, "WindowController.cs");
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
                tempSource,
                """
                namespace DelegateArgumentMod;

                public sealed class WindowController
                {
                    private object windowRect = new();
                    private object style = new();

                    public void Draw()
                    {
                        windowRect = GUI.Window(0, windowRect, DrawWindow, "", style);
                        windowRect = GUI.Window(1, windowRect, S1Interop.Generated.S1InteropDelegateBridge.Convert<GUI.WindowFunction>(DrawWindow), "");
                        windowRect = GUI.Window(2, windowRect, DelegateSupport.ConvertDelegate<GUI.WindowFunction>(DrawWindow), "");
                    }

                    private void DrawWindow(int id)
                    {
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_delegate_arguments" && operation.Automatic),
                "Migration plan should rewrite simple direct delegate arguments.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package" && operation.Automatic),
                "Migration plan should install the S1Interop generator package for delegate conversion helper generation.");
            Assert(
                projectPlan.Operations.All(operation => operation.RuleId != "source_risk_direct_delegate_argument_interop"),
                "Simple delegate argument risks should not remain manual when the generated helper rewrite can handle them.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlanner().Plan(before));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_delegate_arguments"),
                "Migration apply should rewrite simple direct delegate arguments.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Migration apply should install the generator package for rewritten delegate arguments.");

            string migratedProject = File.ReadAllText(tempProject);
            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedProject.Contains("S1Interop.Generators", StringComparison.Ordinal),
                "Delegate argument migration should add the S1Interop generator package reference.");
            Assert(
                migratedSource.Contains("windowRect = GUI.Window(0, windowRect, S1Interop.Generated.S1InteropDelegateBridge.Convert<GUI.WindowFunction>(DrawWindow), \"\", style);", StringComparison.Ordinal),
                "Delegate argument migration should rewrite the plain GUI.Window method-group argument through S1InteropDelegateBridge.");
            Assert(
                CountOccurrences(migratedSource, "S1Interop.Generated.S1InteropDelegateBridge.Convert<GUI.WindowFunction>") == 2,
                "Delegate argument migration should rewrite only the direct method-group argument and keep existing helper usage intact.");
            Assert(
                migratedSource.Contains("DelegateSupport.ConvertDelegate<GUI.WindowFunction>(DrawWindow)", StringComparison.Ordinal),
                "Existing IL2CPP-specific DelegateSupport conversion should stay intact.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Projects.Single().SourceInterop!.SourceRisks.All(risk => risk.Kind != "DirectDelegateArgumentInterop"),
                "Delegate argument migration should clear rewritable delegate argument risks from the migrated source.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the generator package reference change.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the rewritten delegate argument source file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackRewritesIl2CppObjectCasts()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "ObjectCastMod.csproj");
            string tempSource = Path.Combine(tempRoot, "RenderOptimizer.cs");
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
                tempSource,
                """
                namespace ObjectCastMod;

                public static class RenderOptimizer
                {
                    public static bool TrySetScale(object? pipelineAsset)
                    {
                        if (pipelineAsset is UniversalRenderPipelineAsset typedAsset)
                        {
                            typedAsset.renderScale = 0.75f;
                            return true;
                        }

                        if (S1Interop.Generated.S1InteropObjectCast.Is<UniversalRenderPipelineAsset>(pipelineAsset, out UniversalRenderPipelineAsset? existingAsset))
                        {
                            existingAsset.renderScale = 1.0f;
                        }

                        if (undoMaskGo?.GetComponent<Mask>() != null) // remove rounded mask, the button is transparent anyway
                        {
                            return true;
                        }

                        return false;
                    }

                    public static object? PhoneGameObject(object? phone)
                    {
                        return phone is Component c ? c.gameObject : null;
                    }

                    public static float? ReadScale(object? scale)
                    {
                        return scale is float f ? f : null;
                    }

                    public static string GetErrorMessage()
                    {
                        return "Current render pipeline is not a UniversalRenderPipelineAsset.";
                    }
                }

                public sealed class Component
                {
                    public object gameObject = new();
                }

                public sealed class Mask
                {
                }

                public sealed class UniversalRenderPipelineAsset
                {
                    public float renderScale;
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_il2cpp_object_casts" && operation.Automatic),
                "Migration plan should rewrite simple IL2CPP-prone object pattern casts.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package" && operation.Automatic),
                "Migration plan should install the S1Interop generator package for object cast helper generation.");
            Assert(
                projectPlan.Operations.All(operation => operation.RuleId != "source_risk_il2_cpp_object_cast_interop"),
                "Simple object cast risks should not remain manual when the generated helper rewrite can handle them.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlanner().Plan(before));
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_il2cpp_object_casts"),
                "Migration apply should rewrite simple object pattern casts.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Migration apply should install the generator package for rewritten object casts.");

            string migratedProject = File.ReadAllText(tempProject);
            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedProject.Contains("S1Interop.Generators", StringComparison.Ordinal),
                "Object cast migration should add the S1Interop generator package reference.");
            Assert(
                migratedSource.Contains("if (S1Interop.Generated.S1InteropObjectCast.Is<UniversalRenderPipelineAsset>(pipelineAsset, out UniversalRenderPipelineAsset? typedAsset))", StringComparison.Ordinal),
                "Object cast migration should rewrite the plain C# pattern match through S1InteropObjectCast.");
            Assert(
                CountOccurrences(migratedSource, "S1Interop.Generated.S1InteropObjectCast.Is<UniversalRenderPipelineAsset>") == 2,
                "Object cast migration should rewrite the risky cast once without rewriting existing generated-helper usage.");
            Assert(
                migratedSource.Contains("return S1Interop.Generated.S1InteropObjectCast.Is<Component>(phone, out Component? c) ? c.gameObject : null;", StringComparison.Ordinal),
                "Object cast migration should rewrite simple return ternary pattern casts through S1InteropObjectCast.");
            Assert(
                migratedSource.Contains("return scale is float f ? f : null;", StringComparison.Ordinal),
                "Object cast migration should not rewrite value-type pattern casts because S1InteropObjectCast is class-constrained.");
            Assert(
                migratedSource.Contains("if (undoMaskGo?.GetComponent<Mask>() != null) // remove rounded mask, the button is transparent anyway", StringComparison.Ordinal),
                "Object cast migration should not report or rewrite harmless GetComponent checks just because comments contain the word 'is'.");
            Assert(
                migratedSource.Contains("return \"Current render pipeline is not a UniversalRenderPipelineAsset.\";", StringComparison.Ordinal),
                "Object cast migration should not report or rewrite string literals that describe runtime types.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Projects.Single().SourceInterop!.SourceRisks.All(risk => risk.Kind != "Il2CppObjectCastInterop"),
                "Object cast migration should clear rewritable object cast risks from the migrated source.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the generator package reference change.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback did not restore the rewritten object cast source file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void InitCommandApplyAndRollbackCreatesBackendNeutralStarter()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "FreshBackendNeutralMod.csproj");
            string starterPath = Path.Combine(tempRoot, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);

            ProcessResult dryRun = RunCli("init", tempProject);
            Assert(dryRun.ExitCode == 0, $"s1interop init dry-run should succeed. Output: {dryRun.Output}");
            Assert(
                dryRun.Output.Contains("install_s1interop_generator_package", StringComparison.Ordinal) &&
                dryRun.Output.Contains("generate_backend_neutral_starter", StringComparison.Ordinal),
                $"s1interop init dry-run should plan generator install and starter generation. Output: {dryRun.Output}");
            Assert(!File.Exists(starterPath), "s1interop init dry-run should not write the starter file.");

            ProcessResult apply = RunCli("init", tempProject, "--apply");
            Assert(apply.ExitCode == 0, $"s1interop init --apply should succeed. Output: {apply.Output}");
            Assert(File.Exists(starterPath), "s1interop init --apply should create the backend-neutral starter file.");

            string projectSource = File.ReadAllText(tempProject);
            string starterSource = File.ReadAllText(starterPath);
            Assert(
                projectSource.Contains("S1Interop.Generators", StringComparison.Ordinal) &&
                projectSource.Contains("PrivateAssets", StringComparison.Ordinal),
                "s1interop init should install the S1Interop generator package privately.");
            Assert(
                starterSource.Contains("// [assembly: S1Interop.S1InteropGenerateUnityEventBridge]", StringComparison.Ordinal) &&
                starterSource.Contains("// [assembly: S1Interop.S1InteropGenerateDelegateEventBridge]", StringComparison.Ordinal) &&
                !starterSource.Contains($"{Environment.NewLine}[assembly: S1Interop.S1InteropGenerateUnityEventBridge]", StringComparison.Ordinal) &&
                !starterSource.Contains($"{Environment.NewLine}[assembly: S1Interop.S1InteropGenerateDelegateEventBridge]", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropType", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropMember", StringComparison.Ordinal),
                "Backend-neutral starter should keep bridge generation opt-in while seeding editable type/member examples.");

            string manifestPath = ExtractManifestPath(apply.Output);
            ProcessResult rollback = RunCli("migrate", "rollback", manifestPath);
            Assert(rollback.ExitCode == 0, $"s1interop migrate rollback should restore init changes. Output: {rollback.Output}");
            Assert(!File.Exists(starterPath), "Rollback should remove the backend-neutral starter file created by init.");
            Assert(
                !File.ReadAllText(tempProject).Contains("S1Interop.Generators", StringComparison.Ordinal),
                "Rollback should restore the project file before the generator package reference was installed.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void NewCommandCreatesBackendNeutralProjectScaffold()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string targetDirectory = Path.Combine(tempRoot, "Fresh Neutral Mod");
            string projectName = "FreshNeutralMod";
            string solutionPath = Path.Combine(targetDirectory, $"{projectName}.sln");
            string projectPath = Path.Combine(targetDirectory, $"{projectName}.csproj");
            string corePath = Path.Combine(targetDirectory, "ModCore.cs");
            string starterPath = Path.Combine(targetDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
            string localPropsExamplePath = Path.Combine(targetDirectory, "local.build.props.example");
            string localPropsPath = Path.Combine(targetDirectory, "local.build.props");
            string gitignorePath = Path.Combine(targetDirectory, ".gitignore");

            ProcessResult dryRun = RunCli("new", targetDirectory);
            Assert(dryRun.ExitCode == 0, $"s1interop new dry-run should succeed. Output: {dryRun.Output}");
            Assert(
                dryRun.Output.Contains("S1Interop new project dry-run", StringComparison.Ordinal) &&
                dryRun.Output.Contains(projectPath, StringComparison.Ordinal) &&
                dryRun.Output.Contains(starterPath, StringComparison.Ordinal),
                $"s1interop new dry-run should print planned scaffold files. Output: {dryRun.Output}");
            Assert(!Directory.Exists(targetDirectory), "s1interop new dry-run should not create the target directory.");

            ProcessResult apply = RunCli("new", targetDirectory, "--apply");
            Assert(apply.ExitCode == 0, $"s1interop new --apply should succeed. Output: {apply.Output}");
            Assert(File.Exists(solutionPath), "s1interop new should create a solution file for IDE builds.");
            Assert(File.Exists(projectPath), "s1interop new should create the project file.");
            Assert(File.Exists(corePath), "s1interop new should create the core source file.");
            Assert(File.Exists(starterPath), "s1interop new should create the backend-neutral starter file.");
            Assert(File.Exists(localPropsExamplePath), "s1interop new should create a local build props example.");
            Assert(File.Exists(gitignorePath), "s1interop new should create a gitignore for local machine paths and build output.");

            string solutionSource = File.ReadAllText(solutionPath);
            string projectSource = File.ReadAllText(projectPath);
            string coreSource = File.ReadAllText(corePath);
            string starterSource = File.ReadAllText(starterPath);
            string localPropsExampleSource = File.ReadAllText(localPropsExamplePath);
            string gitignoreSource = File.ReadAllText(gitignorePath);
            Assert(
                solutionSource.Contains("Debug|Any CPU = Debug|Any CPU", StringComparison.Ordinal) &&
                solutionSource.Contains("Release|Any CPU = Release|Any CPU", StringComparison.Ordinal) &&
                solutionSource.Contains("Debug Il2Cpp|Any CPU = Debug Il2Cpp|Any CPU", StringComparison.Ordinal) &&
                solutionSource.Contains("Release Il2Cpp|Any CPU = Release Il2Cpp|Any CPU", StringComparison.Ordinal) &&
                solutionSource.Contains($"{projectName}.csproj", StringComparison.Ordinal),
                "Generated solution should expose Mono-default and IL2CPP reference configurations for Visual Studio and Rider.");
            Assert(
                projectSource.Contains("<TargetFramework>netstandard2.1</TargetFramework>", StringComparison.Ordinal) &&
                projectSource.Contains("<LangVersion>10.0</LangVersion>", StringComparison.Ordinal) &&
                projectSource.Contains(S1InteropPackageInfo.GeneratorsPackageId, StringComparison.Ordinal) &&
                projectSource.Contains($"PrivateAssets=\"{S1InteropPackageInfo.PrivateAssets}\"", StringComparison.Ordinal) &&
                projectSource.Contains("<Import Project=\"local.build.props\" Condition=\"Exists('local.build.props')\" />", StringComparison.Ordinal) &&
                projectSource.Contains("<Configurations>Debug;Release;Debug Il2Cpp;Release Il2Cpp</Configurations>", StringComparison.Ordinal) &&
                projectSource.Contains("<S1InteropReferenceRuntime Condition=\"'$(S1InteropReferenceRuntime)'=='' and ('$(Configuration)'=='Debug Il2Cpp' or '$(Configuration)'=='Release Il2Cpp')\">Il2Cpp</S1InteropReferenceRuntime>", StringComparison.Ordinal) &&
                projectSource.Contains("<S1InteropReferenceRuntime Condition=\"'$(S1InteropReferenceRuntime)'==''\">Mono</S1InteropReferenceRuntime>", StringComparison.Ordinal) &&
                projectSource.Contains("<GamePath Condition=\"'$(GamePath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp'\">$(Il2CppGamePath)</GamePath>", StringComparison.Ordinal) &&
                projectSource.Contains("<ManagedPath Condition=\"'$(ManagedPath)'=='' and '$(S1InteropReferenceRuntime)'=='Il2Cpp' and '$(GamePath)'!=''\">$(GamePath)\\MelonLoader\\Il2CppAssemblies</ManagedPath>", StringComparison.Ordinal) &&
                projectSource.Contains("<Reference Include=\"MelonLoader\">", StringComparison.Ordinal) &&
                projectSource.Contains("<Reference Include=\"0Harmony\">", StringComparison.Ordinal) &&
                projectSource.Contains("<Reference Include=\"UnityEngine.CoreModule\">", StringComparison.Ordinal) &&
                projectSource.Contains("<Reference Include=\"Assembly-CSharp\">", StringComparison.Ordinal) &&
                projectSource.Contains("<Reference Include=\"ScheduleOne.Core\" Condition=\"'$(S1InteropReferenceRuntime)'!='Il2Cpp'\">", StringComparison.Ordinal) &&
                projectSource.Contains("<Reference Include=\"Il2CppScheduleOne.Core\" Condition=\"'$(S1InteropReferenceRuntime)'=='Il2Cpp'\">", StringComparison.Ordinal),
                "Generated project should target netstandard2.1, enable C# 10, install S1Interop.Generators privately, and select Mono or IL2CPP game references without forcing backend-specific generated code.");
            Assert(
                coreSource.Contains("namespace FreshNeutralMod;", StringComparison.Ordinal) &&
                coreSource.Contains("[assembly: MelonInfo(typeof(FreshNeutralMod.ModCore), \"FreshNeutralMod\", \"0.1.0\", \"YourName\")]", StringComparison.Ordinal) &&
                coreSource.Contains("public sealed class ModCore : MelonMod", StringComparison.Ordinal) &&
                coreSource.Contains("public override void OnInitializeMelon()", StringComparison.Ordinal) &&
                coreSource.Contains("public const string ModName = \"FreshNeutralMod\";", StringComparison.Ordinal),
                "Generated core source should be a real MelonLoader mod entry point with a sanitized project namespace/name.");
            Assert(
                starterSource.Contains("// [assembly: S1Interop.S1InteropGenerateUnityEventBridge]", StringComparison.Ordinal) &&
                !starterSource.Contains($"{Environment.NewLine}[assembly: S1Interop.S1InteropGenerateUnityEventBridge]", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropType", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropMember", StringComparison.Ordinal),
                "Generated starter should seed backend-neutral type/member examples and leave bridge generation opt-in.");
            Assert(
                localPropsExampleSource.Contains("<MonoGamePath>", StringComparison.Ordinal) &&
                localPropsExampleSource.Contains("<Il2CppGamePath>", StringComparison.Ordinal) &&
                localPropsExampleSource.Contains($"<{S1InteropPackageInfo.GeneratorsPackageSourceProperty}>", StringComparison.Ordinal) &&
                localPropsExampleSource.Contains($"<{S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty} Condition=\"'$({S1InteropPackageInfo.GeneratorsPackageSourceProperty})'!=''\">$({S1InteropPackageInfo.GeneratorsPackageSourceProperty});$({S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty})</{S1InteropPackageInfo.RestoreAdditionalProjectSourcesProperty}>", StringComparison.Ordinal) &&
                gitignoreSource.Contains("local.build.props", StringComparison.Ordinal),
                "Generated local path scaffolding should show both runtime game paths and the optional local generator package feed while keeping local.build.props ignored.");

            string packageSource = CreateLocalGeneratorPackageSource(tempRoot);
            File.WriteAllText(
                localPropsPath,
                localPropsExampleSource.Replace(
                    @"C:\Path\To\S1Interop\artifacts\packages",
                    packageSource,
                    StringComparison.Ordinal));
            ProcessResult restore = RunDotNet("restore", solutionPath, "--nologo", "-v:minimal");
            Assert(
                restore.ExitCode == 0,
                $"Generated local.build.props should feed the local generator package source to restore without extra command-line restore sources. Output: {restore.Output}");

            string monoGamePath = @"D:\SteamLibrary\steamapps\common\Schedule I_alternate";
            string il2CppGamePath = @"D:\SteamLibrary\steamapps\common\Schedule I_public";
            string monoMelonLoader = Path.Combine(monoGamePath, "MelonLoader", "net35", "MelonLoader.dll");
            string il2CppMelonLoader = Path.Combine(il2CppGamePath, "MelonLoader", "net6", "MelonLoader.dll");
            if (File.Exists(monoMelonLoader) || File.Exists(il2CppMelonLoader))
            {
                if (File.Exists(monoMelonLoader))
                {
                    ProcessResult monoScaffoldBuild = RunDotNet(
                        "build",
                        projectPath,
                        "--nologo",
                        "-v:minimal",
                        $"-p:MonoGamePath={monoGamePath}",
                        $"-p:RestoreAdditionalProjectSources={packageSource}");
                    Assert(monoScaffoldBuild.ExitCode == 0, $"Generated backend-neutral MelonLoader scaffold should build against the local Mono game path. Output: {monoScaffoldBuild.Output}");
                }

                if (File.Exists(il2CppMelonLoader))
                {
                    ProcessResult il2CppScaffoldBuild = RunDotNet(
                        "build",
                        projectPath,
                        "--nologo",
                        "-v:minimal",
                        "-p:S1InteropReferenceRuntime=Il2Cpp",
                        $"-p:Il2CppGamePath={il2CppGamePath}",
                        $"-p:RestoreAdditionalProjectSources={packageSource}");
                    Assert(il2CppScaffoldBuild.ExitCode == 0, $"Generated backend-neutral MelonLoader scaffold should build against the local IL2CPP game path without changing source. Output: {il2CppScaffoldBuild.Output}");
                }
            }

            ProcessResult secondApply = RunCli("new", targetDirectory, "--apply");
            Assert(secondApply.ExitCode == 2, $"s1interop new should refuse to overwrite a non-empty target. Output: {secondApply.Output}");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void NewCommandProjectCanSeedFullBackendNeutralSdkFromReferenceMetadata()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string targetDirectory = Path.Combine(tempRoot, "Fresh Sdk Mod");
            string projectName = "FreshSdkMod";
            string projectPath = Path.Combine(targetDirectory, $"{projectName}.csproj");
            string gameRoot = Path.Combine(tempRoot, "SyntheticScheduleI");
            string managedPath = Path.Combine(gameRoot, "Schedule I_Data", "Managed");
            string assemblyPath = Path.Combine(managedPath, "Assembly-CSharp.dll");
            string generatedFacade = Path.Combine(targetDirectory, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WriteAssemblyFromSource(
                assemblyPath,
                "Assembly-CSharp",
                """
                namespace ScheduleOne
                {
                    public sealed class GameManager
                    {
                    }
                }

                namespace ScheduleOne.PlayerScripts
                {
                    public sealed class Player
                    {
                    }
                }

                namespace ScheduleOne.UI.Phone
                {
                    public sealed class Phone
                    {
                    }
                }

                namespace ScheduleOne.DevUtilities
                {
                    public sealed class Phone
                    {
                    }
                }
                """);

            ProcessResult create = RunCli("new", targetDirectory, "--apply");
            Assert(create.ExitCode == 0, $"s1interop new should create the backend-neutral scaffold before SDK seeding. Output: {create.Output}");
            File.WriteAllText(
                Path.Combine(targetDirectory, "local.build.props"),
                $$"""
                <Project>
                  <PropertyGroup>
                    <MonoGamePath>{{gameRoot}}</MonoGamePath>
                  </PropertyGroup>
                </Project>
                """);

            ProcessResult dryRun = RunCli("sdkgen", projectPath, "--full-sdk");
            Assert(
                dryRun.ExitCode == 0 &&
                dryRun.Output.Contains("generate_full_sdk_facade", StringComparison.Ordinal),
                $"sdkgen --full-sdk dry-run should plan full SDK generation for a blank backend-neutral project with game references. Output: {dryRun.Output}");
            Assert(!File.Exists(generatedFacade), "sdkgen --full-sdk dry-run should not write the generated facade.");

            ProcessResult apply = RunCli("sdkgen", projectPath, "--full-sdk", "--apply");
            Assert(apply.ExitCode == 0, $"sdkgen --full-sdk --apply should seed the generated SDK. Output: {apply.Output}");
            Assert(File.Exists(generatedFacade), "sdkgen --full-sdk should write the generated SDK facade file.");

            string source = File.ReadAllText(generatedFacade);
            Assert(
                source.Contains("[assembly: S1Interop.S1InteropNamespace(\"ScheduleOne\", IncludeSubnamespaces = true)]", StringComparison.Ordinal) &&
                !source.Contains("[assembly: S1Interop.S1InteropType(", StringComparison.Ordinal) &&
                !source.Contains("global using Player =", StringComparison.Ordinal),
                "Full SDK generation should emit a compact backend-neutral namespace declaration for reference metadata and avoid broad global aliases.");

            ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(
                source,
                [MetadataReference.CreateFromFile(assemblyPath)]);
            Assert(
                diagnostics.All(diagnostic => diagnostic.Id != "S1I001"),
                $"Generated full SDK declarations should validate against the referenced game assembly. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void VerifyMigrationCanIncludeSourceMigrationsInSandbox()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "SourceMigrationMod.csproj");
            string tempSource = Path.Combine(tempRoot, "UiBindings.cs");
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
                tempSource,
                """
                using UnityEngine.UI;

                namespace SourceMigrationMod;

                public static class UiBindings
                {
                    public static void Bind(Button button)
                    {
                        button.onClick.AddListener(OnClicked);
                    }

                    private static void OnClicked() { }
                }
                """);

            string originalSourceHash = ComputeSha256(tempSource);

            MigrationVerificationResult defaultResult = new MigrationVerifier().Verify(tempProject);
            Assert(defaultResult.Success, "Source-only verifier baseline should pass without requiring source migrations.");
            Assert(defaultResult.PlannedOperations == 0, $"Default verify-migration should not plan advisory source migrations, planned {defaultResult.PlannedOperations}.");
            Assert(defaultResult.AppliedOperations == 0, $"Default verify-migration should not apply advisory source migrations, applied {defaultResult.AppliedOperations}.");

            MigrationVerificationResult sourceResult = new MigrationVerifier().Verify(
                tempProject,
                new MigrationVerifierOptions(DualRuntime: false, IncludeSourceMigrations: true));

            Assert(sourceResult.Success, "Source migration verification should pass after sandboxed source rewrites.");
            Assert(
                sourceResult.PlannedOperations == 2,
                $"Source migration verification should plan bridge generation and listener rewrite, planned {sourceResult.PlannedOperations}.");
            Assert(
                sourceResult.AppliedOperations == 2,
                $"Source migration verification should apply bridge generation and listener rewrite, applied {sourceResult.AppliedOperations}.");
            Assert(sourceResult.SandboxDeleted, "Source migration verification should delete its sandbox.");
            Assert(
                string.Equals(ComputeSha256(tempSource), originalSourceHash, StringComparison.Ordinal),
                "Source migration verification should not mutate the real source file.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void EmployeeTweaksPackageReferencesAreRuntimeEvidence()
    {
        ProjectAnalysis project = AnalyzeProject(@"s1-employeetweaks\EmployeeTweaks.csproj");
        ConfigurationAnalysis mono = GetConfiguration(project, "Mono");
        ConfigurationAnalysis il2Cpp = GetConfiguration(project, "IL2CPP");

        AssertHasRuntime(project, "Mono", RuntimeKind.Mono);
        AssertHasRuntime(project, "IL2CPP", RuntimeKind.Il2Cpp);
        Assert(
            mono.PackageReferences.Any(package =>
                package.Include == "RefGen.Schedule-I.Mono" &&
                package.Version == "0.4.5-f1"),
            "EmployeeTweaks Mono config should expose its RefGen.Schedule-I.Mono package reference.");
        Assert(
            il2Cpp.PackageReferences.Any(package =>
                package.Include == "RefGen.Schedule-I.Il2Cpp" &&
                package.Version == "0.4.5-f1"),
            "EmployeeTweaks IL2CPP config should expose its RefGen.Schedule-I.Il2Cpp package reference.");
        Assert(
            mono.PackageReferences.All(package => package.Include != "RefGen.Schedule-I.Il2Cpp"),
            "EmployeeTweaks Mono config should not include the IL2CPP-only RefGen package.");
        Assert(
            il2Cpp.PackageReferences.All(package => package.Include != "RefGen.Schedule-I.Mono"),
            "EmployeeTweaks IL2CPP config should not include the Mono-only RefGen package.");
        Assert(
            mono.Evidence.Any(evidence => evidence.Contains("Mono package RefGen.Schedule-I.Mono", StringComparison.Ordinal)),
            "EmployeeTweaks Mono runtime evidence should include its RefGen package.");
        Assert(
            il2Cpp.Evidence.Any(evidence => evidence.Contains("IL2CPP package RefGen.Schedule-I.Il2Cpp", StringComparison.Ordinal)),
            "EmployeeTweaks IL2CPP runtime evidence should include its RefGen package.");
    }

    private void EmployeeTweaksReportsHarmonyOverloadBindingRisk()
    {
        ProjectAnalysis project = AnalyzeProject(@"s1-employeetweaks\EmployeeTweaks.csproj");

        Assert(
            project.SourceInterop?.SourceRisks.Any(risk =>
                risk.Kind == "HarmonyOverloadBinding" &&
                risk.FilePath.EndsWith(@"Patches\Unpackaging\MoveItemBehaviourPatches.cs", StringComparison.OrdinalIgnoreCase) &&
                risk.Remediation.Contains("ParameterTypeNames", StringComparison.Ordinal)) == true,
            "EmployeeTweaks should report manual AccessTools.Method overload binding as generated method-target guidance.");

        MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(project.ProjectPath, [project]));
        ProjectMigrationPlan projectPlan = plan.Projects.Single();
        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "generate_harmony_method_targets"),
            "EmployeeTweaks should plan generated Harmony method targets for MoveItemBehaviour overload bindings.");
        Assert(
            projectPlan.Operations.Any(operation =>
                operation.RuleId == "rewrite_harmony_overload_bindings" &&
                operation.FilePath.EndsWith(@"Patches\Unpackaging\MoveItemBehaviourPatches.cs", StringComparison.OrdinalIgnoreCase)),
            "EmployeeTweaks should plan a source rewrite for MoveItemBehaviour AccessTools.Method overload bindings.");
    }
}
