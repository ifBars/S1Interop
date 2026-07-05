# API reference

The API reference is for tooling integrations. It is not the main path for writing mods.

Use the conceptual guides for workflows and architecture, then use the generated API reference for exact signatures, stable result contracts, and XML documentation.

Most developers should use the CLI and generated SDK docs first:

- [New projects](new-projects.md) for backend-neutral scaffolding.
- [Backend-neutral SDK](backend-neutral-sdk.md) for generated facades.
- [Migration](migrating-mono-mods.md) for existing Mono mods.
- [Diagnostics](diagnostics.md) for compile-time checks.

The generated reference only includes stable `S1Interop.Core` contracts:

- analysis and diagnostic models;
- migration planning, apply, rollback, and verification result models;
- entry points for analysis, planning, applying, verification, and project scaffolding.

It skips CLI formatting code, Roslyn generator internals, source rewriters, and implementation helpers. Those pieces are still expected to change during alpha work. Record result models stay compact in the generated reference, while callable entry points expose their documented methods.

Start with <xref:S1Interop.Core> when you need the library surface.

## Typical integration flow

Most tooling should treat analysis, planning, apply, and verification as separate steps:

1. Run <xref:S1Interop.Core.Analysis.WorkspaceAnalyzer> to collect project classifications and diagnostics.
2. Pass the result to <xref:S1Interop.Core.Migration.MigrationPlanner> when you need a dry-run list of migration operations.
3. Use <xref:S1Interop.Core.Migration.MigrationVerifier> for CI, regression probes, or real-mod experiments that must not mutate the original project.
4. Use <xref:S1Interop.Core.Migration.MigrationApplier> only when the caller has chosen to write changes and preserve rollback metadata.

```csharp
using S1Interop.Core.Analysis;
using S1Interop.Core.Contracts;
using S1Interop.Core.Migration;

var analyzer = new WorkspaceAnalyzer();
var analysis = analyzer.Analyze(projectOrWorkspacePath);

var planner = new MigrationPlanner();
var plan = planner.Plan(
    analysis,
    new MigrationPlannerOptions(DualRuntime: true, BuildHook: false));

var verifier = new MigrationVerifier();
var verification = verifier.Verify(
    projectOrWorkspacePath,
    new MigrationVerifierOptions(DualRuntime: true, Build: false));
```

Apply operations only after reviewing the plan. The applier writes backups and a manifest under `s1interop-runs/`; the verifier performs the same style of migration work in a temporary sandbox and is usually the safer automation boundary.

## Result contracts

Use the contract models when you need stable data for logs, reports, or another tool:

| Contract | Contains |
| --- | --- |
| <xref:S1Interop.Core.Contracts.WorkspaceAnalysis> | Projects, configuration evidence, and diagnostics from analysis. |
| <xref:S1Interop.Core.Contracts.MigrationPlan> | Planned migration operations grouped by project. |
| <xref:S1Interop.Core.Contracts.MigrationApplyResult> | Applied operations, changed files, and rollback manifest path. |
| <xref:S1Interop.Core.Contracts.MigrationVerificationResult> | Sandbox diagnostics before and after migration, optional build results, and cleanup status. |
| <xref:S1Interop.Core.Contracts.MigrationBuildResult> | Per-configuration sandbox build outcome and classified failure details. |
