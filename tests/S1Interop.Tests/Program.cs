using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using S1Interop.Core;
using S1Interop.Core.Generators;
using S1Interop.Generators;
using CoreDiagnosticSeverity = S1Interop.Core.DiagnosticSeverity;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

var tests = new S1InteropFixtureTests();
string mode = args.FirstOrDefault() ?? "--all";
int count = mode switch
{
    "--portable" => tests.RunPortable(),
    "--integration" => tests.RunIntegration(requireWorkspace: true),
    "--all" => tests.RunAll(),
    _ => throw new ArgumentException($"Unknown test mode '{mode}'. Expected --all, --portable, or --integration.")
};
Console.WriteLine($"S1Interop fixture tests passed ({count} executed).");

internal sealed class S1InteropFixtureTests
{
    private readonly WorkspaceAnalyzer analyzer = new();
    private string? workspaceRoot;

    private string WorkspaceRoot
    {
        get
        {
            if (!TryGetWorkspaceRoot(out string? root) || root is null)
            {
                throw new DirectoryNotFoundException("Could not locate the broader ScheduleOne workspace required by integration tests.");
            }

            return root;
        }
    }

    public int RunAll()
    {
        int count = RunPortable();
        count += RunIntegration(requireWorkspace: false);
        return count;
    }

    public int RunPortable()
    {
        int count = 0;
        SourceInteropAnalyzerReportsIl2CppSourceRisks();
        count++;
        SourceInteropAnalyzerReportsHarmonyOverloadBindingRisk();
        count++;
        MemberAccessFallbackRewriterRewritesSimpleTypedGetter();
        count++;
        MemberAccessFallbackRewriterRewritesNullableValueGetter();
        count++;
        MemberAccessFallbackRewriterRewritesDynamicNamedHelpers();
        count++;
        SourceInteropAnalyzerReportsFieldPropertyReflectionFallbackRisk();
        count++;
        MigrationRewritesDynamicInstanceReflectionFallbackWithoutTargets();
        count++;
        SourceInteropAnalyzerReportsDirectMemberReflectionLookupRisk();
        count++;
        DirectMemberReflectionLookupRewriterRewritesSplitAndCachedAssignments();
        count++;
        MigrationApplyAndRollbackRewritesHarmonyOverloadBindings();
        count++;
        SourceInteropAnalyzerDoesNotReportRuntimeGuardedSourceRisks();
        count++;
        MigrationApplyAndRollbackRewritesUnityEventListeners();
        count++;
        MigrationApplyAndRollbackRewritesSimpleDelegateAssignments();
        count++;
        MigrationApplyAndRollbackRewritesDelegateArguments();
        count++;
        MigrationApplyAndRollbackRewritesIl2CppObjectCasts();
        count++;
        InitCommandApplyAndRollbackCreatesBackendNeutralStarter();
        count++;
        NewCommandCreatesBackendNeutralProjectScaffold();
        count++;
        MigrationApplyAndRollbackGeneratesSourceRiskReport();
        count++;
        VerifyMigrationCanIncludeSourceMigrationsInSandbox();
        count++;
        MsBuildOsPlatformConditionsAreEvaluated();
        count++;
        LocalPathDiagnosticsDetectAnyWindowsDriveLetter();
        count++;
        MigrationPreservesLocalPropsUnderOsConditionedGameDir();
        count++;
        MigrationApplyAndRollbackScaffoldsLocalReferenceProperties();
        count++;
        MigrationRepairsExistingLocalBuildPropsImport();
        count++;
        RuntimeDefineMigrationUsesDiagnosticDefineEvidenceForLegacyPlatformGroups();
        count++;
        DualRuntimeGeneratedUsingGuardsAddMonoDefinesForLegacyNames();
        count++;
        DualRuntimeMigrationScaffoldsAnalyzerInferredDebugReleaseMonoProject();
        count++;
        DualRuntimeMigrationPreservesSpaceContainingMonoConfigurationNames();
        count++;
        DualRuntimeMigrationConditionsImportedMonoRuntimeFlag();
        count++;
        DualRuntimeMigrationInstallsGeneratorPackageWhenAttributesAreDeclared();
        count++;
        DualRuntimeMigrationDoesNotReprefixExistingIl2CppNamedConfigurations();
        count++;
        ExplicitIl2CppConfigurationNameWinsOverSharedMonoReferences();
        count++;
        MigrationTargetFrameworkOverrideWinsAfterImportedProps();
        count++;
        VerifyMigrationSupportsWorkspaceDirectories();
        count++;
        WorkspaceAnalysisSkipsEditorMetadataDirectories();
        count++;
        VerifyMigrationBuildGateBuildsSandboxConfigurations();
        count++;
        VerifyMigrationBuildGateStagesLocalS1InteropGeneratorPackage();
        count++;
        VerifyMigrationBuildGatePreservesAncestorNuGetConfig();
        count++;
        CliReporterPrintsPackageRestoreSources();
        count++;
        VerifyMigrationBuildGateFailsCompilerBrokenSandbox();
        count++;
        VerifyMigrationBuildGateReportsMissingHintPathReadiness();
        count++;
        VerifyMigrationBuildGateClassifiesExternalReferenceSurfaceFailures();
        count++;
        VerifyMigrationBuildGateClassifiesExternalMemberSurfaceFailures();
        count++;
        VerifyMigrationBuildGateClassifiesIl2CppMemberSurfaceFailuresAsMigrationIssues();
        count++;
        VerifyMigrationBuildGateReportsUnsetLocalReferenceProperties();
        count++;
        VerifyMigrationBuildGateClassifiesSiblingBinReferencesAsModDependencies();
        count++;
        VerifyMigrationBuildGatePreservesProjectLocalDependencyDlls();
        count++;
        VerifyMigrationBuildGateClassifiesMissingTransitiveExternalAssembly();
        count++;
        VerifyMigrationBuildGatePassesRuntimeGameRootsToMsBuild();
        count++;
        VerifyMigrationBuildGateHydratesModDependencyProperties();
        count++;
        VerifyMigrationBuildGateStagesConfigurationScopedFileDependencyProperties();
        count++;
        VerifyMigrationBuildGateStagesProjectLocalRuntimeReferenceFolders();
        count++;
        MigrationVerifierSkipsWindowsReservedDeviceNames();
        count++;
        VerifyMigrationReportsResidualDiagnosticsOnBrokenInjectedType();
        count++;
        MigrationApplyAddsIntPtrConstructorToMonoBehaviourInjectedType();
        count++;
        MigrationApplyRegistersMonoOnlyInjectedComponentTypes();
        count++;
        BuildHookInstallsReversibleValidationTarget();
        count++;
        BuildHookFailsBuildForResidualInteropDiagnostics();
        count++;
        BuildHookValidatesOnlyActiveConfiguration();
        count++;
        SourceInteropAnalyzerIgnoresGeneratedAndToolDirectories();
        count++;
        ScheduleOneUsingRewriterGroupsAdjacentUsings();
        count++;
        ScheduleOneUsingRewriterCanPreferGlobalFacade();
        count++;
        S1InteropTypeRegistryGeneratorProducesBackendSpecificReflectionCache();
        count++;
        BackendNeutralTypeRegistryExecutesAgainstIl2CppLikeTypes();
        count++;
        S1InteropGeneratorProducesCompileTimeEventBridges();
        count++;
        SdkFacadeAliasesFullyQualifiedScheduleOneTypes();
        count++;
        DuplicateLangVersionUsesEffectiveLastValueForSdkFacadeSupport();
        count++;
        MigrationApplyAndRollbackRewritesFullyQualifiedScheduleOneTypes();
        count++;
        HideFromIl2CppMigrationHandlesMultipleTargetsAndOverloads();
        count++;
        return count;
    }

