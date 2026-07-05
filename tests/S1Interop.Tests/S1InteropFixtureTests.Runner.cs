internal sealed partial class S1InteropFixtureTests
{
    private readonly WorkspaceAnalyzer analyzer = new();
    private string? repositoryRoot;
    private string? workspaceRoot;

    private string RepositoryRoot
    {
        get
        {
            repositoryRoot ??= TryFindRepositoryRoot();
            if (repositoryRoot is null)
            {
                throw new DirectoryNotFoundException("Could not locate the S1Interop repository root required by portable tests.");
            }

            return repositoryRoot;
        }
    }

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

    public int RunQuick()
    {
        int count = 0;
        CliVersionPrintsPackageVersionWithoutAnalyzingWorkspace();
        count++;
        CliRejectsInvalidOptionsBeforeDispatch();
        count++;
        CliHelpUsageLinesAreDocumented();
        count++;
        PackageInfoMatchesPackageProjects();
        count++;
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
        MemberAccessTargetCatalogDiscoversTypedFieldPropertyHelperCalls();
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
        NewCommandProjectCanSeedFullBackendNeutralSdkFromReferenceMetadata();
        count++;
        MigrationApplyAndRollbackGeneratesSourceRiskReport();
        count++;
        VerifyMigrationCanIncludeSourceMigrationsInSandbox();
        count++;
        MsBuildOsPlatformConditionsAreEvaluated();
        count++;
        LocalPathDiagnosticsDetectAnyWindowsDriveLetter();
        count++;
        MigrationUsesRuntimeGamePathSlotsForCustomRuntimeConfigurations();
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
        MigrationVerifierSkipsWindowsReservedDeviceNames();
        count++;
        VerifyMigrationReportsResidualDiagnosticsOnBrokenInjectedType();
        count++;
        MigrationApplyAddsIntPtrConstructorToMonoBehaviourInjectedType();
        count++;
        MigrationApplyRegistersMonoOnlyInjectedComponentTypes();
        count++;
        SourceInteropAnalyzerIgnoresGeneratedAndToolDirectories();
        count++;
        ScheduleOneUsingRewriterGroupsAdjacentUsings();
        count++;
        ScheduleOneUsingRewriterCanPreferGlobalFacade();
        count++;
        S1InteropTypeRegistryGeneratorProducesBackendSpecificReflectionCache();
        count++;
        S1InteropTypeRegistryGeneratorDiscoversPublicTypeMembers();
        count++;
        S1InteropTypeRegistryGeneratorExpandsNamespaceDeclarations();
        count++;
        S1InteropTypeRegistryGeneratorOwnerQualifiesDiscoveredMemberAliases();
        count++;
        S1InteropTypeRegistryGeneratorMergesDuplicateAliases();
        count++;
        S1InteropTypeRegistryGeneratorValidatesDeclaredTypesAgainstReferencedGameAssemblies();
        count++;
        S1InteropTypeRegistryGeneratorReportsMissingDeclaredTypesWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorReportsMissingIl2CppDeclaredTypesWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorSkipsReferenceValidationWhenGameReferencesAreAbsent();
        count++;
        S1InteropTypeRegistryGeneratorReportsMissingDeclaredMembersWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorValidatesMethodParameterAliasesAgainstReferencedGameAssemblies();
        count++;
        S1InteropTypeRegistryGeneratorReportsWrongMethodParameterTypesWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorReportsIl2CppSourceBoundaryDiagnostics();
        count++;
        BackendNeutralRuntimeDetectsDefaultBackendMarkersWithoutTypeAliases();
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

    public int RunPortable()
    {
        int count = 0;
        CliVersionPrintsPackageVersionWithoutAnalyzingWorkspace();
        count++;
        CliRejectsInvalidOptionsBeforeDispatch();
        count++;
        CliHelpUsageLinesAreDocumented();
        count++;
        PackageInfoMatchesPackageProjects();
        count++;
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
        MemberAccessTargetCatalogDiscoversTypedFieldPropertyHelperCalls();
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
        NewCommandProjectCanSeedFullBackendNeutralSdkFromReferenceMetadata();
        count++;
        MigrationApplyAndRollbackGeneratesSourceRiskReport();
        count++;
        VerifyMigrationCanIncludeSourceMigrationsInSandbox();
        count++;
        MsBuildOsPlatformConditionsAreEvaluated();
        count++;
        LocalPathDiagnosticsDetectAnyWindowsDriveLetter();
        count++;
        MigrationUsesRuntimeGamePathSlotsForCustomRuntimeConfigurations();
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
        VerifyMigrationBuildGateDoesNotForwardUnexpandedLocalProperties();
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
        S1InteropTypeRegistryGeneratorDiscoversPublicTypeMembers();
        count++;
        S1InteropTypeRegistryGeneratorOwnerQualifiesDiscoveredMemberAliases();
        count++;
        S1InteropTypeRegistryGeneratorMergesDuplicateAliases();
        count++;
        S1InteropTypeRegistryGeneratorValidatesDeclaredTypesAgainstReferencedGameAssemblies();
        count++;
        S1InteropTypeRegistryGeneratorReportsMissingDeclaredTypesWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorReportsMissingIl2CppDeclaredTypesWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorSkipsReferenceValidationWhenGameReferencesAreAbsent();
        count++;
        S1InteropTypeRegistryGeneratorReportsMissingDeclaredMembersWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorValidatesMethodParameterAliasesAgainstReferencedGameAssemblies();
        count++;
        S1InteropTypeRegistryGeneratorReportsWrongMethodParameterTypesWhenGameReferencesExist();
        count++;
        S1InteropTypeRegistryGeneratorReportsIl2CppSourceBoundaryDiagnostics();
        count++;
        BackendNeutralRuntimeDetectsDefaultBackendMarkersWithoutTypeAliases();
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
        BackendNeutralScaffoldBuildsRealS1FuelModFacadeTargetsAgainstBothReferenceSurfaces();
        count++;
        MigrationApplyGeneratesS1FuelModMemberAccessFacadesThatBuildAgainstBothReferenceSurfaces();
        count++;
        SdkFacadeGeneratorDetectsBarsGraphicsBackendAliasPairs();
        count++;
        BackendNeutralScaffoldBuildsRealBarsGraphicsFacadeTargetsAgainstBothReferenceSurfaces();
        count++;
        SdkGenApplyGeneratesBarsGraphicsFacadesFromReferenceMetadata();
        count++;
        MigrationApplyAndRollbackAddsHideFromIl2CppOnS1FuelModFixture();
        count++;
        MigrationApplyConditionalizesS1FuelModGameConstructor();
        count++;
        VerifyMigrationSucceedsOnS1FuelModWithoutMutatingSource();
        count++;
        VerifyMigrationBuildGateConvertsMonoOnlyS1FuelModCopy();
        count++;
        VerifyMigrationCleansBigWillyPropertyBasedReferences();
        count++;
        VerifyMigrationMovesBetterJukeboxAbsoluteHintPaths();
        count++;
        VerifyMigrationPreservesBguiMixedConfigurationPaths();
        count++;
        VerifyMigrationConvergesOnHoverboardWithoutMutatingSource();
        count++;
        BackendNeutralScaffoldBuildsRealHoverboardNamespaceScopedFacadeTargets();
        count++;
        SdkGenApplyGeneratesHoverboardFacadesFromReferenceMetadata();
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

    public int RunIntegrationBackendNeutral(bool requireWorkspace)
    {
        if (!CanRunIntegration(requireWorkspace))
        {
            return 0;
        }

        int count = 0;
        S1FuelModInjectedTypesAreAnalyzed();
        count++;
        BackendNeutralRegistryCompilesRealS1FuelModFacadeTargets();
        count++;
        MigrationApplyGeneratesS1FuelModMemberAccessFacadesThatBuildAgainstBothReferenceSurfaces();
        count++;
        SdkFacadeGeneratorDetectsBarsGraphicsBackendAliasPairs();
        count++;
        SdkGenApplyGeneratesBarsGraphicsFacadesFromReferenceMetadata();
        count++;
        VerifyMigrationConvergesOnHoverboardWithoutMutatingSource();
        count++;
        SdkGenApplyGeneratesHoverboardFacadesFromReferenceMetadata();
        count++;
        SdkFacadeGeneratorDetectsGunsAlwaysAccurateNamespaces();
        count++;
        DuplicateLangVersionRealModsDoNotRequireCSharp10Migration();
        count++;
        return count;
    }

    public int RunIntegrationHoverboard(bool requireWorkspace)
    {
        if (!CanRunIntegration(requireWorkspace))
        {
            return 0;
        }

        int count = 0;
        VerifyMigrationConvergesOnHoverboardWithoutMutatingSource();
        count++;
        SdkGenApplyGeneratesHoverboardFacadesFromReferenceMetadata();
        count++;
        DuplicateLangVersionRealModsDoNotRequireCSharp10Migration();
        count++;
        return count;
    }

    public int RunIntegrationBuildGates(bool requireWorkspace)
    {
        if (!CanRunIntegration(requireWorkspace))
        {
            return 0;
        }

        int count = 0;
        VerifyMigrationBuildGateConvertsMonoOnlyS1FuelModCopy();
        count++;
        VerifyMigrationBuildGateConvertsMonoOnlyBotanistFixCopy();
        count++;
        VerifyMigrationBuildGateConvertsMonoOnlyS1VoiceChatCopy();
        count++;
        VerifyMigrationBuildGateConvertsRealBarsGraphics();
        count++;
        VerifyMigrationBuildGateCollapsesStagedIl2CppWrapperReferences();
        count++;
        return count;
    }

    private bool CanRunIntegration(bool requireWorkspace)
    {
        if (TryGetWorkspaceRoot(out _))
        {
            return true;
        }

        if (requireWorkspace)
        {
            throw new DirectoryNotFoundException("Could not locate the broader ScheduleOne workspace required by integration tests.");
        }

        Console.WriteLine("Skipping local integration fixtures because the broader ScheduleOne workspace was not found.");
        return false;
    }
}
