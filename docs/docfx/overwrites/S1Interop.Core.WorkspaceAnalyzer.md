---
uid: S1Interop.Core.WorkspaceAnalyzer
summary: Discovers Schedule One mod projects and reports their runtime shape, references, and S1Interop diagnostics.
---

# WorkspaceAnalyzer

Use `WorkspaceAnalyzer` when you need the same project inspection that powers `s1interop analyze`, `lint`, `migrate`, and `verify-migration`.

`Analyze` accepts a project file or directory. It returns a <xref:S1Interop.Core.WorkspaceAnalysis> containing discovered projects, configurations, runtime classification, references, package references, and diagnostics.
