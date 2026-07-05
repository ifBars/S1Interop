---
uid: S1Interop.Core
summary: Stable result models and library entry points for S1Interop tooling.
---

# S1Interop.Core

`S1Interop.Core` is the library surface behind the CLI workflows.

Most mod authors should start with the CLI docs rather than this API reference. Use these pages when you are building automation around S1Interop, reading result models, or embedding analysis and migration planning in another tool.

The public reference is filtered on purpose. It includes the stable contracts and library entry points used by `analyze`, `sdkgen`, `migrate`, `new`, and `verify-migration`. It does not document CLI presentation code, Roslyn generator implementation details, source rewriters, or other internal helpers that may change while the alpha is still moving.

The namespaces follow the same broad shape as S1API's feature folders: analysis code lives under `S1Interop.Core.Analysis`, migration code under `S1Interop.Core.Migration`, shared result models under `S1Interop.Core.Contracts`, and package metadata under `S1Interop.Core.Packaging`.

## Common entry points

| API | Use it when |
| --- | --- |
| <xref:S1Interop.Core.Analysis.WorkspaceAnalyzer> | You need to inspect projects and get diagnostics. |
| <xref:S1Interop.Core.Migration.MigrationPlanner> | You need a dry-run migration plan from an analysis result. |
| <xref:S1Interop.Core.Migration.MigrationApplier> | You need to apply a migration plan and get rollback metadata. |
| <xref:S1Interop.Core.Migration.MigrationVerifier> | You need sandbox verification for a project or workspace. |
| <xref:S1Interop.Core.Scaffolding.BackendNeutralProjectScaffolder> | You need to create the same backend-neutral project shape as `s1interop new`. |

## Result models

The `WorkspaceAnalysis`, `MigrationPlan`, `MigrationApplyResult`, and `MigrationVerificationResult` families live under <xref:S1Interop.Core.Contracts>. They are the primary contracts for tooling integrations and the types serialized by the CLI when JSON output is added or consumed by test harnesses.
