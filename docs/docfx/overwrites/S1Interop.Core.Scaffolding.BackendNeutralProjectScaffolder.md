---
uid: S1Interop.Core.Scaffolding.BackendNeutralProjectScaffolder
summary: Creates the default dual-runtime or experimental backend-neutral starter used by the CLI.
---

# BackendNeutralProjectScaffolder

`BackendNeutralProjectScaffolder` backs `s1interop new`. The CLI uses the explicit Mono/IL2CPP shape by default and selects the backend-neutral shape only with `--backend-neutral`.

Use `CreatePlan` to inspect the files that would be created, then the mode-aware `Apply` overload to write the solution, project file, local path example, starter source, and README. The parameterless-mode legacy overload retains the experimental backend-neutral shape for API compatibility; new integrations should choose the mode explicitly.
