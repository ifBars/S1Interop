using System.Text.Json.Serialization;

namespace S1Interop.Core.Contracts;

public enum RuntimeKind
{
    Unknown,
    Mono,
    Il2Cpp,
    CrossCompat
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record WorkspaceAnalysis(
    string RootPath,
    IReadOnlyList<ProjectAnalysis> Projects)
{
    public IReadOnlyList<InteropDiagnostic> Diagnostics =>
        Projects.SelectMany(project => project.Diagnostics).ToArray();
}

public sealed record ProjectAnalysis(
    string ProjectPath,
    IReadOnlyList<ConfigurationAnalysis> Configurations,
    IReadOnlyList<InteropDiagnostic> Diagnostics,
    SourceInteropAnalysis? SourceInterop = null);

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

public sealed record ReferenceInfo(
    string Include,
    string? HintPath,
    string? Condition,
    bool PrivateFalse,
    string? SourcePath = null,
    bool Imported = false);

public sealed record PackageReferenceInfo(
    string Include,
    string? Version,
    string? Condition,
    string? SourcePath = null,
    bool Imported = false);

public sealed record InteropDiagnostic(
    string RuleId,
    DiagnosticSeverity Severity,
    string Message,
    string ProjectPath,
    string? Configuration,
    string? Evidence);

public sealed record MigrationPlan(
    string RootPath,
    IReadOnlyList<ProjectMigrationPlan> Projects);

public sealed record MigrationPlannerOptions(bool DualRuntime, bool BuildHook = false, bool IncludeSourceRisks = true)
{
    public static MigrationPlannerOptions Default { get; } = new(DualRuntime: false);
}

public sealed record MigrationVerifierOptions(
    bool DualRuntime,
    bool Build = false,
    int BuildTimeoutSeconds = 120,
    bool IncludeSourceMigrations = false,
    string? Il2CppGamePath = null,
    string? MonoGamePath = null)
{
    public static MigrationVerifierOptions Default { get; } = new(DualRuntime: false);
}

public sealed record ProjectMigrationPlan(
    string ProjectPath,
    IReadOnlyList<MigrationOperation> Operations);

public sealed record MigrationOperation(
    string RuleId,
    string FilePath,
    string? Configuration,
    string Risk,
    bool Automatic,
    string Description,
    string? Evidence = null);

public sealed record SdkFacadePlan(
    string ProjectPath,
    string OutputPath,
    IReadOnlyList<string> ScheduleOneNamespaces,
    IReadOnlyList<SdkTypeAlias> TypeAliases,
    bool HasContent);

public sealed record SdkTypeAlias(
    string Alias,
    string MonoType,
    string Il2CppType,
    bool GenerateGlobalUsing = true);

public sealed record SourceInteropAnalysis(
    string ProjectPath,
    IReadOnlyList<InjectedTypeAnalysis> InjectedTypes,
    IReadOnlyList<string> Il2CppGuardEvidence,
    IReadOnlyList<string> BridgeEvidence,
    IReadOnlyList<SourceRisk> SourceRisks,
    IReadOnlyList<InteropDiagnostic> Diagnostics);

public sealed record SourceRisk(
    string Kind,
    string Risk,
    string FilePath,
    int Line,
    string Message,
    string Evidence,
    string Remediation);

public sealed record InjectedTypeAnalysis(
    string Name,
    string FilePath,
    int Line,
    string BaseType,
    bool HasIntPtrConstructor,
    bool HasDerivedConstructorPointer,
    bool HasDerivedConstructorBody,
    IReadOnlyList<string> HiddenMembers);

public sealed record MigrationApplyResult(
    string RunId,
    string ManifestPath,
    IReadOnlyList<MigrationOperation> Operations,
    IReadOnlyList<MigrationFileChange> FileChanges);

public sealed record MigrationFileChange(
    string FilePath,
    string BackupPath,
    string? OriginalHash,
    string? NewHash,
    bool Created);

public sealed record MigrationRollbackResult(
    string RunId,
    IReadOnlyList<string> RestoredFiles,
    IReadOnlyList<string> RemovedFiles);

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

public sealed record WorkspaceMigrationVerificationResult(
    string RootPath,
    bool Success,
    int ProjectCount,
    int PlannedOperations,
    int AppliedOperations,
    IReadOnlyList<MigrationVerificationResult> Projects);

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
