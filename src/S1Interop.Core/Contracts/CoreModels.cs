using System.Text.Json.Serialization;

namespace S1Interop.Core.Contracts;

/// <summary>
/// Identifies the Schedule One runtime surface inferred for a project configuration.
/// </summary>
public enum RuntimeKind
{
    /// <summary>
    /// Indicates that S1Interop could not infer a specific runtime from the available project evidence.
    /// </summary>
    Unknown,

    /// <summary>
    /// Indicates a Mono runtime configuration that references the managed Schedule One assemblies.
    /// </summary>
    Mono,

    /// <summary>
    /// Indicates an IL2CPP runtime configuration that references MelonLoader-generated wrapper assemblies.
    /// </summary>
    Il2Cpp,

    /// <summary>
    /// Indicates a configuration intended to compile against a backend-neutral compatibility surface.
    /// </summary>
    CrossCompat
}

/// <summary>
/// Describes the severity of an analysis or migration diagnostic.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Provides informational evidence that does not require a migration operation.
    /// </summary>
    Info,

    /// <summary>
    /// Flags a condition that may block runtime compatibility or migration quality.
    /// </summary>
    Warning,

    /// <summary>
    /// Flags a condition that S1Interop expects callers to fix before treating the project as migration-ready.
    /// </summary>
    Error
}

/// <summary>
/// Represents the analysis result for a workspace path.
/// </summary>
/// <param name="RootPath">The full path that was analyzed.</param>
/// <param name="Projects">The project analyses discovered under <paramref name="RootPath"/>.</param>
public sealed record WorkspaceAnalysis(
    string RootPath,
    IReadOnlyList<ProjectAnalysis> Projects)
{
    /// <summary>
    /// Gets all diagnostics reported by every analyzed project.
    /// </summary>
    public IReadOnlyList<InteropDiagnostic> Diagnostics =>
        Projects.SelectMany(project => project.Diagnostics).ToArray();
}

/// <summary>
/// Represents the analysis result for a single C# project.
/// </summary>
/// <param name="ProjectPath">The full path to the analyzed project file.</param>
/// <param name="Configurations">The build configurations discovered from the project.</param>
/// <param name="Diagnostics">The project-level diagnostics produced by analysis.</param>
/// <param name="SourceInterop">Optional source-level interop analysis for migration risks and injected-type checks.</param>
public sealed record ProjectAnalysis(
    string ProjectPath,
    IReadOnlyList<ConfigurationAnalysis> Configurations,
    IReadOnlyList<InteropDiagnostic> Diagnostics,
    SourceInteropAnalysis? SourceInterop = null);

