---
uid: S1Interop.Core.MigrationPlanner
summary: Converts analysis diagnostics into a rollbackable migration plan.
---

# MigrationPlanner

`MigrationPlanner` is the dry-run side of migration. It turns <xref:S1Interop.Core.WorkspaceAnalysis> into a <xref:S1Interop.Core.MigrationPlan> without changing files.

The planner only emits operations the tool can describe explicitly. Unsupported IL2CPP risks remain visible as diagnostics or manual migration operations instead of being silently rewritten.