    public int RunIntegration(bool requireWorkspace)
    {
        if (!TryGetWorkspaceRoot(out _))
        {
            if (requireWorkspace)
            {
                throw new DirectoryNotFoundException("Could not locate the broader ScheduleOne workspace required by --integration tests.");
            }

            Console.WriteLine("Skipping local integration fixtures because the broader ScheduleOne workspace was not found.");
            return 0;
        }

        int count = 0;
        AlwaysJackpotHasDualRuntimeShapeAndIl2CppFrameworkDiagnostic();
        count++;
        JackpotEveryTimeReportsManagedIl2CppSurface();
        count++;
        GunsAlwaysAccurateIsRecognizedAsCleanDualRuntimeBaseline();
        count++;
        DedicatedServerModEffectiveConfigurationConditionsAreEvaluated();
        count++;
        OverTheCounterStartsWithConditionsAreEvaluated();
        count++;
        OverTheCounterReportsHarmonyTranspilerRisk();
        count++;
        EmployeeTweaksPackageReferencesAreRuntimeEvidence();
        count++;
        EmployeeTweaksReportsHarmonyOverloadBindingRisk();
        count++;
        VerifyMigrationSupportsLegacyConfigurationPlatformConditions();
        count++;
        BetterJukeboxReportsMissingRuntimeDefines();
        count++;
        S1FuelModInjectedTypesAreAnalyzed();
        count++;
        BackendNeutralRegistryCompilesRealS1FuelModFacadeTargets();
        count++;
        SdkFacadeGeneratorDetectsBarsGraphicsBackendAliasPairs();
        count++;
        MigrationApplyAndRollbackAddsHideFromIl2CppOnS1FuelModFixture();
        count++;
        MigrationApplyConditionalizesS1FuelModGameConstructor();
        count++;
        VerifyMigrationSucceedsOnS1FuelModWithoutMutatingSource();
        count++;
        VerifyMigrationConvergesOnMonoOnlyS1FuelModCopy();
        count++;
        VerifyMigrationCleansBigWillyPropertyBasedReferences();
        count++;
        VerifyMigrationMovesBetterJukeboxAbsoluteHintPaths();
        count++;
        VerifyMigrationPreservesBguiMixedConfigurationPaths();
        count++;
        VerifyMigrationConvergesOnHoverboardWithoutMutatingSource();
        count++;
        VerifyMigrationBuildGateConvertsMonoOnlyBotanistFixCopy();
        count++;
        VerifyMigrationBuildGateConvertsMonoOnlyS1VoiceChatCopy();
        count++;
        VerifyMigrationBuildGateConvertsRealBarsGraphics();
        count++;
        VerifyMigrationMovesGameRootModDependencyHintPaths();
        count++;
        VerifyMigrationHandlesIterativeRuntimeDefineFixes();
        count++;
        S1DockExportsCrossCompatIsNotForcedToIl2CppFramework();
        count++;
        VerifyMigrationMovesAbsoluteSiblingDllHintPaths();
        count++;
        MigrationApplyReplacesStalePublicizedReferenceWithPublicizer();
        count++;
        VerifyMigrationBuildGateCollapsesStagedIl2CppWrapperReferences();
        count++;
        SdkFacadeGeneratorDetectsGunsAlwaysAccurateNamespaces();
        count++;
        SdkFacadeMigrationRequiresCSharp10ForDefaultLangVersionProjects();
        count++;
        DuplicateLangVersionRealModsDoNotRequireCSharp10Migration();
        count++;
        MigrationPlannerCreatesOperationsForBrokenFixture();
        count++;
        MigrationApplyAndRollbackWorkOnCopiedFixture();
        count++;
        MigrationApplyAndRollbackFixRuntimeDefinesOnCopiedFixture();
        count++;
        DualRuntimeMigrationScaffoldsS1DsPlayerListFixture();
        count++;
        DualRuntimeMigrationAddsGeneratedMonoGuardDefines();
        count++;
        return count;
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
                    private static object windowRect = new();
                    private static object pipelineAsset = new();
                    private static void OnClicked() { }
                    private static void DrawWindow(int id) { }
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
                source.SourceRisks.Any(risk => risk.Kind == "ManagedCollectionSignatureInterop"),
                "Source analyzer should report managed collection signature interop risk.");
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
            string reportPath = Path.Combine(tempRoot, "S1Interop.Generated", SourceRiskReportGenerator.ReportFileName);
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
                risks.Count(risk => risk.Kind == "FieldPropertyReflectionFallback") == 3,
                "Source analyzer should report field-first and property-first reflection fallbacks while ignoring the runtime-guarded fallback.");
            Assert(
                risks.Where(risk => risk.Kind == "FieldPropertyReflectionFallback").All(risk => risk.Remediation.Contains("S1InteropMember", StringComparison.Ordinal)),
                "Reflection fallback risk should point developers toward generated S1InteropMember accessors.");

            MigrationPlan plan = new MigrationPlanner().Plan(new WorkspaceAnalysis(tempProject, [project]));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "source_risk_field_property_reflection_fallback" &&
                    !operation.Automatic),
                "Migration plan should surface field/property reflection fallback as manual generated-accessor guidance.");
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
                applyResult.Operations.Any(operation => operation.RuleId == "generate_source_risk_report"),
                "Migration apply should create a source-risk report for reflection fallback guidance.");
            Assert(File.Exists(targetPath), "Generated member-access target declarations were not written.");
            Assert(File.Exists(reportPath), "Source-risk report was not written for reflection fallback guidance.");

            string generatedTargets = File.ReadAllText(targetPath);
            Assert(
                generatedTargets.Contains("[assembly: S1Interop.S1InteropType(\"GameOffenceNotice\", Alias = \"GameOffenceNotice\")]", StringComparison.Ordinal) &&
                generatedTargets.Contains("[assembly: S1Interop.S1InteropMember(\"GameOffenceNotice\", \"container\", Alias = \"container\")]", StringComparison.Ordinal),
                "Generated member-access targets should include typed owner/member declarations.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                migratedSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry.Getcontainer<GameObject>(notice);", StringComparison.Ordinal) &&
                !migratedSource.Contains("typeof(GameOffenceNotice).GetField(\"container\"", StringComparison.Ordinal),
                "Simple typed fallback getter should be rewritten through S1InteropMemberRegistry.");
            Assert(
                migratedSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry.GetInstanceValue(notice, \"container\");", StringComparison.Ordinal) &&
                !migratedSource.Contains("notice.GetType().GetField(\"container\"", StringComparison.Ordinal),
                "Simple dynamic object fallback getter should be rewritten through the backend-neutral instance member registry.");

            string report = File.ReadAllText(reportPath);
            Assert(
                report.Contains("Field Property Reflection Fallback", StringComparison.Ordinal) &&
                report.Contains("S1InteropMember", StringComparison.Ordinal),
                "Source-risk report should include field/property reflection fallback guidance.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback should restore the generator package reference change.");
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback should restore the rewritten source file.");
            Assert(rollbackResult.RemovedFiles.Contains(targetPath), "Rollback should remove generated member-access target declarations.");
            Assert(rollbackResult.RemovedFiles.Contains(reportPath), "Rollback should remove the generated source-risk report.");
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
                }
                """);

            ProjectAnalysis project = new WorkspaceAnalyzer().Analyze(tempProject).Projects.Single();
            SourceRisk[] risks = project.SourceInterop!.SourceRisks.ToArray();
            Assert(
                risks.Count(risk => risk.Kind == "DirectMemberReflectionLookup") == 2,
                $"Source analyzer should report direct typeof(...).GetField/GetProperty lookups as generated member-target guidance. Risks:{Environment.NewLine}{string.Join(Environment.NewLine, risks.Select(risk => $"{risk.Kind}: {risk.Evidence}"))}");
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
                generatedTargets.Contains("[assembly: S1Interop.S1InteropMember(\"MelonEnvironment\", \"UserDataDirectory\", Alias = \"UserDataDirectory\", Kind = S1Interop.S1InteropMemberKind.Property, IsStatic = true)]", StringComparison.Ordinal),
                "Generated member-access targets should include direct member reflection declarations and static metadata.");
            string rewrittenSource = File.ReadAllText(tempSource);
            Assert(
                rewrittenSource.Contains("return S1Interop.Generated.S1InteropMemberRegistry._homeScreenInstanceFieldInfo;", StringComparison.Ordinal) &&
                rewrittenSource.Contains("var property = S1Interop.Generated.S1InteropMemberRegistry.UserDataDirectoryPropertyInfo;", StringComparison.Ordinal),
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
            rewritten.Contains("return S1Interop.Generated.S1InteropMemberRegistry.Getcontainer<GameObject>(notice);", StringComparison.Ordinal),
            $"Simple typed fallback getter should rewrite through the generated member registry. Rewritten source:{Environment.NewLine}{rewritten}");
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
            rewritten.Contains("return S1Interop.Generated.S1InteropMemberRegistry.GetrenderScaleValue<float>(asset);", StringComparison.Ordinal),
            $"Nullable value fallback getter should rewrite through the generated member registry value accessor. Rewritten source:{Environment.NewLine}{rewritten}");
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
                risks.All(risk => risk.Kind != "Il2CppByteBufferInterop"),
                "Runtime-guarded IL2CPP packet buffers should not be reported.");
            Assert(
                risks.All(risk => risk.Kind != "ManagedCollectionSignatureInterop"),
                "Runtime-guarded managed collection signatures should not be reported.");
            Assert(
                risks.All(risk => risk.Kind != "Il2CppObjectCastInterop"),
                "Runtime-guarded IL2CPP object TryCast branches should not be reported.");
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

                        return false;
                    }
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
            string cliProject = Path.Combine(WorkspaceRoot, "S1Interop", "src", "S1Interop.Cli", "S1Interop.Cli.csproj");
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

            ProcessResult dryRun = RunDotNet("run", "--project", cliProject, "--", "init", tempProject);
            Assert(dryRun.ExitCode == 0, $"s1interop init dry-run should succeed. Output: {dryRun.Output}");
            Assert(
                dryRun.Output.Contains("install_s1interop_generator_package", StringComparison.Ordinal) &&
                dryRun.Output.Contains("generate_backend_neutral_starter", StringComparison.Ordinal),
                $"s1interop init dry-run should plan generator install and starter generation. Output: {dryRun.Output}");
            Assert(!File.Exists(starterPath), "s1interop init dry-run should not write the starter file.");

            ProcessResult apply = RunDotNet("run", "--project", cliProject, "--", "init", tempProject, "--apply");
            Assert(apply.ExitCode == 0, $"s1interop init --apply should succeed. Output: {apply.Output}");
            Assert(File.Exists(starterPath), "s1interop init --apply should create the backend-neutral starter file.");

            string projectSource = File.ReadAllText(tempProject);
            string starterSource = File.ReadAllText(starterPath);
            Assert(
                projectSource.Contains("S1Interop.Generators", StringComparison.Ordinal) &&
                projectSource.Contains("PrivateAssets", StringComparison.Ordinal),
                "s1interop init should install the S1Interop generator package privately.");
            Assert(
                starterSource.Contains("S1InteropGenerateUnityEventBridge", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropGenerateDelegateEventBridge", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropType", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropMember", StringComparison.Ordinal),
                "Backend-neutral starter should seed generator bridge attributes and editable type/member examples.");

            string manifestPath = ExtractManifestPath(apply.Output);
            ProcessResult rollback = RunDotNet("run", "--project", cliProject, "--", "migrate", "rollback", manifestPath);
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
            string projectPath = Path.Combine(targetDirectory, $"{projectName}.csproj");
            string corePath = Path.Combine(targetDirectory, "ModCore.cs");
            string starterPath = Path.Combine(targetDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
            string cliProject = Path.Combine(WorkspaceRoot, "S1Interop", "src", "S1Interop.Cli", "S1Interop.Cli.csproj");

            ProcessResult dryRun = RunDotNet("run", "--project", cliProject, "--", "new", targetDirectory);
            Assert(dryRun.ExitCode == 0, $"s1interop new dry-run should succeed. Output: {dryRun.Output}");
            Assert(
                dryRun.Output.Contains("S1Interop new project dry-run", StringComparison.Ordinal) &&
                dryRun.Output.Contains(projectPath, StringComparison.Ordinal) &&
                dryRun.Output.Contains(starterPath, StringComparison.Ordinal),
                $"s1interop new dry-run should print planned scaffold files. Output: {dryRun.Output}");
            Assert(!Directory.Exists(targetDirectory), "s1interop new dry-run should not create the target directory.");

            ProcessResult apply = RunDotNet("run", "--project", cliProject, "--", "new", targetDirectory, "--apply");
            Assert(apply.ExitCode == 0, $"s1interop new --apply should succeed. Output: {apply.Output}");
            Assert(File.Exists(projectPath), "s1interop new should create the project file.");
            Assert(File.Exists(corePath), "s1interop new should create the core source file.");
            Assert(File.Exists(starterPath), "s1interop new should create the backend-neutral starter file.");

            string projectSource = File.ReadAllText(projectPath);
            string coreSource = File.ReadAllText(corePath);
            string starterSource = File.ReadAllText(starterPath);
            Assert(
                projectSource.Contains("<TargetFramework>netstandard2.1</TargetFramework>", StringComparison.Ordinal) &&
                projectSource.Contains("<LangVersion>10.0</LangVersion>", StringComparison.Ordinal) &&
                projectSource.Contains("S1Interop.Generators", StringComparison.Ordinal) &&
                projectSource.Contains("PrivateAssets=\"all\"", StringComparison.Ordinal),
                "Generated project should target netstandard2.1, enable C# 10, and install S1Interop.Generators privately.");
            Assert(
                coreSource.Contains("namespace FreshNeutralMod;", StringComparison.Ordinal) &&
                coreSource.Contains("public const string ModName = \"FreshNeutralMod\";", StringComparison.Ordinal),
                "Generated core source should use a sanitized project namespace/name.");
            Assert(
                starterSource.Contains("S1InteropGenerateUnityEventBridge", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropType", StringComparison.Ordinal) &&
                starterSource.Contains("S1InteropMember", StringComparison.Ordinal),
                "Generated starter should seed backend-neutral generator attributes and examples.");

            ProcessResult secondApply = RunDotNet("run", "--project", cliProject, "--", "new", targetDirectory, "--apply");
            Assert(secondApply.ExitCode == 2, $"s1interop new should refuse to overwrite a non-empty target. Output: {secondApply.Output}");
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
            string originalSource = File.ReadAllText(tempFuelVehicleData);

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
            Assert(
                string.Equals(File.ReadAllText(tempFuelVehicleData), originalSource, StringComparison.Ordinal),
                "Rollback should restore the original FuelVehicleData source.");
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
                generated.Contains("public static S1InteropRuntimeBackend Backend => cachedBackend ??= DetectBackend();", StringComparison.Ordinal),
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

    private void VerifyMigrationConvergesOnMonoOnlyS1FuelModCopy()
    {
        string sourceDirectory = Path.Combine(WorkspaceRoot, "S1FuelMod");
        string sourceProject = Path.Combine(sourceDirectory, "S1FuelMod.csproj");
        string sourceFuelVehicleData = Path.Combine(sourceDirectory, "Systems", "FuelVehicleData.cs");
        if (!File.Exists(sourceProject) || !File.Exists(sourceFuelVehicleData))
        {
            Console.WriteLine("Skipping S1FuelMod Mono-only migration integration because S1FuelMod is not available.");
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
                    IncludeSourceMigrations: true));

            Assert(
                result.Success,
                $"Mono-only S1FuelMod copy should converge to a dual-runtime migration in a sandbox. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
            Assert(result.PlannedOperations > 0, "Mono-only S1FuelMod migration should plan sandbox operations.");
            Assert(result.AppliedOperations > 0, "Mono-only S1FuelMod migration should apply sandbox operations.");
            Assert(result.SandboxDeleted, "S1FuelMod migration verification should delete its sandbox.");
            Assert(result.AfterDiagnostics.Count == 0, $"S1FuelMod migration should leave no analyzer diagnostics. Residual: {FormatDiagnostics(result.AfterDiagnostics)}");
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
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.1" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
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

    private void ScheduleOneUsingRewriterGroupsAdjacentUsings()
    {
        string source =
            """
            using HarmonyLib;
            using ScheduleOne.DevUtilities;
            using ScheduleOne.Equipping;
            using PlayerAlias = ScheduleOne.PlayerScripts;
            using static ScheduleOne.UI.HUD;
            using UnityEngine;

            namespace SyntheticMod;
            """;

        string rewritten = ScheduleOneUsingRewriter.RewriteSource(source);

        Assert(CountOccurrences(rewritten, "#if MONO") == 1, "Adjacent ScheduleOne usings should share one MONO guard.");
        Assert(CountOccurrences(rewritten, "#elif IL2CPP") == 1, "Adjacent ScheduleOne usings should share one IL2CPP guard.");
        Assert(CountOccurrences(rewritten, "#endif") == 1, "Adjacent ScheduleOne usings should share one closing guard.");
        Assert(
            rewritten.Contains(
                """
                #if MONO
                using ScheduleOne.DevUtilities;
                using ScheduleOne.Equipping;
                using PlayerAlias = ScheduleOne.PlayerScripts;
                using static ScheduleOne.UI.HUD;
                #elif IL2CPP
                using Il2CppScheduleOne.DevUtilities;
                using Il2CppScheduleOne.Equipping;
                using PlayerAlias = Il2CppScheduleOne.PlayerScripts;
                using static Il2CppScheduleOne.UI.HUD;
                #endif
                """,
                StringComparison.Ordinal),
            $"Grouped ScheduleOne using block was not emitted as expected. Rewritten source:{Environment.NewLine}{rewritten}");
        Assert(
            rewritten.Contains("using HarmonyLib;", StringComparison.Ordinal) &&
            rewritten.Contains("using UnityEngine;", StringComparison.Ordinal),
            "Non-ScheduleOne usings should remain outside the generated runtime guard.");
    }

    private void ScheduleOneUsingRewriterCanPreferGlobalFacade()
    {
        string source =
            """
            using HarmonyLib;
            using ScheduleOne.DevUtilities;
            using ScheduleOne.Equipping;
            using PlayerAlias = ScheduleOne.PlayerScripts;
            using static ScheduleOne.UI.HUD;
            using UnityEngine;

            namespace SyntheticMod;
            """;

        string rewritten = ScheduleOneUsingRewriter.RewriteSource(
            source,
            ScheduleOneUsingRewriter.RewriteMode.PreferGlobalFacade);

        Assert(
            !rewritten.Contains("using ScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
            !rewritten.Contains("using Il2CppScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
            !rewritten.Contains("using ScheduleOne.Equipping;", StringComparison.Ordinal) &&
            !rewritten.Contains("using Il2CppScheduleOne.Equipping;", StringComparison.Ordinal),
            $"Normal ScheduleOne namespace usings should be removed when the global facade owns them. Rewritten source:{Environment.NewLine}{rewritten}");
        Assert(CountOccurrences(rewritten, "#if MONO") == 1, "Alias/static ScheduleOne usings should still share one MONO guard.");
        Assert(
            rewritten.Contains("using PlayerAlias = ScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
            rewritten.Contains("using PlayerAlias = Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
            rewritten.Contains("using static ScheduleOne.UI.HUD;", StringComparison.Ordinal) &&
            rewritten.Contains("using static Il2CppScheduleOne.UI.HUD;", StringComparison.Ordinal),
            "Alias and static ScheduleOne usings should remain source-level guarded because global namespace imports cannot replace them safely.");
    }

    private void S1InteropTypeRegistryGeneratorProducesBackendSpecificReflectionCache()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.UI.Phone.Phone", Alias = "Phone", Il2CppTypeName = "Il2CppScheduleOne.UI.Phone.Phone")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.NPCs.Behaviour.MoveItemBehaviour", Alias = "MoveItemBehaviour")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.Management.TransitRoute", Alias = "TransitRoute")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.ItemFramework.ItemInstance", Alias = "ItemInstance")]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "container", Alias = "NoticeContainer")]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "Instance", Alias = "PlayerCameraInstance", IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "_homeScreenInstance", Alias = "HomeScreenField", Kind = S1Interop.S1InteropMemberKind.Field)]
            [assembly: S1Interop.S1InteropMember("Phone", "StartUpdateVolume", Alias = "StartUpdateVolume", Kind = S1Interop.S1InteropMemberKind.Method)]
            [assembly: S1Interop.S1InteropMember("Phone", "Open", Alias = "OpenPhone", Kind = S1Interop.S1InteropMemberKind.Method, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Phone", "deviceUniqueIdentifier", Alias = "DeviceIdProperty", Kind = S1Interop.S1InteropMemberKind.Property, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("MoveItemBehaviour", "IsDestinationValid", Alias = "IsDestinationValid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "TransitRoute", "ItemInstance", "string&" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetPacket", Alias = "SetPacket", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "byte[]" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetLabels", Alias = "SetLabels", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "string[]" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetScores", Alias = "SetScores", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.Dictionary<string, int>" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetTags", Alias = "SetTags", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.HashSet<string>" })]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        string monoGenerated = RunTypeRegistryGenerator(source, "MONO");
        string il2CppGenerated = RunTypeRegistryGenerator(source, "IL2CPP");
        string runtimeGenerated = RunTypeRegistryGenerator(source);

        Assert(
            monoGenerated.Contains("public const S1InteropRuntimeBackend Backend = S1InteropRuntimeBackend.Mono;", StringComparison.Ordinal) &&
            monoGenerated.Contains("public const bool IsMono = true;", StringComparison.Ordinal),
            $"Mono generator output should expose Mono runtime constants. Generated source:{Environment.NewLine}{monoGenerated}");
        Assert(
            monoGenerated.Contains("public const string PlayerCameraName = \"ScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal),
            $"Mono generator output should keep Mono ScheduleOne type names. Generated source:{Environment.NewLine}{monoGenerated}");
        Assert(
            il2CppGenerated.Contains("public const S1InteropRuntimeBackend Backend = S1InteropRuntimeBackend.Il2Cpp;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public const bool IsIl2Cpp = true;", StringComparison.Ordinal),
            $"IL2CPP generator output should expose IL2CPP runtime constants. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string PlayerCameraName = \"Il2CppScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal),
            $"IL2CPP generator output should rewrite ScheduleOne type names to Il2CppScheduleOne. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string PhoneName = \"Il2CppScheduleOne.UI.Phone.Phone\";", StringComparison.Ordinal),
            $"IL2CPP generator output should respect explicit Il2CppTypeName overrides. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("private static readonly System.Collections.Generic.Dictionary<string, System.Type?> Cache", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("System.Type.GetType(runtimeTypeName, throwOnError: false)", StringComparison.Ordinal),
            "Generated type registry should include a compile-time generated reflection cache.");
        Assert(
            il2CppGenerated.Contains("internal static class S1InteropObjectCast", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool Is<T>(object? value, out T? result) where T : class", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? As<T>(object? value) where T : class", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("method.MakeGenericMethod(targetType)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", StringComparison.Ordinal),
            $"Generated type registry should include a backend-neutral object cast helper for IL2CPP TryCast<T> proxy unwrapping. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("internal static class S1InteropDelegateBridge", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static TDelegate Convert<TDelegate>(TDelegate listener) where TDelegate : class", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.DelegateSupport", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("method.MakeGenericMethod(delegateType)", StringComparison.Ordinal),
            $"Generated type registry should include a backend-neutral delegate bridge for reflected IL2CPP DelegateSupport conversion. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("ResolveFromLoadedAssemblies(runtimeTypeName)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("assembly.GetType(runtimeTypeName, throwOnError: false)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("string.Equals(type.Name, runtimeTypeName, System.StringComparison.Ordinal)", StringComparison.Ordinal),
            "Generated type registry should fall back to cached loaded-assembly lookup for simple generated migration type names.");
        Assert(
            il2CppGenerated.Contains("public const string NoticeContainerName = \"container\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static System.Reflection.FieldInfo? NoticeContainerFieldInfo => ResolveMember(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, parameterTypeNames: null, S1InteropMemberKind.Field) as System.Reflection.FieldInfo;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static System.Reflection.PropertyInfo? NoticeContainerPropertyInfo => ResolveMember(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, parameterTypeNames: null, S1InteropMemberKind.Property) as System.Reflection.PropertyInfo;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? GetNoticeContainer(object instance) => GetValue(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, instance, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetNoticeContainer<T>(object instance) where T : class => GetNoticeContainer(instance) as T;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetNoticeContainerValue<T>(object instance) where T : struct => GetNoticeContainer(instance) is T value ? value : (T?)null;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool TrySetNoticeContainer(object instance, object? value) => TrySetValue(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, instance, value, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal),
            $"Generated member registry should include field/property bridge helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string PlayerCameraInstanceName = \"Instance\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? GetPlayerCameraInstance() => GetValue(S1InteropTypeRegistry.PlayerCameraName, PlayerCameraInstanceName, null, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetPlayerCameraInstance<T>() where T : class => GetPlayerCameraInstance() as T;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetPlayerCameraInstanceValue<T>() where T : struct => GetPlayerCameraInstance() is T value ? value : (T?)null;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool TrySetPlayerCameraInstance(object? value) => TrySetValue(S1InteropTypeRegistry.PlayerCameraName, PlayerCameraInstanceName, null, value, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal),
            $"Generated member registry should include static field/property bridge helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static object? GetHomeScreenField(object instance) => GetValue(S1InteropTypeRegistry.PlayerCameraName, HomeScreenFieldName, instance, S1InteropMemberKind.Field);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? GetDeviceIdProperty() => GetValue(S1InteropTypeRegistry.PhoneName, DeviceIdPropertyName, null, S1InteropMemberKind.Property);", StringComparison.Ordinal),
            $"Generated member registry should honor exact field/property member kinds. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static System.Reflection.FieldInfo? HomeScreenFieldFieldInfo => ResolveMember(S1InteropTypeRegistry.PlayerCameraName, HomeScreenFieldName, parameterTypeNames: null, S1InteropMemberKind.Field) as System.Reflection.FieldInfo;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static System.Reflection.PropertyInfo? DeviceIdPropertyPropertyInfo => ResolveMember(S1InteropTypeRegistry.PhoneName, DeviceIdPropertyName, parameterTypeNames: null, S1InteropMemberKind.Property) as System.Reflection.PropertyInfo;", StringComparison.Ordinal),
            $"Generated member registry should expose exact typed member metadata accessors. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string StartUpdateVolumeName = \"StartUpdateVolume\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static System.Reflection.MethodInfo? StartUpdateVolumeMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, StartUpdateVolumeName, null);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeStartUpdateVolume(object? instance, params object?[] args) => Invoke(S1InteropTypeRegistry.PhoneName, StartUpdateVolumeName, null, instance, args);", StringComparison.Ordinal),
            $"Generated member registry should include method invoker helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string OpenPhoneName = \"Open\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeOpenPhone(params object?[] args) => Invoke(S1InteropTypeRegistry.PhoneName, OpenPhoneName, null, null, args);", StringComparison.Ordinal),
            $"Generated member registry should include static method invoker helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string MoveItemBehaviourName = \"Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static System.Reflection.MethodInfo? IsDestinationValidMethod => ResolveMethod(S1InteropTypeRegistry.MoveItemBehaviourName, IsDestinationValidName, new string[] { S1InteropTypeRegistry.TransitRouteName, S1InteropTypeRegistry.ItemInstanceName, \"string&\" });", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeIsDestinationValid(object? instance, params object?[] args) => Invoke(S1InteropTypeRegistry.MoveItemBehaviourName, IsDestinationValidName, new string[] { S1InteropTypeRegistry.TransitRouteName, S1InteropTypeRegistry.ItemInstanceName, \"string&\" }, instance, args);", StringComparison.Ordinal),
            $"Generated member registry should include overload-specific method invoker helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("case S1InteropMemberKind.Field:", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("case S1InteropMemberKind.Property:", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ownerType.GetProperty(memberName, AllBindings)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ownerType.GetField(memberName, AllBindings)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ownerType.GetMethod(memberName, AllBindings, binder: null, types: parameterTypes, modifiers: null)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("parameterType.MakeByRefType()", StringComparison.Ordinal),
            "Generated member registry should cache property, field, method overload, and by-ref lookup paths.");
        Assert(
            il2CppGenerated.Contains("public static object? GetInstanceValue(object? instance, string memberName)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("System.Reflection.MemberInfo? member = ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames: null, kind);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool TrySetInstanceValue(object? instance, string memberName, object? value, S1InteropMemberKind kind)", StringComparison.Ordinal),
            $"Generated member registry should include cached instance-type helpers for generic reflection code. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static bool TryConvertValue(object? value, System.Type targetType, out object? converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppGuid(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppList(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppHashSet(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppDictionary(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryGetDictionaryEntry(entry, out object? key, out object? entryValue)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppArray(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppSystem.Collections.Generic.List`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppSystem.Collections.Generic.HashSet`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppSystem.Collections.Generic.Dictionary`2", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("property.SetValue(instance, converted, null);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("field.SetValue(instance, converted);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("converted = System.Convert.ChangeType(value, conversionType, System.Globalization.CultureInfo.InvariantCulture)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("System.Enum.Parse(conversionType, text, ignoreCase: true)", StringComparison.Ordinal),
            $"Generated member registry should centralize value conversion before field/property writes. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("if (!TryConvertArguments(method.GetParameters(), args, out object?[] converted))", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("object? result = method.Invoke(instance, converted);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("CopyByRefArguments(method.GetParameters(), converted, args);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("System.Type conversionType = parameterType.IsByRef && parameterType.GetElementType() is System.Type elementType", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("args[index] = ConvertBackValue(args[index], converted[index]);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackGuid(object converted, out System.Guid guid)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackArray(System.Array original, object converted, out System.Array? managedArray)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackList(System.Collections.IList original, object converted, out System.Collections.IList? managedList)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackDictionary(object? original, object converted, out object? managedDictionary)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackHashSet(object? original, object converted, out object? managedHashSet)", StringComparison.Ordinal),
            $"Generated member registry should convert method invocation arguments and copy by-ref values back after reflection Invoke. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static object? InvokeInstance(object? instance, string memberName, params object?[] args)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeInstance(object? instance, string memberName, string[]? parameterTypeNames, params object?[] args)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames, S1InteropMemberKind.Method) as System.Reflection.MethodInfo", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static System.Reflection.MemberInfo? ResolveMemberCached(System.Type ownerType, string memberName, string[]? parameterTypeNames, S1InteropMemberKind kind)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("string ownerKey = ownerType.AssemblyQualifiedName ?? ownerType.FullName ?? ownerType.Name;", StringComparison.Ordinal),
            $"Generated member registry should include cached dynamic instance method invocation helpers for backend-neutral reflection wrappers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            runtimeGenerated.Contains("public static object? CreatePlayerCamera(params object?[] args) => Create(PlayerCameraName, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? CreatePlayerCamera<T>(params object?[] args) where T : class => CreatePlayerCamera(args) as T;", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? GetPlayerCameraStatic(string memberName) => S1InteropMemberRegistry.GetValue(PlayerCameraName, memberName, null);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool TrySetPlayerCameraStatic(string memberName, object? value) => S1InteropMemberRegistry.TrySetValue(PlayerCameraName, memberName, null, value);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCameraStatic(string methodName, params object?[] args) => S1InteropMemberRegistry.Invoke(PlayerCameraName, methodName, parameterTypeNames: null, null, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCameraStatic<T>(string methodName, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCameraStatic(methodName, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCameraStatic(string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.Invoke(PlayerCameraName, methodName, parameterTypeNames, null, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCameraStatic<T>(string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCameraStatic(methodName, parameterTypeNames, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsPlayerCamera(object? instance) => IsInstance(instance, PlayerCameraName);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? GetPlayerCamera(object? instance, string memberName) => S1InteropMemberRegistry.GetInstanceValue(instance, memberName);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool TrySetPlayerCamera(object? instance, string memberName, object? value) => S1InteropMemberRegistry.TrySetInstanceValue(instance, memberName, value);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCamera(object? instance, string methodName, params object?[] args) => S1InteropMemberRegistry.InvokeInstance(instance, methodName, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCamera<T>(object? instance, string methodName, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCamera(instance, methodName, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCamera(object? instance, string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.InvokeInstance(instance, methodName, parameterTypeNames, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCamera<T>(object? instance, string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCamera(instance, methodName, parameterTypeNames, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? Create(string runtimeTypeName, params object?[] args)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsInstance(object? instance, string runtimeTypeName)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("constructor.Invoke(converted)", StringComparison.Ordinal),
            $"Backend-neutral type registry should emit object-based type facade helpers that do not require compiling against backend-specific types. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static S1InteropRuntimeBackend Backend => cachedBackend ??= DetectBackend();", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsMono => Backend == S1InteropRuntimeBackend.Mono;", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsIl2Cpp => Backend == S1InteropRuntimeBackend.Il2Cpp;", StringComparison.Ordinal),
            $"Backend-neutral generator output should detect and cache the runtime backend. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public const string PlayerCameraMonoName = \"ScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public const string PlayerCameraIl2CppName = \"Il2CppScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static string PlayerCameraName => GetRuntimeTypeName(PlayerCameraMonoName, PlayerCameraIl2CppName);", StringComparison.Ordinal),
            $"Backend-neutral generator output should keep both backend type names and resolve the active one at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("if (S1InteropTypeRegistry.Resolve(\"Il2CppScheduleOne.PlayerScripts.PlayerCamera\") is not null)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("if (S1InteropTypeRegistry.Resolve(\"ScheduleOne.PlayerScripts.PlayerCamera\") is not null)", StringComparison.Ordinal),
            $"Backend-neutral generator output should probe known IL2CPP and Mono types. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static string GetRuntimeTypeName(string monoTypeName, string il2CppTypeName)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("S1InteropRuntime.Backend == S1InteropRuntimeBackend.Il2Cpp ? il2CppTypeName : monoTypeName", StringComparison.Ordinal),
            $"Backend-neutral generator output should expose runtime type-name selection for method parameter caches. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static System.Reflection.MethodInfo? IsDestinationValidMethod => ResolveMethod(S1InteropTypeRegistry.MoveItemBehaviourName, IsDestinationValidName, new string[] { S1InteropTypeRegistry.TransitRouteName, S1InteropTypeRegistry.ItemInstanceName, \"string&\" });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route alias parameter types through runtime-resolved names. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static T? InvokeIsDestinationValid<T>(object? instance, params object?[] args) => CastResult<T>(InvokeIsDestinationValid(instance, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? CastResult<T>(object? value)", StringComparison.Ordinal),
            $"Backend-neutral member registry should expose typed method invocation helpers for new backend-neutral projects. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static System.Reflection.MethodInfo? SetPacketMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetPacketName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"byte[]\", \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed array parameter names to IL2CPP array wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static System.Reflection.MethodInfo? SetLabelsMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetLabelsName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"string[]\", \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<string>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed reference-array parameter names to IL2CPP reference array wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static System.Reflection.MethodInfo? SetScoresMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetScoresName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"System.Collections.Generic.Dictionary<string, int>\", \"Il2CppSystem.Collections.Generic.Dictionary<string, int>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed dictionary parameter names to IL2CPP dictionary wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static System.Reflection.MethodInfo? SetTagsMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetTagsName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"System.Collections.Generic.HashSet<string>\", \"Il2CppSystem.Collections.Generic.HashSet<string>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed hash set parameter names to IL2CPP hash set wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
    }

    private void BackendNeutralTypeRegistryExecutesAgainstIl2CppLikeTypes()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.UI.HUD", Alias = "Hud", Il2CppTypeName = "Il2CppScheduleOne.UI.HUD")]
            [assembly: S1Interop.S1InteropMember("Hud", "Instance", Alias = "HudInstance", IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Hud", "Scale", Alias = "HudScale", Kind = S1Interop.S1InteropMemberKind.Field)]
            [assembly: S1Interop.S1InteropMember("Hud", "SetLevel", Alias = "HudSetLevel", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "int", "string&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "GetScaleText", Alias = "HudGetScaleText", Kind = S1Interop.S1InteropMemberKind.Method)]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteGuid", Alias = "HudRewriteGuid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Guid&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteNames", Alias = "HudRewriteNames", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.List<string>&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetData", Alias = "HudSetData", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Guid", "System.Collections.Generic.List<string>" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteBytes", Alias = "HudRewriteBytes", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "byte[]&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetBytes", Alias = "HudSetBytes", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "byte[]" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetLabels", Alias = "HudSetLabels", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "string[]" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteScores", Alias = "HudRewriteScores", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.Dictionary<string, int>&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteTags", Alias = "HudRewriteTags", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.HashSet<string>&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetScores", Alias = "HudSetScores", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.Dictionary<string, int>" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetTags", Alias = "HudSetTags", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.HashSet<string>" })]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }

            namespace Il2CppScheduleOne.UI
            {
                public sealed class HUD
                {
                    public static HUD Instance { get; } = new HUD();
                    public int Scale;
                    public string? LastName { get; private set; }
                    public string? LastGuid { get; private set; }
                    public string? LastRewriteNames { get; private set; }
                    public string? LastData { get; private set; }
                    public string? LastRewriteBytes { get; private set; }
                    public string? LastBytes { get; private set; }
                    public string? LastLabels { get; private set; }
                    public string? LastScores { get; private set; }
                    public string? LastTags { get; private set; }

                    public string SetLevel(int level, ref string name)
                    {
                        Scale = level;
                        name = "il2cpp:" + name;
                        LastName = name;
                        return "done";
                    }

                    public string GetScaleText()
                    {
                        return Scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    public string SetData(Il2CppSystem.Guid guid, Il2CppSystem.Collections.Generic.List<string> names)
                    {
                        LastData = guid.Value + ":" + names.Count + ":" + names[0];
                        return LastData;
                    }

                    public string RewriteGuid(ref Il2CppSystem.Guid guid)
                    {
                        guid = new Il2CppSystem.Guid("22222222-3333-4444-5555-666666666666");
                        LastGuid = guid.Value;
                        return LastGuid;
                    }

                    public string RewriteNames(ref Il2CppSystem.Collections.Generic.List<string> names)
                    {
                        names = new Il2CppSystem.Collections.Generic.List<string>();
                        names.Add("delta");
                        names.Add("echo");
                        LastRewriteNames = names.Count + ":" + names[0] + ":" + names[1];
                        return LastRewriteNames;
                    }

                    public string RewriteBytes(ref Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
                    {
                        bytes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(3);
                        bytes[0] = 4;
                        bytes[1] = 5;
                        bytes[2] = 6;
                        LastRewriteBytes = bytes.Length + ":" + bytes[0] + ":" + bytes[1] + ":" + bytes[2];
                        return LastRewriteBytes;
                    }

                    public string SetBytes(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
                    {
                        LastBytes = bytes.Length + ":" + bytes[0] + ":" + bytes[1];
                        return LastBytes;
                    }

                    public string SetLabels(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<string> labels)
                    {
                        LastLabels = labels.Length + ":" + labels[0] + ":" + labels[1];
                        return LastLabels;
                    }

                    public string SetScores(Il2CppSystem.Collections.Generic.Dictionary<string, int> scores)
                    {
                        LastScores = scores.Count + ":" + scores["north"] + ":" + scores["south"];
                        return LastScores;
                    }

                    public string RewriteScores(ref Il2CppSystem.Collections.Generic.Dictionary<string, int> scores)
                    {
                        scores = new Il2CppSystem.Collections.Generic.Dictionary<string, int>();
                        scores.Add("east", 12);
                        scores.Add("west", 15);
                        LastScores = scores.Count + ":" + scores["east"] + ":" + scores["west"];
                        return LastScores;
                    }

                    public string SetTags(Il2CppSystem.Collections.Generic.HashSet<string> tags)
                    {
                        LastTags = tags.Count + ":" + tags.Contains("north") + ":" + tags.Contains("south");
                        return LastTags;
                    }

                    public string RewriteTags(ref Il2CppSystem.Collections.Generic.HashSet<string> tags)
                    {
                        tags = new Il2CppSystem.Collections.Generic.HashSet<string>();
                        tags.Add("east");
                        tags.Add("west");
                        LastTags = tags.Count + ":" + tags.Contains("east") + ":" + tags.Contains("west");
                        return LastTags;
                    }
                }
            }

            namespace Il2CppSystem
            {
                public sealed class Guid
                {
                    public Guid(string value)
                    {
                        Value = value;
                    }

                    public string Value { get; }
                }

                namespace Collections.Generic
                {
                    public sealed class List<T>
                    {
                        private readonly System.Collections.Generic.List<T> inner = new System.Collections.Generic.List<T>();

                        public int Count => inner.Count;

                        public T this[int index] => inner[index];

                        public void Add(T value)
                        {
                            inner.Add(value);
                        }
                    }

                    public sealed class HashSet<T>
                    {
                        private readonly System.Collections.Generic.HashSet<T> inner = new System.Collections.Generic.HashSet<T>();

                        public int Count => inner.Count;

                        public bool Add(T value) => inner.Add(value);

                        public bool Contains(T value) => inner.Contains(value);

                        public System.Collections.Generic.HashSet<T>.Enumerator GetEnumerator() => inner.GetEnumerator();
                    }

                    public sealed class Dictionary<TKey, TValue> where TKey : notnull
                    {
                        private readonly System.Collections.Generic.Dictionary<TKey, TValue> inner = new System.Collections.Generic.Dictionary<TKey, TValue>();

                        public int Count => inner.Count;

                        public TValue this[TKey key] => inner[key];

                        public void Add(TKey key, TValue value)
                        {
                            inner.Add(key, value);
                        }

                        public System.Collections.Generic.Dictionary<TKey, TValue>.Enumerator GetEnumerator() => inner.GetEnumerator();
                    }
                }
            }

            namespace Il2CppInterop.Runtime.InteropTypes
            {
                public class Il2CppObjectBase
                {
                    private readonly object? target;

                    public Il2CppObjectBase(object? target)
                    {
                        this.target = target;
                    }

                    public T? TryCast<T>() where T : class
                    {
                        return target as T;
                    }
                }

                namespace Arrays
                {
                    public sealed class Il2CppStructArray<T> where T : struct
                    {
                        private readonly T[] inner;

                        public Il2CppStructArray(int length)
                        {
                            inner = new T[length];
                        }

                        public int Length => inner.Length;

                        public T this[int index]
                        {
                            get => inner[index];
                            set => inner[index] = value;
                        }
                    }

                    public sealed class Il2CppReferenceArray<T> where T : class
                    {
                        private readonly T?[] inner;

                        public Il2CppReferenceArray(int length)
                        {
                            inner = new T?[length];
                        }

                        public int Length => inner.Length;

                        public T? this[int index]
                        {
                            get => inner[index];
                            set => inner[index] = value;
                        }
                    }
                }
            }

            namespace Il2CppInterop.Runtime
            {
                public static class DelegateSupport
                {
                    public static bool WasCalled;

                    public static T? ConvertDelegate<T>(System.Delegate listener) where T : class
                    {
                        WasCalled = true;
                        return listener as T;
                    }
                }
            }
            """;

        System.Reflection.Assembly assembly = CompileAndLoadS1InteropGeneratedAssembly(source);
        Type runtimeType = assembly.GetType("S1Interop.Generated.S1InteropRuntime", throwOnError: true)!;
        Type typeRegistryType = assembly.GetType("S1Interop.Generated.S1InteropTypeRegistry", throwOnError: true)!;
        Type memberRegistryType = assembly.GetType("S1Interop.Generated.S1InteropMemberRegistry", throwOnError: true)!;
        Type objectCastType = assembly.GetType("S1Interop.Generated.S1InteropObjectCast", throwOnError: true)!;
        Type delegateBridgeType = assembly.GetType("S1Interop.Generated.S1InteropDelegateBridge", throwOnError: true)!;
        Type memberKindType = assembly.GetTypes().Single(type => type.Name == "S1InteropMemberKind");

        object? backend = runtimeType.GetProperty("Backend")?.GetValue(null);
        object? isIl2Cpp = runtimeType.GetProperty("IsIl2Cpp")?.GetValue(null);
        object? hudName = typeRegistryType.GetProperty("HudName")?.GetValue(null);
        object? hudType = typeRegistryType.GetProperty("Hud")?.GetValue(null);
        MethodInfo? getHudInstance = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetHudInstance" && !method.IsGenericMethod && method.GetParameters().Length == 0);
        object? hudInstance = getHudInstance?.Invoke(null, null);

        Assert(string.Equals(backend?.ToString(), "Il2Cpp", StringComparison.Ordinal), $"Backend-neutral runtime detection should select Il2Cpp when only Il2Cpp types are loadable. Backend={backend}");
        Assert(isIl2Cpp is true, "Backend-neutral runtime IsIl2Cpp should be true for the fake Il2Cpp assembly.");
        Assert(string.Equals(hudName as string, "Il2CppScheduleOne.UI.HUD", StringComparison.Ordinal), $"Backend-neutral HudName should resolve to Il2Cpp type name. HudName={hudName}");
        Assert(hudType is Type resolvedHudType && resolvedHudType.FullName == "Il2CppScheduleOne.UI.HUD", "Backend-neutral type registry should resolve the fake Il2Cpp HUD type.");
        Assert(hudInstance is not null && hudInstance.GetType().FullName == "Il2CppScheduleOne.UI.HUD", "Generated static member helper should return the fake Il2Cpp HUD instance.");
        object hud = hudInstance!;

        MethodInfo? isHud = typeRegistryType.GetMethod("IsHud", [typeof(object)]);
        MethodInfo? getHud = typeRegistryType.GetMethod("GetHud", [typeof(object), typeof(string)]);
        MethodInfo? trySetHud = typeRegistryType.GetMethod("TrySetHud", [typeof(object), typeof(string), typeof(object)]);
        MethodInfo? invokeHud = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object), typeof(string), typeof(object[]) }));
        MethodInfo? invokeHudOverload = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object), typeof(string), typeof(string[]), typeof(object[]) }));
        Assert(isHud is not null, "Generated type registry should expose an alias-level IsHud helper.");
        Assert(getHud is not null, "Generated type registry should expose an alias-level GetHud helper.");
        Assert(trySetHud is not null, "Generated type registry should expose an alias-level TrySetHud helper.");
        Assert(invokeHud is not null, "Generated type registry should expose an alias-level InvokeHud helper.");
        Assert(invokeHudOverload is not null, "Generated type registry should expose an overload-specific alias-level InvokeHud helper.");
        Assert(isHud!.Invoke(null, [hud]) is true, "Generated alias-level type checker should recognize the fake Il2Cpp HUD instance.");
        Assert(getHud!.Invoke(null, [hud, "Scale"]) is 0, "Generated alias-level instance getter should route through the member registry.");
        Assert(trySetHud!.Invoke(null, [hud, "Scale", "18"]) is true, "Generated alias-level instance setter should convert and write values.");
        Assert(getHud.Invoke(null, [hud, "Scale"]) is 18, "Generated alias-level instance getter should read values written through the alias setter.");
        object?[] facadeArgs = ["21", "facade"];
        Assert(string.Equals(invokeHud!.Invoke(null, [hud, "SetLevel", facadeArgs]) as string, "done", StringComparison.Ordinal), "Generated alias-level method invoker should route through the member registry.");
        Assert(facadeArgs[1] is "il2cpp:facade", "Generated alias-level method invoker should preserve by-ref copy-back behavior.");
        object?[] facadeOverloadArgs = ["22", "facade-overload"];
        Assert(string.Equals(invokeHudOverload!.Invoke(null, [hud, "SetLevel", new[] { "int", "string&" }, facadeOverloadArgs]) as string, "done", StringComparison.Ordinal), "Generated alias-level overload invoker should route through cached parameter-specific member lookup.");
        Assert(facadeOverloadArgs[1] is "il2cpp:facade-overload", "Generated alias-level overload invoker should preserve by-ref copy-back behavior.");

        MethodInfo? trySetScale = memberRegistryType.GetMethod("TrySetHudScale", [typeof(object), typeof(object)]);
        Assert(trySetScale is not null, "Generated member registry should expose TrySetHudScale.");
        object? setResult = trySetScale!.Invoke(null, [hud, "42"]);
        Assert(setResult is true, "Generated field setter should convert string values to the reflected integer field type.");

        object? scale = hud.GetType().GetField("Scale")?.GetValue(hud);
        Assert(scale is 42, $"Generated field setter should update the fake Il2Cpp field. Scale={scale}");

        object fieldKind = Enum.Parse(memberKindType, "Field");
        MethodInfo? getInstanceValue = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetInstanceValue" && method.GetParameters().Length == 3);
        MethodInfo? trySetInstanceValue = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "TrySetInstanceValue" && method.GetParameters().Length == 4);
        Assert(getInstanceValue is not null, "Generated member registry should expose typed GetInstanceValue.");
        Assert(trySetInstanceValue is not null, "Generated member registry should expose typed TrySetInstanceValue.");
        object? dynamicScale = getInstanceValue!.Invoke(null, [hud, "Scale", fieldKind]);
        Assert(dynamicScale is 42, $"Generated dynamic instance getter should read from the fake Il2Cpp object. Scale={dynamicScale}");
        object? dynamicSetResult = trySetInstanceValue!.Invoke(null, [hud, "Scale", "99", fieldKind]);
        Assert(dynamicSetResult is true, "Generated dynamic instance setter should convert values and write through cached instance member lookup.");
        Assert(hud.GetType().GetField("Scale")?.GetValue(hud) is 99, "Generated dynamic instance setter should update the fake Il2Cpp field.");

        MethodInfo? invokeHudGeneric = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 3);
        Assert(invokeHudGeneric is not null, "Generated alias-level invoker should expose a typed generic overload.");
        object? typedFacadeResult = invokeHudGeneric!.MakeGenericMethod(typeof(int)).Invoke(null, [hud, "GetScaleText", Array.Empty<object?>()]);
        Assert(typedFacadeResult is 99, $"Generated alias-level typed invoker should convert simple reflected return values. Result={typedFacadeResult}");

        MethodInfo? invokeGetScaleTextGeneric = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHudGetScaleText" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1);
        Assert(invokeGetScaleTextGeneric is not null, "Generated member invoker should expose a typed generic overload.");
        object? typedMemberResult = invokeGetScaleTextGeneric!.MakeGenericMethod(typeof(int)).Invoke(null, [hud, Array.Empty<object?>()]);
        Assert(typedMemberResult is 99, $"Generated member typed invoker should convert simple reflected return values. Result={typedMemberResult}");

        MethodInfo? invokeSetLevel = GetNonGenericMethod(memberRegistryType, "InvokeHudSetLevel", typeof(object), typeof(object[]));
        Assert(invokeSetLevel is not null, "Generated member registry should expose InvokeHudSetLevel.");
        object?[] args = ["7", "fps"];
        object? invokeResult = invokeSetLevel!.Invoke(null, [hud, args]);

        Assert(string.Equals(invokeResult as string, "done", StringComparison.Ordinal), $"Generated method invoker should return reflected method result. Result={invokeResult}");
        Assert(hud.GetType().GetField("Scale")?.GetValue(hud) is 7, "Generated method invoker should convert string numeric arguments before invocation.");
        Assert(string.Equals(args[1] as string, "il2cpp:fps", StringComparison.Ordinal), $"Generated method invoker should copy by-ref argument values back to caller args. Arg={args[1]}");

        MethodInfo? invokeInstance = memberRegistryType.GetMethod("InvokeInstance", [typeof(object), typeof(string), typeof(string[]), typeof(object[])]);
        Assert(invokeInstance is not null, "Generated member registry should expose overload-specific InvokeInstance.");
        object?[] dynamicArgs = ["11", "hud"];
        object? dynamicInvokeResult = invokeInstance!.Invoke(null, [hud, "SetLevel", new[] { "int", "string&" }, dynamicArgs]);
        Assert(string.Equals(dynamicInvokeResult as string, "done", StringComparison.Ordinal), $"Generated dynamic instance invoker should return reflected method result. Result={dynamicInvokeResult}");
        Assert(hud.GetType().GetField("Scale")?.GetValue(hud) is 11, "Generated dynamic instance invoker should convert arguments before invocation.");
        Assert(string.Equals(dynamicArgs[1] as string, "il2cpp:hud", StringComparison.Ordinal), $"Generated dynamic instance invoker should copy by-ref argument values back to caller args. Arg={dynamicArgs[1]}");

        MethodInfo? invokeRewriteGuid = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteGuid", typeof(object), typeof(object[]));
        Assert(invokeRewriteGuid is not null, "Generated member registry should expose InvokeHudRewriteGuid.");
        object?[] guidArgs = [Guid.Parse("11111111-2222-3333-4444-555555555555")];
        object? rewriteGuidResult = invokeRewriteGuid!.Invoke(null, [hud, guidArgs]);
        Assert(string.Equals(rewriteGuidResult as string, "22222222-3333-4444-5555-666666666666", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref Guid result. Result={rewriteGuidResult}");
        Assert(guidArgs[0] is Guid copiedGuid && copiedGuid == Guid.Parse("22222222-3333-4444-5555-666666666666"), $"Generated method invoker should copy IL2CPP Guid ref values back as System.Guid. Arg={guidArgs[0]}");

        MethodInfo? invokeRewriteNames = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteNames", typeof(object), typeof(object[]));
        Assert(invokeRewriteNames is not null, "Generated member registry should expose InvokeHudRewriteNames.");
        object?[] nameRefArgs = [new List<string> { "alpha", "beta" }];
        object? rewriteNamesResult = invokeRewriteNames!.Invoke(null, [hud, nameRefArgs]);
        Assert(string.Equals(rewriteNamesResult as string, "2:delta:echo", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref list result. Result={rewriteNamesResult}");
        Assert(nameRefArgs[0] is List<string> copiedNames && copiedNames.SequenceEqual(new[] { "delta", "echo" }), $"Generated method invoker should copy IL2CPP list ref values back as managed lists. Arg={nameRefArgs[0]}");

        MethodInfo? invokeRewriteScores = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteScores", typeof(object), typeof(object[]));
        Assert(invokeRewriteScores is not null, "Generated member registry should expose InvokeHudRewriteScores.");
        object?[] scoreRefArgs = [new Dictionary<string, int> { ["north"] = 4 }];
        object? rewriteScoresResult = invokeRewriteScores!.Invoke(null, [hud, scoreRefArgs]);
        Assert(string.Equals(rewriteScoresResult as string, "2:12:15", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref dictionary result. Result={rewriteScoresResult}");
        Assert(scoreRefArgs[0] is Dictionary<string, int> copiedScores && copiedScores.Count == 2 && copiedScores["east"] == 12 && copiedScores["west"] == 15, $"Generated method invoker should copy IL2CPP dictionary ref values back as managed dictionaries. Arg={scoreRefArgs[0]}");

        MethodInfo? invokeRewriteTags = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteTags", typeof(object), typeof(object[]));
        Assert(invokeRewriteTags is not null, "Generated member registry should expose InvokeHudRewriteTags.");
        object?[] tagRefArgs = [new HashSet<string> { "north" }];
        object? rewriteTagsResult = invokeRewriteTags!.Invoke(null, [hud, tagRefArgs]);
        Assert(string.Equals(rewriteTagsResult as string, "2:True:True", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref hash set result. Result={rewriteTagsResult}");
        Assert(tagRefArgs[0] is HashSet<string> copiedTags && copiedTags.SetEquals(new[] { "east", "west" }), $"Generated method invoker should copy IL2CPP hash set ref values back as managed hash sets. Arg={tagRefArgs[0]}");

        MethodInfo? invokeRewriteBytes = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteBytes", typeof(object), typeof(object[]));
        Assert(invokeRewriteBytes is not null, "Generated member registry should expose InvokeHudRewriteBytes.");
        object?[] byteRefArgs = [new byte[] { 1, 2 }];
        object? rewriteBytesResult = invokeRewriteBytes!.Invoke(null, [hud, byteRefArgs]);
        Assert(string.Equals(rewriteBytesResult as string, "3:4:5:6", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref byte-array result. Result={rewriteBytesResult}");
        Assert(byteRefArgs[0] is byte[] copiedBytes && copiedBytes.SequenceEqual(new byte[] { 4, 5, 6 }), $"Generated method invoker should copy IL2CPP byte-array ref values back as managed byte arrays. Arg={byteRefArgs[0]}");

        MethodInfo? invokeSetData = GetNonGenericMethod(memberRegistryType, "InvokeHudSetData", typeof(object), typeof(object[]));
        Assert(invokeSetData is not null, "Generated member registry should expose InvokeHudSetData.");
        Guid guid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        object? setDataResult = invokeSetData!.Invoke(null, [hud, new object?[] { guid, new[] { "alpha", "beta" } }]);
        Assert(
            string.Equals(setDataResult as string, "11111111-2222-3333-4444-555555555555:2:alpha", StringComparison.Ordinal),
            $"Generated method invoker should convert System.Guid and managed arrays to fake IL2CPP Guid/List parameter types. Result={setDataResult}");

        MethodInfo? invokeSetBytes = GetNonGenericMethod(memberRegistryType, "InvokeHudSetBytes", typeof(object), typeof(object[]));
        Assert(invokeSetBytes is not null, "Generated member registry should expose InvokeHudSetBytes.");
        object? setBytesResult = invokeSetBytes!.Invoke(null, [hud, new object?[] { new byte[] { 7, 9 } }]);
        Assert(
            string.Equals(setBytesResult as string, "2:7:9", StringComparison.Ordinal),
            $"Generated method invoker should convert managed byte arrays to fake IL2CPP struct arrays. Result={setBytesResult}");

        MethodInfo? invokeSetLabels = GetNonGenericMethod(memberRegistryType, "InvokeHudSetLabels", typeof(object), typeof(object[]));
        Assert(invokeSetLabels is not null, "Generated member registry should expose InvokeHudSetLabels.");
        object? setLabelsResult = invokeSetLabels!.Invoke(null, [hud, new object?[] { new[] { "north", "south" } }]);
        Assert(
            string.Equals(setLabelsResult as string, "2:north:south", StringComparison.Ordinal),
            $"Generated method invoker should convert managed string arrays to fake IL2CPP reference arrays. Result={setLabelsResult}");

        MethodInfo? invokeSetScores = GetNonGenericMethod(memberRegistryType, "InvokeHudSetScores", typeof(object), typeof(object[]));
        Assert(invokeSetScores is not null, "Generated member registry should expose InvokeHudSetScores.");
        var scores = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>
        {
            ["north"] = 4,
            ["south"] = 8
        });
        object? setScoresResult = invokeSetScores!.Invoke(null, [hud, new object?[] { scores }]);
        Assert(
            string.Equals(setScoresResult as string, "2:4:8", StringComparison.Ordinal),
            $"Generated method invoker should convert managed read-only dictionaries to fake IL2CPP dictionaries. Result={setScoresResult}");

        MethodInfo? invokeSetTags = GetNonGenericMethod(memberRegistryType, "InvokeHudSetTags", typeof(object), typeof(object[]));
        Assert(invokeSetTags is not null, "Generated member registry should expose InvokeHudSetTags.");
        var tags = new HashSet<string> { "north", "south", "north" };
        object? setTagsResult = invokeSetTags!.Invoke(null, [hud, new object?[] { tags }]);
        Assert(
            string.Equals(setTagsResult as string, "2:True:True", StringComparison.Ordinal),
            $"Generated method invoker should convert managed hash sets to fake IL2CPP hash sets. Result={setTagsResult}");

        Type objectBaseType = assembly.GetType("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", throwOnError: true)!;
        object proxy = Activator.CreateInstance(objectBaseType, [hud])!;
        MethodInfo? objectCastAs = objectCastType.GetMethod("As")?.MakeGenericMethod(hud.GetType());
        Assert(objectCastAs is not null, "Generated object cast helper should expose As<T>.");
        object? castResult = objectCastAs!.Invoke(null, [proxy]);
        Assert(ReferenceEquals(castResult, hud), "Generated object cast helper should unwrap fake IL2CPP proxies through reflected TryCast<T>.");

        Type delegateSupportType = assembly.GetType("Il2CppInterop.Runtime.DelegateSupport", throwOnError: true)!;
        MethodInfo? convertDelegate = delegateBridgeType.GetMethod("Convert")?.MakeGenericMethod(typeof(Action));
        Assert(convertDelegate is not null, "Generated delegate bridge should expose Convert<TDelegate>.");
        Action action = static () => { };
        object? convertedDelegate = convertDelegate!.Invoke(null, [action]);
        object? delegateSupportCalled = delegateSupportType.GetField("WasCalled")?.GetValue(null);
        Assert(ReferenceEquals(convertedDelegate, action), "Generated delegate bridge should return the converted delegate instance.");
        Assert(delegateSupportCalled is true, "Generated delegate bridge should route IL2CPP delegate conversion through reflected DelegateSupport.ConvertDelegate<T>.");
    }

    private void S1InteropGeneratorProducesCompileTimeEventBridges()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropGenerateUnityEventBridge]
            [assembly: S1Interop.S1InteropGenerateDelegateEventBridge]

            namespace UnityEngine.Events
            {
                public delegate void UnityAction();
                public delegate void UnityAction<T0>(T0 value);

                public sealed class UnityEvent
                {
                    public void AddListener(UnityAction listener) { }
                    public void AddListener(System.Action listener) { }
                    public void RemoveListener(UnityAction listener) { }
                    public void RemoveListener(System.Action listener) { }
                }

                public sealed class UnityEvent<T0>
                {
                    public void AddListener(UnityAction<T0> listener) { }
                    public void AddListener(System.Action<T0> listener) { }
                    public void RemoveListener(UnityAction<T0> listener) { }
                    public void RemoveListener(System.Action<T0> listener) { }
                }
            }

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        IReadOnlyDictionary<string, string> monoGenerated = RunS1InteropGenerator(source, "MONO");
        IReadOnlyDictionary<string, string> il2CppGenerated = RunS1InteropGenerator(source, "IL2CPP");

        bool hasMonoUnityBridge = monoGenerated.TryGetValue("S1Interop.UnityEventBridge.g.cs", out string? monoUnityBridge);
        bool hasMonoDelegateBridge = monoGenerated.TryGetValue("S1Interop.DelegateEventBridge.g.cs", out string? monoDelegateBridge);
        bool hasIl2CppUnityBridge = il2CppGenerated.TryGetValue("S1Interop.UnityEventBridge.g.cs", out string? il2CppUnityBridge);
        bool hasIl2CppDelegateBridge = il2CppGenerated.TryGetValue("S1Interop.DelegateEventBridge.g.cs", out string? il2CppDelegateBridge);

        Assert(
            hasMonoUnityBridge && hasMonoDelegateBridge,
            "Generator should emit requested event bridges for Mono builds.");
        Assert(
            hasIl2CppUnityBridge && hasIl2CppDelegateBridge,
            "Generator should emit requested event bridges for IL2CPP builds.");

        monoUnityBridge ??= string.Empty;
        monoDelegateBridge ??= string.Empty;
        il2CppUnityBridge ??= string.Empty;
        il2CppDelegateBridge ??= string.Empty;

        Assert(
            monoUnityBridge.Contains("S1InteropUnityEventBridge", StringComparison.Ordinal) &&
            monoUnityBridge.Contains("UnityEngine.Events.UnityAction wrapped = new UnityEngine.Events.UnityAction(listener);", StringComparison.Ordinal),
            "Compile-time UnityEvent bridge should include Mono UnityAction wrapping.");
        Assert(
            il2CppUnityBridge.Contains("#if IL2CPP", StringComparison.Ordinal) &&
            il2CppUnityBridge.Contains("System.Action wrapped = new System.Action(listener);", StringComparison.Ordinal),
            "Compile-time UnityEvent bridge should include IL2CPP System.Action wrapping.");
        Assert(
            monoDelegateBridge.Contains("S1InteropDelegateEventBridge", StringComparison.Ordinal) &&
            il2CppDelegateBridge.Contains("System.Delegate.Combine", StringComparison.Ordinal) &&
            il2CppDelegateBridge.Contains("System.Delegate.Remove", StringComparison.Ordinal),
            "Compile-time delegate bridge should include Combine and Remove helpers.");
    }

    private void SdkFacadeAliasesFullyQualifiedScheduleOneTypes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "AliasFacadeMod.csproj");
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
                using GameHud = Il2CppScheduleOne.UI.HUD;
                using GameWeed = ScheduleOne.Product.WeedDefinition;

                namespace AliasFacadeMod
                {
                    public static class Core
                    {
                        private static GameHud? hud;
                        private static GameWeed? aliasedWeed;
                        private static ScheduleOne.Product.WeedDefinition? weed;
                        private static ScheduleOne.Persistence.Datas.QuestEntryData? questEntry;
                        private static ScheduleOne.Other.QuestEntryData? collidingQuestEntry;
                    }
                }
                """);

            ProjectAnalysis project = analyzer.Analyze(tempProject).Projects.Single();
            var generator = new SdkFacadeGenerator();
            SdkFacadePlan plan = generator.Plan(project);
            string facadeSource = generator.GenerateSource(plan);

            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "WeedDefinition" &&
                    alias.MonoType == "ScheduleOne.Product.WeedDefinition" &&
                    alias.Il2CppType == "Il2CppScheduleOne.Product.WeedDefinition" &&
                    alias.GenerateGlobalUsing),
                "Facade plan should alias unique fully-qualified ScheduleOne type references.");
            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "GameHud" &&
                    alias.MonoType == "ScheduleOne.UI.HUD" &&
                    alias.Il2CppType == "Il2CppScheduleOne.UI.HUD" &&
                    !alias.GenerateGlobalUsing),
                "Facade plan should normalize explicit Il2Cpp ScheduleOne alias directives into backend-neutral registry aliases without duplicating local aliases.");
            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "GameWeed" &&
                    alias.MonoType == "ScheduleOne.Product.WeedDefinition" &&
                    alias.Il2CppType == "Il2CppScheduleOne.Product.WeedDefinition" &&
                    !alias.GenerateGlobalUsing),
                "Facade plan should preserve explicit Mono ScheduleOne alias directives as backend-neutral registry aliases without duplicating local aliases.");
            Assert(
                plan.TypeAliases.All(alias => alias.Alias != "QuestEntryData"),
                "Facade plan should skip aliases when multiple fully-qualified types share the same simple name.");
            Assert(
                facadeSource.Contains("global using WeedDefinition = ScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using WeedDefinition = Il2CppScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal),
                "Generated facade should include conditional aliases for unique fully-qualified type references.");
            Assert(
                !facadeSource.Contains("global using GameHud =", StringComparison.Ordinal) &&
                !facadeSource.Contains("global using GameWeed =", StringComparison.Ordinal),
                "Generated facade should not duplicate aliases already declared by source files.");
            Assert(
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.UI.HUD\", Alias = \"GameHud\", Il2CppTypeName = \"Il2CppScheduleOne.UI.HUD\")]", StringComparison.Ordinal) &&
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Product.WeedDefinition\", Alias = \"GameWeed\", Il2CppTypeName = \"Il2CppScheduleOne.Product.WeedDefinition\")]", StringComparison.Ordinal),
                "Generated facade should emit registry attributes so explicit aliases can feed backend-neutral Roslyn caches.");
            Assert(
                !facadeSource.Contains("S1InteropRuntime", StringComparison.Ordinal),
                "File-based SDK facade should not emit runtime helpers; the Roslyn generator owns backend-neutral runtime detection and caches.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackRewritesFullyQualifiedScheduleOneTypes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "AliasRewriteMod.csproj");
            string tempSource = Path.Combine(tempRoot, "Core.cs");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");
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

                namespace AliasRewriteMod
                {
                    public static class Core
                    {
                        public const string TypeName = "ScheduleOne.Product.WeedDefinition";
                        public const string VerbatimTypeName = @"ScheduleOne.Product.WeedDefinition";
                        // ScheduleOne.Product.WeedDefinition should remain readable in comments.
                        public static Type WeedType => typeof(ScheduleOne.Product.WeedDefinition);
                        public static ScheduleOne.Product.WeedDefinition? Find() => null;
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_sdk_facade"),
                "Fully-qualified ScheduleOne types should trigger SDK facade generation.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Generated SDK facade registry attributes should install the S1Interop Roslyn generator package.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_fully_qualified_scheduleone_types"),
                "Fully-qualified ScheduleOne types should plan a source rewrite when the alias is unique.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_fully_qualified_scheduleone_types"),
                "Migration apply should rewrite fully-qualified ScheduleOne type references.");
            Assert(File.Exists(generatedFacade), "Migration apply should generate the SDK facade for type aliases.");

            string migratedSource = File.ReadAllText(tempSource);
            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                migratedSource.Contains("typeof(WeedDefinition)", StringComparison.Ordinal) &&
                migratedSource.Contains("WeedDefinition? Find()", StringComparison.Ordinal),
                "Migration should replace fully-qualified ScheduleOne type tokens with the generated alias.");
            Assert(
                !migratedSource.Contains("typeof(ScheduleOne.Product.WeedDefinition)", StringComparison.Ordinal) &&
                !migratedSource.Contains("ScheduleOne.Product.WeedDefinition? Find()", StringComparison.Ordinal),
                "Migration should remove fully-qualified ScheduleOne type tokens from code when the alias is unique.");
            Assert(
                migratedSource.Contains("public const string TypeName = \"ScheduleOne.Product.WeedDefinition\";", StringComparison.Ordinal) &&
                migratedSource.Contains("public const string VerbatimTypeName = @\"ScheduleOne.Product.WeedDefinition\";", StringComparison.Ordinal) &&
                migratedSource.Contains("// ScheduleOne.Product.WeedDefinition should remain readable in comments.", StringComparison.Ordinal),
                "Migration should not rewrite fully-qualified ScheduleOne type names inside strings or comments.");
            Assert(
                facadeSource.Contains("global using WeedDefinition = ScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using WeedDefinition = Il2CppScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal),
                "Generated facade should provide Mono and IL2CPP aliases for the rewritten type.");
            Assert(
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Product.WeedDefinition\", Alias = \"WeedDefinition\", Il2CppTypeName = \"Il2CppScheduleOne.Product.WeedDefinition\")]", StringComparison.Ordinal),
                "Generated facade should register rewritten aliases for backend-neutral reflection cache generation.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback should restore the rewritten source file.");
            Assert(!File.Exists(generatedFacade), "Rollback should remove the generated alias facade.");
            Assert(
                File.ReadAllText(tempSource).Contains("ScheduleOne.Product.WeedDefinition", StringComparison.Ordinal),
                "Rollback should restore the original fully-qualified ScheduleOne type references.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void HideFromIl2CppMigrationHandlesMultipleTargetsAndOverloads()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "SyntheticMod.csproj");
            string tempSource = Path.Combine(tempRoot, "InjectedComponent.cs");
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

                public class HelperA
                {
                }

                public class HelperB
                {
                }

                #if !MONO
                [MelonLoader.RegisterTypeInIl2Cpp]
                #endif
                public class InjectedComponent
                {
                #if !MONO
                    public InjectedComponent(IntPtr ptr) : base(ptr) { }
                #endif

                    public void Convert(int value)
                    {
                    }

                    public HelperA Convert(HelperA value)
                    {
                        return value;
                    }

                    public HelperB ConvertB(HelperB value)
                    {
                        return value;
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            MigrationOperation[] hideOperations = projectPlan.Operations
                .Where(operation => operation.RuleId == "injected_member_requires_hidefromil2cpp")
                .ToArray();
            Assert(hideOperations.Length == 2, "Synthetic fixture should report two managed-surface migration operations.");
            Assert(
                hideOperations.Any(operation => operation.Evidence?.Contains("Convert(HelperA value)", StringComparison.Ordinal) == true),
                "HideFromIl2Cpp migration evidence should preserve overload parameter text.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlan(tempProject, [projectPlan]));
            Assert(
                applyResult.Operations.Count(operation => operation.RuleId == "injected_member_requires_hidefromil2cpp") == 2,
                "Migration apply should apply both same-file HideFromIl2Cpp operations.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                CountOccurrences(migratedSource, "Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp") == 2,
                "Migration should add exactly two HideFromIl2Cpp attributes.");

            int primitiveOverloadIndex = migratedSource.IndexOf("public void Convert(int value)", StringComparison.Ordinal);
            int helperOverloadIndex = migratedSource.IndexOf("public HelperA Convert(HelperA value)", StringComparison.Ordinal);
            int firstAttributeIndex = migratedSource.IndexOf("[Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]", StringComparison.Ordinal);
            Assert(
                primitiveOverloadIndex >= 0 &&
                helperOverloadIndex > primitiveOverloadIndex &&
                firstAttributeIndex > primitiveOverloadIndex &&
                firstAttributeIndex < helperOverloadIndex,
                "Migration should hide the HelperA overload without hiding the primitive overload.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            ProjectMigrationPlan idempotentPlan = new MigrationPlanner().Plan(after).Projects.Single();
            Assert(
                idempotentPlan.Operations.All(operation => operation.RuleId != "injected_member_requires_hidefromil2cpp"),
                "A second migration plan should not duplicate HideFromIl2Cpp operations after apply.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationPlannerCreatesOperationsForBrokenFixture()
    {
        string path = Path.Combine(WorkspaceRoot, @"JackpotEveryTime\JackpotEveryTime.csproj");
        WorkspaceAnalysis analysis = analyzer.Analyze(path);
        MigrationPlan plan = new MigrationPlanner().Plan(analysis);
        ProjectMigrationPlan projectPlan = plan.Projects.Single();

        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "wrong_target_framework" && operation.Automatic),
            "Expected automatic TargetFramework migration operation for JackpotEveryTime.");
        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "wrong_il2cpp_reference_surface" && operation.Risk == "medium"),
            "Expected IL2CPP reference-surface migration operation for JackpotEveryTime.");
        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "generate_sdk_facade" && operation.Automatic),
            "Expected SDK facade generation operation for JackpotEveryTime.");
    }

    private void SdkFacadeGeneratorDetectsGunsAlwaysAccurateNamespaces()
    {
        ProjectAnalysis project = AnalyzeProject(@"GunsAlwaysAccurate\GunsAlwaysAccurate.csproj");
        var generator = new SdkFacadeGenerator();
        SdkFacadePlan facadePlan = generator.Plan(project);
        string source = generator.GenerateSource(facadePlan);

        Assert(facadePlan.HasContent, "GunsAlwaysAccurate should produce a facade generation plan.");
        Assert(
            facadePlan.ScheduleOneNamespaces.Contains("ScheduleOne.DevUtilities"),
            "Expected facade plan to include ScheduleOne.DevUtilities.");
        Assert(
            facadePlan.ScheduleOneNamespaces.Contains("ScheduleOne.UI"),
            "Expected facade plan to include ScheduleOne.UI.");
        Assert(
            source.Contains("global using ScheduleOne.UI;", StringComparison.Ordinal),
            "Expected generated source to include Mono ScheduleOne.UI global using.");
        Assert(
            source.Contains("global using Il2CppScheduleOne.UI;", StringComparison.Ordinal),
            "Expected generated source to include IL2CPP ScheduleOne.UI global using.");
        Assert(
            !source.Contains("Il2CppIl2Cpp", StringComparison.Ordinal),
            "Generated source should not double-prefix Il2Cpp namespaces.");
    }

    private void SdkFacadeMigrationRequiresCSharp10ForDefaultLangVersionProjects()
    {
        ProjectAnalysis project = AnalyzeProject(@"DedicatedServerAddons\S1DS-PlayerList\S1DS-PlayerList.csproj");

        AssertHasDiagnostic(project, "global_usings_require_langversion", null);

        MigrationPlan plan = new MigrationPlanner().Plan(
            new WorkspaceAnalysis(project.ProjectPath, [project]),
            new MigrationPlannerOptions(DualRuntime: true));
        Assert(
            plan.Projects.Single().Operations.Any(operation =>
                operation.RuleId == "global_usings_require_langversion" &&
                operation.Automatic),
            "Default LangVersion projects with generated S1Interop facades should plan a C# 10 migration.");
    }

    private void DuplicateLangVersionUsesEffectiveLastValueForSdkFacadeSupport()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "DuplicateLangVersionMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <LangVersion>default</LangVersion>
                    <LangVersion>latest</LangVersion>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                using ScheduleOne.DevUtilities;

                namespace DuplicateLangVersionMod;

                public static class Core
                {
                    public static void Touch() => PlayerSingleton<PlayerCamera>.InstanceExists.ToString();
                }
                """);

            ProjectAnalysis project = analyzer.Analyze(tempProject).Projects.Single();

            Assert(
                new SdkFacadeGenerator().Plan(project).HasContent,
                "Fixture should require an SDK facade so LangVersion support is evaluated.");
            Assert(
                project.Diagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
                "Duplicate LangVersion properties should use the effective last value.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DuplicateLangVersionRealModsDoNotRequireCSharp10Migration()
    {
        ProjectAnalysis hoverboard = AnalyzeProject(@"Hoverboard\Hoverboard.csproj");
        Assert(
            hoverboard.Diagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
            "Hoverboard's later LangVersion=latest should satisfy generated facade support.");

        ProjectAnalysis modernCheatMenu = AnalyzeProject(@"Modern-Cheat-Menu\Cheat Menu\Modern Cheat Menu.csproj");
        Assert(
            modernCheatMenu.Diagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
            "Modern-Cheat-Menu's later LangVersion=latest should satisfy generated facade support.");
    }

    private void MigrationApplyAndRollbackWorkOnCopiedFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, "JackpotEveryTime");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "JackpotEveryTime.csproj");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);

            Assert(File.Exists(applyResult.ManifestPath), "Migration manifest was not written.");
            Assert(File.Exists(Path.Combine(tempRoot, "local.build.props")), "local.build.props was not created.");
            Assert(File.Exists(Path.Combine(tempRoot, "local.build.props.example")), "local.build.props.example was not created.");
            Assert(File.Exists(generatedFacade), "SDK facade was not generated.");

            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                facadeSource.Contains("global using ScheduleOne.Casino;", StringComparison.Ordinal),
                "Generated facade should include Mono casino namespace.");
            Assert(
                facadeSource.Contains("global using Il2CppScheduleOne.Casino;", StringComparison.Ordinal),
                "Generated facade should include IL2CPP casino namespace.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                !after.Diagnostics.Any(diagnostic => diagnostic.Severity == CoreDiagnosticSeverity.Error),
                "Copied JackpotEveryTime fixture should have no error diagnostics after migration apply.");
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "local_path_in_project"),
                "Copied JackpotEveryTime fixture should not retain committed local path diagnostics after migration apply.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the copied project file.");
            Assert(!File.Exists(Path.Combine(tempRoot, "local.build.props")), "Rollback did not remove generated local.build.props.");
            Assert(rollbackResult.RemovedFiles.Contains(generatedFacade), "Rollback did not report removing the generated SDK facade.");
            Assert(!File.Exists(generatedFacade), "Rollback did not remove the generated SDK facade.");

            WorkspaceAnalysis rolledBack = analyzer.Analyze(tempProject);
            Assert(
                rolledBack.Diagnostics.Any(diagnostic => diagnostic.RuleId == "wrong_target_framework"),
                "Rollback should restore the original wrong_target_framework diagnostic.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackFixRuntimeDefinesOnCopiedFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceProject = Path.Combine(WorkspaceRoot, @"BetterJukebox\BetterJukebox.csproj");
            string sourceCore = Path.Combine(WorkspaceRoot, @"BetterJukebox\Core.cs");
            string tempProject = Path.Combine(tempRoot, "BetterJukebox.csproj");
            string tempCore = Path.Combine(tempRoot, "Core.cs");
            File.Copy(sourceProject, tempProject);
            File.Copy(sourceCore, tempCore);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectAnalysis beforeProject = before.Projects.Single();
            AssertHasDiagnostic(beforeProject, "missing_runtime_define", "Mono");
            AssertHasDiagnostic(beforeProject, "missing_runtime_define", "IL2CPP");

            MigrationPlan plan = new MigrationPlanner().Plan(before);
            Assert(
                plan.Projects.Single().Operations.Any(operation => operation.RuleId == "missing_runtime_define"),
                "Expected missing_runtime_define migration operations for BetterJukebox.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "missing_runtime_define"),
                "Migration apply did not apply missing_runtime_define operations.");
            Assert(
                ConfigurationDefines(tempProject, "Mono").Contains("MONO"),
                "Mono configuration should define MONO after migration.");
            Assert(
                ConfigurationDefines(tempProject, "IL2CPP").Contains("IL2CPP"),
                "IL2CPP configuration should define IL2CPP after migration.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Copied BetterJukebox fixture should not retain missing runtime define diagnostics after migration apply.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the BetterJukebox project file.");
            Assert(
                !ConfigurationDefines(tempProject, "Mono").Contains("MONO"),
                "Rollback should remove the migrated MONO define.");
            Assert(
                !ConfigurationDefines(tempProject, "IL2CPP").Contains("IL2CPP"),
                "Rollback should remove the migrated IL2CPP define.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationScaffoldsS1DsPlayerListFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, @"DedicatedServerAddons\S1DS-PlayerList");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "S1DS-PlayerList.csproj");
            string tempClientSource = Path.Combine(tempRoot, "S1DSPlayerListClientMod.cs");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();

            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "add_il2cpp_configuration"),
                "Expected dual-runtime migration to add IL2CPP configurations.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "conditionalize_scheduleone_usings" &&
                    string.Equals(operation.FilePath, tempClientSource, StringComparison.OrdinalIgnoreCase)),
                "Expected dual-runtime migration to conditionalize S1DS client ScheduleOne usings.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "add_il2cpp_configuration"),
                "Dual-runtime apply did not scaffold IL2CPP configurations.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "conditionalize_scheduleone_usings"),
                "Dual-runtime apply did not conditionalize source usings.");

            string projectText = File.ReadAllText(tempProject);
            Assert(projectText.Contains("Il2cpp_Client", StringComparison.Ordinal), "Scaffolded project should include Il2cpp_Client.");
            Assert(projectText.Contains("Il2cpp_Server", StringComparison.Ordinal), "Scaffolded project should include Il2cpp_Server.");
            Assert(projectText.Contains("<TargetFramework>net6.0</TargetFramework>", StringComparison.Ordinal), "IL2CPP configs should target net6.0.");
            Assert(projectText.Contains("<DefineConstants>IL2CPP;CLIENT</DefineConstants>", StringComparison.Ordinal), "IL2CPP client config should define IL2CPP;CLIENT.");
            Assert(projectText.Contains("<DefineConstants>IL2CPP;SERVER</DefineConstants>", StringComparison.Ordinal), "IL2CPP server config should define IL2CPP;SERVER.");
            Assert(projectText.Contains("DedicatedServerMod_Il2cpp_Client", StringComparison.Ordinal), "Client references should target the IL2CPP DedicatedServerMod assembly.");
            Assert(projectText.Contains("Il2CppFishNet.Runtime", StringComparison.Ordinal), "FishNet reference should be rewritten to the IL2CPP wrapper assembly.");
            Assert(projectText.Contains("Il2CppInterop.Runtime", StringComparison.Ordinal), "IL2CPP configs should reference Il2CppInterop.Runtime.");
            Assert(projectText.Contains("S1DSModSearchPath", StringComparison.Ordinal), "IL2CPP configs should retain the S1DS mod search path fallback.");

            string clientSource = File.ReadAllText(tempClientSource);
            Assert(
                !clientSource.Contains("using ScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
                !clientSource.Contains("using Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal),
                "Client source should let the generated facade own normal ScheduleOne namespace imports.");
            Assert(File.Exists(generatedFacade), "Dual-runtime migration should generate the SDK facade.");
            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                facadeSource.Contains("global using ScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal),
                "Generated facade should provide Mono and IL2CPP ScheduleOne.PlayerScripts imports.");
            Assert(
                projectText.Contains(@"<Compile Include=""S1Interop.Generated\S1Interop.GlobalUsings.g.cs""", StringComparison.Ordinal),
                "Projects with EnableDefaultCompileItems=false should explicitly compile the generated facade.");
            AssertHasUnconditionedCompileInclude(tempProject, @"S1Interop.Generated\S1Interop.GlobalUsings.g.cs");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the S1DS project file.");
            Assert(rollbackResult.RestoredFiles.Contains(tempClientSource), "Rollback did not restore the S1DS client source file.");

            string rolledBackProject = File.ReadAllText(tempProject);
            string rolledBackClientSource = File.ReadAllText(tempClientSource);
            Assert(!rolledBackProject.Contains("Il2cpp_Client", StringComparison.Ordinal), "Rollback should remove scaffolded Il2cpp_Client config.");
            Assert(!rolledBackProject.Contains("S1Interop.GlobalUsings.g.cs", StringComparison.Ordinal), "Rollback should remove the generated facade Compile include.");
            Assert(rolledBackClientSource.Contains("using ScheduleOne.PlayerScripts;", StringComparison.Ordinal), "Rollback should restore the unconditional ScheduleOne using.");
            Assert(!rolledBackClientSource.Contains("using Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal), "Rollback should remove the generated IL2CPP using.");
            Assert(!File.Exists(generatedFacade), "Rollback should remove the generated SDK facade.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationAddsGeneratedMonoGuardDefines()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, @"DedicatedServerAddons\SeparateOrganisations.POC");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "SeparateOrganisations.csproj");
            string tempModelSource = Path.Combine(tempRoot, "SeparateOrgs.Models.cs");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();

            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Mono_Client"),
                "Dual-runtime source guard generation should plan MONO for Mono_Client.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Mono_Server"),
                "Dual-runtime source guard generation should plan MONO for Mono_Server.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Count(operation => operation.RuleId == "missing_runtime_define") == 2,
                "Dual-runtime apply should add MONO to both existing Mono configurations.");
            string projectText = File.ReadAllText(tempProject);
            Assert(
                projectText.Contains(@"<Compile Include=""S1Interop.Generated\S1Interop.GlobalUsings.g.cs""", StringComparison.Ordinal),
                "SeparateOrganisations should explicitly compile the generated facade because default compile items are disabled.");
            AssertHasUnconditionedCompileInclude(tempProject, @"S1Interop.Generated\S1Interop.GlobalUsings.g.cs");

            IReadOnlyList<string> clientDefines = ConfigurationDefines(tempProject, "Mono_Client");
            IReadOnlyList<string> serverDefines = ConfigurationDefines(tempProject, "Mono_Server");
            Assert(clientDefines.Contains("CLIENT"), "Mono_Client should retain CLIENT.");
            Assert(clientDefines.Contains("MONO"), "Mono_Client should define MONO after migration.");
            Assert(serverDefines.Contains("SERVER"), "Mono_Server should retain SERVER.");
            Assert(serverDefines.Contains("MONO"), "Mono_Server should define MONO after migration.");

            string modelSource = File.ReadAllText(tempModelSource);
            Assert(
                !modelSource.Contains("using ScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
                !modelSource.Contains("using ScheduleOne.Persistence;", StringComparison.Ordinal),
                "Ordinary ScheduleOne usings should move out of SeparateOrgs.Models.cs when the facade owns them.");
            Assert(File.Exists(generatedFacade), "Dual-runtime migration should generate a facade for SeparateOrganisations.");
            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                facadeSource.Contains("global using ScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using Il2CppScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using ScheduleOne.Persistence;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using Il2CppScheduleOne.Persistence;", StringComparison.Ordinal),
                "Generated facade should contain Mono and IL2CPP imports for SeparateOrganisations ScheduleOne namespaces.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Migrated SeparateOrganisations fixture should not retain missing runtime define diagnostics.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the SeparateOrganisations project file.");
            Assert(rollbackResult.RestoredFiles.Contains(tempModelSource), "Rollback did not restore the SeparateOrganisations model source file.");
            string rolledBackProjectText = File.ReadAllText(tempProject);
            Assert(
                !rolledBackProjectText.Contains("S1Interop.GlobalUsings.g.cs", StringComparison.Ordinal),
                "Rollback should remove the SeparateOrganisations generated facade Compile include.");
            Assert(
                !ConfigurationDefines(tempProject, "Mono_Client").Contains("MONO"),
                "Rollback should remove generated MONO from Mono_Client.");
            Assert(
                !ConfigurationDefines(tempProject, "Mono_Server").Contains("MONO"),
                "Rollback should remove generated MONO from Mono_Server.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private ProjectAnalysis AnalyzeProject(string relativePath)
    {
        string path = Path.Combine(WorkspaceRoot, relativePath);
        WorkspaceAnalysis analysis = analyzer.Analyze(path);
        Assert(analysis.Projects.Count == 1, $"Expected one project for {relativePath}, found {analysis.Projects.Count}.");
        return analysis.Projects[0];
    }

    private static void AssertHasRuntime(ProjectAnalysis project, string configName, RuntimeKind runtime)
    {
        ConfigurationAnalysis resolved = GetConfiguration(project, configName);
        Assert(resolved.Runtime == runtime, $"Expected {configName} to infer {runtime}, got {resolved.Runtime}.");
    }

    private static void AssertHasTargetFramework(ProjectAnalysis project, string configName, string targetFramework)
    {
        ConfigurationAnalysis configuration = GetConfiguration(project, configName);
        Assert(
            string.Equals(configuration.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase),
            $"Expected {configName} to target {targetFramework}, got {configuration.TargetFramework ?? "<missing>"}.");
    }

    private static ConfigurationAnalysis GetConfiguration(ProjectAnalysis project, string configName)
    {
        ConfigurationAnalysis? configuration = project.Configurations.FirstOrDefault(config =>
            string.Equals(config.Name, configName, StringComparison.OrdinalIgnoreCase));
        Assert(configuration is not null, $"Missing configuration {configName} in {project.ProjectPath}.");
        return configuration!;
    }

    private static void AssertHasDiagnostic(ProjectAnalysis project, string ruleId, string? configuration)
    {
        bool found = project.Diagnostics.Any(diagnostic =>
            diagnostic.RuleId == ruleId &&
            string.Equals(diagnostic.Configuration, configuration, StringComparison.OrdinalIgnoreCase));
        Assert(found, $"Expected diagnostic {ruleId} for {configuration} in {project.ProjectPath}.");
    }

    private static void AssertHasUnconditionedCompileInclude(string projectPath, string include)
    {
        XDocument document = XDocument.Load(projectPath);
        bool found = document.Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Any(element =>
                element.Parent?.Attribute("Condition") is null &&
                string.Equals(element.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));
        Assert(found, $"Expected unconditioned Compile Include={include} in {projectPath}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static MethodInfo? GetNonGenericMethod(Type type, string name, params Type[] parameterTypes)
    {
        return type.GetMethods()
            .FirstOrDefault(method =>
                string.Equals(method.Name, name, StringComparison.Ordinal) &&
                !method.IsGenericMethod &&
                method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
    }

    private static string RunTypeRegistryGenerator(string source, params string[] symbols)
    {
        IReadOnlyDictionary<string, string> generatedSources = RunS1InteropGenerator(source, symbols);
        return generatedSources.Single(pair => pair.Key.Contains("S1Interop.TypeRegistry.g.cs", StringComparison.Ordinal)).Value;
    }

    private static IReadOnlyDictionary<string, string> RunS1InteropGenerator(string source, params string[] symbols)
    {
        Compilation outputCompilation = RunS1InteropGeneratorCompilation(source, symbols);
        return outputCompilation.SyntaxTrees
            .Where(tree => (tree.FilePath ?? string.Empty).Contains("S1Interop.Generators", StringComparison.Ordinal))
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString(),
                StringComparer.Ordinal);
    }

    private static System.Reflection.Assembly CompileAndLoadS1InteropGeneratedAssembly(string source, params string[] symbols)
    {
        Compilation outputCompilation = RunS1InteropGeneratorCompilation(source, symbols);
        using var assemblyStream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = outputCompilation.Emit(assemblyStream);
        Assert(
            emitResult.Success,
            $"S1Interop generated compilation emit failed: {string.Join(Environment.NewLine, emitResult.Diagnostics)}");

        assemblyStream.Position = 0;
        return System.Reflection.Assembly.Load(assemblyStream.ToArray());
    }

    private static Compilation RunS1InteropGeneratorCompilation(string source, params string[] symbols)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(symbols);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "SyntheticMod." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new S1InteropTypeRegistryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert(
            generatorDiagnostics.All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error),
            $"S1Interop type registry generator reported errors: {string.Join(Environment.NewLine, generatorDiagnostics)}");
        Assert(
            outputCompilation.GetDiagnostics().All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error),
            $"S1Interop generated compilation reported errors: {string.Join(Environment.NewLine, outputCompilation.GetDiagnostics())}");

        return outputCompilation;
    }

    private static IReadOnlyList<MetadataReference> GetTrustedPlatformReferences()
    {
        string trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string BuildBackendNeutralRegistrySource(IEnumerable<SdkTypeAlias> aliases)
    {
        var builder = new StringBuilder();
        foreach (SdkTypeAlias alias in aliases)
        {
            builder.AppendLine($"[assembly: S1Interop.S1InteropType(\"{EscapeCSharpString(alias.MonoType)}\", Alias = \"{EscapeCSharpString(alias.Alias)}\", Il2CppTypeName = \"{EscapeCSharpString(alias.Il2CppType)}\")]");
        }

        builder.AppendLine();
        builder.AppendLine("namespace SyntheticRealMod");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class BackendNeutralRegistryProbe");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string EscapeCSharpString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static int CountProjectImports(string projectPath, string importPath)
    {
        return XDocument.Load(projectPath).Root!.Elements()
            .Count(element =>
                string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("Project")?.Value, importPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsPath(IEnumerable<string> paths, string expectedPath)
    {
        string fullExpectedPath = Path.GetFullPath(expectedPath);
        return paths.Any(path => string.Equals(Path.GetFullPath(path), fullExpectedPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatDiagnostics(IEnumerable<InteropDiagnostic> diagnostics)
    {
        return string.Join(
            "; ",
            diagnostics.Select(diagnostic =>
                $"{diagnostic.RuleId}/{diagnostic.Configuration ?? "project"}: {diagnostic.Evidence ?? diagnostic.Message}"));
    }

    private static string FormatBuildResults(IEnumerable<MigrationBuildResult>? buildResults)
    {
        if (buildResults is null)
        {
            return "<none>";
        }

        return string.Join(
            "; ",
            buildResults.Select(result =>
                $"{result.Configuration}/{result.Runtime}: exit={result.ExitCode}, success={result.Success}, timedOut={result.TimedOut}, readiness={result.ReadinessStatus}, attribution={result.Attribution}, kind={result.FailureKind}, summary={result.Summary}, issues={string.Join("|", result.Issues.Select(issue => $"{issue.Kind}:{issue.Include}:{issue.Path}"))}, command={result.Command}, output={result.Output}"));
    }

    private static string ExtractManifestPath(string output)
    {
        foreach (string line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Manifest:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        throw new InvalidOperationException($"Could not find manifest path in output: {output}");
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ProcessResult RunDotNet(params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out.");
        }

        return new ProcessResult(process.ExitCode, output + error);
    }

    private static IReadOnlyList<string> ConfigurationDefines(string projectPath, string configuration)
    {
        XElement propertyGroup = GetConfigurationPropertyGroup(projectPath, configuration);
        XElement defineConstants = propertyGroup.Elements()
            .First(element => string.Equals(element.Name.LocalName, "DefineConstants", StringComparison.OrdinalIgnoreCase));
        return defineConstants.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetConditionedDefineConstants(XDocument document, string configurationPlatform)
    {
        XElement propertyGroup = document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            .First(element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains(configurationPlatform, StringComparison.OrdinalIgnoreCase));
        return propertyGroup.Elements()
            .First(element => string.Equals(element.Name.LocalName, "DefineConstants", StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static void ReduceBotanistFixCopyToMonoOnly(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in document.Root!.Elements().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if ((element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) &&
                condition.Contains("Il2cpp", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }
        XElement configurations = document.Descendants()
            .First(element => element.Name.LocalName.Equals("Configurations", StringComparison.OrdinalIgnoreCase));
        configurations.Value = "Mono;MonoRelease";

        const string monoReferenceCondition = "'$(Configuration)'=='Mono' Or '$(Configuration)'=='MonoRelease'";
        foreach (XElement itemGroup in document.Root.Elements()
                     .Where(element =>
                         element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) &&
                         element.Attribute("Condition") is null &&
                         element.Elements().Any(child => child.Name.LocalName.Equals("Reference", StringComparison.OrdinalIgnoreCase))))
        {
            itemGroup.SetAttributeValue("Condition", monoReferenceCondition);
        }

        document.Save(projectPath);
    }

    private static void ReduceS1VoiceChatCopyToMonoOnly(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in document.Root!.Elements().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if ((element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) &&
                (condition.Contains("Il2CppMelon", StringComparison.OrdinalIgnoreCase) ||
                 condition.Contains("IL2CPPMELON", StringComparison.OrdinalIgnoreCase)))
            {
                element.Remove();
            }
        }
        foreach (XElement element in document.Descendants().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if (condition.Contains("Il2CppMelon", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("IL2CPPMELON", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }

        XElement configurations = document.Descendants()
            .First(element => element.Name.LocalName.Equals("Configurations", StringComparison.OrdinalIgnoreCase));
        configurations.Value = "MonoMelon";

        XElement? targetFrameworks = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase));
        if (targetFrameworks is not null)
        {
            targetFrameworks.Value = "netstandard2.1";
        }

        document.Save(projectPath);
    }

    private static void ReduceS1FuelModCopyToMonoOnly(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in document.Root!.Elements().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if ((element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) &&
                condition.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }

        foreach (XElement import in document.Root.Elements()
                     .Where(element =>
                         element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase) &&
                         (element.Attribute("Project")?.Value ?? string.Empty).Contains("MelonIL2CPP.targets", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            import.Remove();
        }

        XElement configurations = document.Descendants()
            .First(element => element.Name.LocalName.Equals("Configurations", StringComparison.OrdinalIgnoreCase));
        configurations.Value = "Debug Mono;Release Mono";

        document.Save(projectPath);

        string conditionsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "build", "conditions.props");
        if (!File.Exists(conditionsPath))
        {
            return;
        }

        XDocument conditionsDocument = XDocument.Load(conditionsPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in conditionsDocument.Descendants().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if (condition.Contains("IsMono", StringComparison.OrdinalIgnoreCase) &&
                condition.Contains("!= 'true'", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }

        conditionsDocument.Save(conditionsPath);
    }

    private static XElement GetConfigurationPropertyGroup(string projectPath, string configuration)
    {
        XDocument document = XDocument.Load(projectPath);
        return document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            .First(element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains(configuration, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetWorkspaceRoot(out string? root)
    {
        if (workspaceRoot is not null)
        {
            root = workspaceRoot;
            return true;
        }

        workspaceRoot = TryFindWorkspaceRoot();
        root = workspaceRoot;
        return root is not null;
    }

    private static string? TryFindWorkspaceRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "AlwaysJackpot")) &&
                Directory.Exists(Path.Combine(current.FullName, "GunsAlwaysAccurate")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void CopyFixtureDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipFixturePath(sourceDirectory, directory))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipFixturePath(sourceDirectory, file))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            string destination = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(childDirectory, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    private static bool ShouldSkipFixturePath(string sourceDirectory, string path)
    {
        string relativePath = Path.GetRelativePath(sourceDirectory, path);
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".agent", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("s1interop-runs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("S1Interop.Generated", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("target", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Il2CppAssemblies", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("cpp2il_out", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("AssetRipper", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("AssetRipperExport", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("UnityExplorer", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("UniverseLib", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Cpp2IL", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Il2CppInterop", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