/// <summary>
/// Describes S1Interop's runtime classification for one project configuration.
/// </summary>
/// <param name="Name">The configuration name, such as <c>Debug</c> or <c>Debug Il2Cpp</c>.</param>
/// <param name="Runtime">The runtime kind inferred from references, target framework, defines, and evidence.</param>
/// <param name="MonoScore">The amount of evidence supporting a Mono classification.</param>
/// <param name="Il2CppScore">The amount of evidence supporting an IL2CPP classification.</param>
/// <param name="CrossCompatScore">The amount of evidence supporting a backend-neutral compatibility classification.</param>
/// <param name="TargetFramework">The target framework configured for this configuration, or null when none is resolved.</param>
/// <param name="DefineConstants">The conditional compilation symbols visible to this configuration.</param>
/// <param name="References">The assembly references visible to this configuration.</param>
/// <param name="PackageReferences">The NuGet package references visible to this configuration.</param>
/// <param name="Evidence">Human-readable evidence used to classify the configuration.</param>
public sealed record ConfigurationAnalysis(
    string Name,
    RuntimeKind Runtime,
    int MonoScore,
    int Il2CppScore,
    int CrossCompatScore,
    string? TargetFramework,
    IReadOnlyList<string> DefineConstants,
    IReadOnlyList<ReferenceInfo> References,
    IReadOnlyList<PackageReferenceInfo> PackageReferences,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Describes an assembly reference found in a project or imported project file.
/// </summary>
/// <param name="Include">The reference identity from the MSBuild <c>Include</c> attribute.</param>
/// <param name="HintPath">The resolved or declared hint path, or null when the reference has no hint path.</param>
/// <param name="Condition">The MSBuild condition attached to the reference, or null when the reference is unconditional.</param>
/// <param name="PrivateFalse">True when the reference explicitly disables copy-local output.</param>
/// <param name="SourcePath">The project or imported file that declared the reference, when known.</param>
/// <param name="Imported">True when the reference came from an imported MSBuild file.</param>
public sealed record ReferenceInfo(
    string Include,
    string? HintPath,
    string? Condition,
    bool PrivateFalse,
    string? SourcePath = null,
    bool Imported = false);

/// <summary>
/// Describes a NuGet package reference found in a project or imported project file.
/// </summary>
/// <param name="Include">The package ID from the MSBuild <c>Include</c> attribute.</param>
/// <param name="Version">The declared package version, or null when no version is declared on the item.</param>
/// <param name="Condition">The MSBuild condition attached to the package reference, or null when the reference is unconditional.</param>
/// <param name="SourcePath">The project or imported file that declared the package reference, when known.</param>
/// <param name="Imported">True when the package reference came from an imported MSBuild file.</param>
public sealed record PackageReferenceInfo(
    string Include,
    string? Version,
    string? Condition,
    string? SourcePath = null,
    bool Imported = false);

/// <summary>
/// Represents a diagnostic produced while analyzing, planning, or verifying interop readiness.
/// </summary>
/// <param name="RuleId">The stable diagnostic rule identifier.</param>
/// <param name="Severity">The severity assigned to the diagnostic.</param>
/// <param name="Message">The user-facing diagnostic message.</param>
/// <param name="ProjectPath">The project path associated with the diagnostic.</param>
/// <param name="Configuration">The configuration associated with the diagnostic, or null for project-wide diagnostics.</param>
/// <param name="Evidence">Optional evidence that explains where or why the diagnostic was produced.</param>
public sealed record InteropDiagnostic(
    string RuleId,
    DiagnosticSeverity Severity,
    string Message,
    string ProjectPath,
    string? Configuration,
    string? Evidence);

/// <summary>
/// Represents the migration operations planned for a workspace.
/// </summary>
/// <param name="RootPath">The analyzed workspace or project root path.</param>
/// <param name="Projects">The per-project migration plans.</param>
public sealed record MigrationPlan(
    string RootPath,
    IReadOnlyList<ProjectMigrationPlan> Projects);

/// <summary>
/// Configures how migration operations are planned from an analysis result.
/// </summary>
/// <param name="DualRuntime">True to plan separate Mono and IL2CPP build configuration scaffolding when needed.</param>
/// <param name="BuildHook">True to plan installation of a project-local build validation hook.</param>
/// <param name="IncludeSourceRisks">True to include source-level IL2CPP risk rewrites and reports in the plan.</param>
public sealed record MigrationPlannerOptions(bool DualRuntime, bool BuildHook = false, bool IncludeSourceRisks = true)
{
    /// <summary>
    /// Gets the default planning options for a backend-neutral migration pass without dual-runtime scaffolding.
    /// </summary>
    public static MigrationPlannerOptions Default { get; } = new(DualRuntime: false);
}

/// <summary>
/// Configures sandbox verification for a migration plan.
/// </summary>
/// <param name="DualRuntime">True to include dual-runtime planning operations during verification.</param>
/// <param name="Build">True to build the migrated sandbox after diagnostics are cleared.</param>
/// <param name="BuildTimeoutSeconds">The maximum number of seconds to wait for each sandbox build.</param>
/// <param name="IncludeSourceMigrations">True to apply source-level migration operations while verifying.</param>
/// <param name="Il2CppGamePath">Optional local IL2CPP game install path used to hydrate sandbox references.</param>
/// <param name="MonoGamePath">Optional local Mono game install path used to hydrate sandbox references.</param>
public sealed record MigrationVerifierOptions(
    bool DualRuntime,
    bool Build = false,
    int BuildTimeoutSeconds = 120,
    bool IncludeSourceMigrations = false,
    string? Il2CppGamePath = null,
    string? MonoGamePath = null)
{
    /// <summary>
    /// Gets the default verification options for a backend-neutral diagnostic pass without sandbox builds.
    /// </summary>
    public static MigrationVerifierOptions Default { get; } = new(DualRuntime: false);
}

/// <summary>
/// Represents planned migration operations for one project.
/// </summary>
/// <param name="ProjectPath">The full path to the project file being migrated.</param>
/// <param name="Operations">The operations S1Interop can report or apply for this project.</param>
public sealed record ProjectMigrationPlan(
    string ProjectPath,
    IReadOnlyList<MigrationOperation> Operations);

/// <summary>
/// Describes one migration action that can be reported to a caller and may be applied automatically.
/// </summary>
/// <param name="RuleId">The rule or operation identifier.</param>
/// <param name="FilePath">The project or source file path affected by the operation.</param>
/// <param name="Configuration">The build configuration affected by the operation, or null for project-wide changes.</param>
/// <param name="Risk">The relative migration risk reported to callers.</param>
/// <param name="Automatic">True when <see cref="S1Interop.Core.Migration.MigrationApplier"/> can apply the operation.</param>
/// <param name="Description">The user-facing operation description.</param>
/// <param name="Evidence">Optional evidence that explains why the operation was planned.</param>
public sealed record MigrationOperation(
    string RuleId,
    string FilePath,
    string? Configuration,
    string Risk,
    bool Automatic,
    string Description,
    string? Evidence = null);

/// <summary>
/// Describes backend-neutral SDK facade source that can be generated for a project.
/// </summary>
/// <param name="ProjectPath">The project that owns the generated facade source.</param>
/// <param name="OutputPath">The source file path where the facade should be written.</param>
/// <param name="ScheduleOneNamespaces">The Schedule One namespaces detected for facade generation.</param>
/// <param name="TypeAliases">The specific type aliases detected or requested for facade generation.</param>
/// <param name="HasContent">True when the plan contains at least one namespace import or type alias to generate.</param>
public sealed record SdkFacadePlan(
    string ProjectPath,
    string OutputPath,
    IReadOnlyList<string> ScheduleOneNamespaces,
    IReadOnlyList<SdkTypeAlias> TypeAliases,
    bool HasContent)
{
    /// <summary>
    /// Gets namespace import declarations to include in the generated facade.
    /// </summary>
    public IReadOnlyList<SdkNamespaceImport> NamespaceImports { get; init; } = Array.Empty<SdkNamespaceImport>();
}

/// <summary>
/// Describes a Schedule One namespace that should be exposed through generated backend-neutral facades.
/// </summary>
/// <param name="Namespace">The Mono Schedule One namespace to expose.</param>
/// <param name="IncludeSubnamespaces">True to include child namespaces under <paramref name="Namespace"/>.</param>
/// <param name="IncludeMembers">True to discover public member helpers for generated types in the namespace.</param>
public sealed record SdkNamespaceImport(
    string Namespace,
    bool IncludeSubnamespaces,
    bool IncludeMembers = false);

/// <summary>
/// Describes one backend-neutral type alias between Mono and IL2CPP Schedule One type names.
/// </summary>
/// <param name="Alias">The generated alias or facade name used by mod source.</param>
/// <param name="MonoType">The Mono runtime type name.</param>
/// <param name="Il2CppType">The IL2CPP wrapper runtime type name.</param>
/// <param name="GenerateGlobalUsing">True to emit a global using alias for the generated facade.</param>
public sealed record SdkTypeAlias(
    string Alias,
    string MonoType,
    string Il2CppType,
    bool GenerateGlobalUsing = true);

/// <summary>
/// Represents source-level interop findings for one project.
/// </summary>
/// <param name="ProjectPath">The project path that was scanned.</param>
/// <param name="InjectedTypes">Project-owned types that appear to be registered with or injected into IL2CPP.</param>
/// <param name="Il2CppGuardEvidence">Evidence of runtime conditional guards in source files.</param>
/// <param name="BridgeEvidence">Evidence of explicit IL2CPP bridge types or helper usage.</param>
/// <param name="SourceRisks">Source patterns that may need migration rewrites or manual review.</param>
/// <param name="Diagnostics">Diagnostics produced during source-level analysis.</param>
public sealed record SourceInteropAnalysis(
    string ProjectPath,
    IReadOnlyList<InjectedTypeAnalysis> InjectedTypes,
    IReadOnlyList<string> Il2CppGuardEvidence,
    IReadOnlyList<string> BridgeEvidence,
    IReadOnlyList<SourceRisk> SourceRisks,
    IReadOnlyList<InteropDiagnostic> Diagnostics);

/// <summary>
/// Describes a source pattern that may not be portable between Mono and IL2CPP.
/// </summary>
/// <param name="Kind">The source-risk category.</param>
/// <param name="Risk">The relative risk level reported to callers.</param>
/// <param name="FilePath">The source file containing the risk.</param>
/// <param name="Line">The one-based source line number containing the risk.</param>
/// <param name="Message">The user-facing risk explanation.</param>
/// <param name="Evidence">The source evidence that triggered the finding.</param>
/// <param name="Remediation">Suggested migration guidance for this risk.</param>
public sealed record SourceRisk(
    string Kind,
    string Risk,
    string FilePath,
    int Line,
    string Message,
    string Evidence,
    string Remediation);

/// <summary>
/// Describes a project-owned type that appears to participate in IL2CPP type injection.
/// </summary>
/// <param name="Name">The injected type name.</param>
/// <param name="FilePath">The source file where the type is declared.</param>
/// <param name="Line">The one-based source line number where the type declaration starts.</param>
/// <param name="BaseType">The declared base type list captured from source.</param>
/// <param name="HasIntPtrConstructor">True when the type exposes the public <see cref="IntPtr"/> constructor expected by IL2CPP injection.</param>
/// <param name="HasDerivedConstructorPointer">True when the source calls <c>ClassInjector.DerivedConstructorPointer&lt;T&gt;()</c>.</param>
/// <param name="HasDerivedConstructorBody">True when the source calls <c>ClassInjector.DerivedConstructorBody(this)</c>.</param>
/// <param name="HiddenMembers">Public members marked to be hidden from the IL2CPP surface.</param>
public sealed record InjectedTypeAnalysis(
    string Name,
    string FilePath,
    int Line,
    string BaseType,
    bool HasIntPtrConstructor,
    bool HasDerivedConstructorPointer,
    bool HasDerivedConstructorBody,
    IReadOnlyList<string> HiddenMembers);

/// <summary>
/// Reports the result of applying a migration plan.
/// </summary>
/// <param name="RunId">The unique migration run identifier.</param>
/// <param name="ManifestPath">The manifest path used for rollback.</param>
/// <param name="Operations">The operations that were applied.</param>
/// <param name="FileChanges">The files created or changed by the apply pass.</param>
public sealed record MigrationApplyResult(
    string RunId,
    string ManifestPath,
    IReadOnlyList<MigrationOperation> Operations,
    IReadOnlyList<MigrationFileChange> FileChanges);

/// <summary>
/// Describes one file created or changed during a migration apply pass.
/// </summary>
/// <param name="FilePath">The changed file path.</param>
/// <param name="BackupPath">The backup path recorded for rollback.</param>
/// <param name="OriginalHash">The original file hash, or null for newly created files.</param>
/// <param name="NewHash">The post-apply file hash, or null when no new file content is available.</param>
/// <param name="Created">True when the file was created by the apply pass.</param>
public sealed record MigrationFileChange(
    string FilePath,
    string BackupPath,
    string? OriginalHash,
    string? NewHash,
    bool Created);

/// <summary>
/// Reports the result of rolling back a migration apply run.
/// </summary>
/// <param name="RunId">The migration run identifier read from the rollback manifest.</param>
/// <param name="RestoredFiles">Files restored from backups.</param>
/// <param name="RemovedFiles">Files removed because they were created by the apply run.</param>
public sealed record MigrationRollbackResult(
    string RunId,
    IReadOnlyList<string> RestoredFiles,
    IReadOnlyList<string> RemovedFiles);

/// <summary>
/// Reports sandbox verification for one project migration.
/// </summary>
/// <param name="SourceProjectPath">The original project path supplied for verification.</param>
/// <param name="SandboxProjectPath">The temporary sandbox project path used during verification.</param>
/// <param name="Success">True when planned migration passes cleared diagnostics and optional builds succeeded.</param>
/// <param name="SandboxDeleted">True when the temporary sandbox was deleted after verification.</param>
/// <param name="PlannedOperations">The total number of operations planned across verification passes.</param>
/// <param name="AppliedOperations">The total number of operations actually applied in the sandbox.</param>
/// <param name="ManifestPath">The last sandbox migration manifest path, or null when no operations were applied.</param>
/// <param name="BeforeDiagnostics">Diagnostics found before sandbox migration.</param>
/// <param name="AfterDiagnostics">Diagnostics remaining after sandbox migration.</param>
/// <param name="BuildResults">Optional build results captured when build verification is enabled.</param>
public sealed record MigrationVerificationResult(
    string SourceProjectPath,
    string SandboxProjectPath,
    bool Success,
    bool SandboxDeleted,
    int PlannedOperations,
    int AppliedOperations,
    string? ManifestPath,
    IReadOnlyList<InteropDiagnostic> BeforeDiagnostics,
    IReadOnlyList<InteropDiagnostic> AfterDiagnostics,
    IReadOnlyList<MigrationBuildResult>? BuildResults = null);

/// <summary>
/// Reports the result of building one migrated sandbox configuration.
/// </summary>
/// <param name="Configuration">The configuration that was built.</param>
/// <param name="Runtime">The runtime classification for the configuration.</param>
/// <param name="Success">True when the build completed successfully.</param>
/// <param name="TimedOut">True when the build exceeded the configured timeout.</param>
/// <param name="ExitCode">The process exit code, or -1 when no build process completed.</param>
/// <param name="ReadinessStatus">A high-level readiness classification for the build attempt.</param>
/// <param name="Attribution">Whether failure is attributed to migration output, local references, restore state, or another category.</param>
/// <param name="FailureKind">The normalized failure category, or <c>None</c> for successful builds.</param>
/// <param name="Summary">The most useful one-line build outcome for users.</param>
/// <param name="Issues">Structured readiness or build issues extracted from the attempt.</param>
/// <param name="Command">The command line used for the sandbox build.</param>
/// <param name="Output">The bounded build output captured from the process.</param>
public sealed record MigrationBuildResult(
    string Configuration,
    RuntimeKind Runtime,
    bool Success,
    bool TimedOut,
    int ExitCode,
    string ReadinessStatus,
    string Attribution,
    string FailureKind,
    string Summary,
    IReadOnlyList<MigrationBuildIssue> Issues,
    string Command,
    string Output);

/// <summary>
/// Describes one readiness or build issue from sandbox build verification.
/// </summary>
/// <param name="Kind">The normalized issue category.</param>
/// <param name="Message">The user-facing issue message.</param>
/// <param name="Include">The reference or package identity involved in the issue, when known.</param>
/// <param name="Path">The path associated with the issue, when known.</param>
/// <param name="SourcePath">The project or imported file that declared the issue source, when known.</param>
/// <param name="Imported">True when the issue came from an imported project file.</param>
/// <param name="Remediation">Suggested remediation for the issue, when available.</param>
/// <param name="Version">The package version associated with the issue, when known.</param>
/// <param name="RestoreSources">Restore sources reported by NuGet for package resolution issues.</param>
public sealed record MigrationBuildIssue(
    string Kind,
    string Message,
    string? Include = null,
    string? Path = null,
    string? SourcePath = null,
    bool Imported = false,
    string? Remediation = null,
    string? Version = null,
    IReadOnlyList<string>? RestoreSources = null);

/// <summary>
/// Reports sandbox verification across all projects discovered in a workspace.
/// </summary>
/// <param name="RootPath">The workspace path supplied for verification.</param>
/// <param name="Success">True when every project verification succeeded and every sandbox was deleted.</param>
/// <param name="ProjectCount">The number of projects verified.</param>
/// <param name="PlannedOperations">The total number of planned operations across all projects.</param>
/// <param name="AppliedOperations">The total number of applied operations across all projects.</param>
/// <param name="Projects">The per-project verification results.</param>
public sealed record WorkspaceMigrationVerificationResult(
    string RootPath,
    bool Success,
    int ProjectCount,
    int PlannedOperations,
    int AppliedOperations,
    IReadOnlyList<MigrationVerificationResult> Projects);

/// <summary>
/// Provides source-generated JSON metadata for S1Interop public contract models.
/// </summary>
[JsonSerializable(typeof(WorkspaceAnalysis))]
[JsonSerializable(typeof(ProjectAnalysis))]
[JsonSerializable(typeof(ConfigurationAnalysis))]
[JsonSerializable(typeof(ReferenceInfo))]
[JsonSerializable(typeof(PackageReferenceInfo))]
[JsonSerializable(typeof(InteropDiagnostic))]
[JsonSerializable(typeof(MigrationPlan))]
[JsonSerializable(typeof(ProjectMigrationPlan))]
[JsonSerializable(typeof(MigrationOperation))]
[JsonSerializable(typeof(MigrationApplyResult))]
[JsonSerializable(typeof(MigrationFileChange))]
[JsonSerializable(typeof(MigrationRollbackResult))]
[JsonSerializable(typeof(MigrationVerificationResult))]
[JsonSerializable(typeof(MigrationBuildResult))]
[JsonSerializable(typeof(MigrationBuildIssue))]
[JsonSerializable(typeof(WorkspaceMigrationVerificationResult))]
[JsonSerializable(typeof(SdkFacadePlan))]
[JsonSerializable(typeof(SourceInteropAnalysis))]
[JsonSerializable(typeof(SourceRisk))]
[JsonSerializable(typeof(InjectedTypeAnalysis))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public sealed partial class S1InteropJsonContext : JsonSerializerContext;
