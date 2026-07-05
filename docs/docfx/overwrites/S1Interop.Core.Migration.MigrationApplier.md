---
uid: S1Interop.Core.Migration.MigrationApplier
summary: Applies automatic migration operations and records rollback metadata.
---

# MigrationApplier

`MigrationApplier` applies automatic operations from a <xref:S1Interop.Core.Contracts.MigrationPlan>.

Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`. The manifest is the input used by the rollback command, so callers should preserve the returned `ManifestPath` when they apply changes outside the CLI.
