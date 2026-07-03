# Architecture

S1Interop is split into three deliverable assemblies plus a fixture test harness:

- `S1Interop.Cli`: command-line entry point and user-facing output.
- `S1Interop.Core`: analysis, migration, rewriting, generation, rollback, and verification engine.
- `S1Interop.Generators`: Roslyn source-generator package used by migrated or backend-neutral mod projects.
- `S1Interop.Tests`: executable fixture harness for portable and local integration coverage.

The core design principle is separation between discovery, decision-making, edits, and verification. A migration should be explainable before it is applied, reversible after it is applied, and safe to test in a sandbox before touching a real project.

## High-Level Flow

```text
CLI command
  -> parse command arguments
  -> analyze project or workspace
  -> plan diagnostics and migration operations
  -> optionally apply changes with backups and manifest
  -> optionally verify in a sandbox
  -> report text or JSON output
```

For a typical dual-runtime migration:

```text
ProjectDiscovery
  -> WorkspaceAnalyzer
  -> CsprojAnalyzer + SourceInteropAnalyzer
  -> MigrationPlanner
  -> MigrationApplier
  -> MigrationVerifier
```

## CLI Layer

Path: `src/S1Interop.Cli/`

Responsibilities:

- Parse supported commands and options.
- Dispatch to command handlers.
- Keep output stable and readable.
- Translate Core results to text or JSON.

Important areas:

- `CommandLine/`: command metadata and parsed command shape.
- `Commands/`: command execution paths.
- `Reporting/`: help text and result formatting.
- `S1InteropCli.cs`: top-level CLI orchestration.

The CLI should stay thin. If a behavior is useful outside command-line output, put it in `S1Interop.Core`.

## Core Layer

Path: `src/S1Interop.Core/`

### Analysis

Path: `src/S1Interop.Core/Analysis/`

Analysis discovers projects, evaluates project metadata, and reports migration diagnostics without editing files.

Key types:

- `ProjectDiscovery`: finds project files under a path.
- `WorkspaceAnalyzer`: coordinates project and source analysis.
- `CsprojAnalyzer`: reads project/configuration/reference metadata.
- `SourceInteropAnalyzer`: finds source-level Mono/IL2CPP risks.
- `HarmonyMethodTargetCatalog` and `MemberAccessTargetCatalog`: discover reflection/member targets that can feed generated helpers.
- `WorkspaceTraversal`: shared traversal filters for generated, build, and tool output directories.

Analysis should be conservative. If a source shape is ambiguous, report it instead of rewriting it.

### Migration

Path: `src/S1Interop.Core/Migration/`

Migration converts analysis findings into operations, applies operations with backups, and validates the result.

Key types:

- `MigrationPlanner`: converts diagnostics and source risks into ordered operations.
- `MigrationApplier`: edits files, writes backups, and records a manifest.
- `MigrationVerifier`: copies a project into a sandbox, applies migration, optionally builds, and returns classified results.
- `DualRuntimeProjectScaffolder`: adds inferred Mono/IL2CPP configuration shape.
- `SolutionConfigurationScaffolder`: updates sibling solution configurations for Visual Studio.
- `BuildValidationHook`: installs opt-in build-time validation target.
- `S1InteropGeneratorDetector`: detects generator package/project state.

Migration rules:

- Apply to sandbox copies during verification.
- Keep real project mutation behind explicit `--apply`.
- Write rollback metadata for applied changes.
- Preserve user-authored structure where possible.
- Prefer project XML operations over string edits for project files.

### Rewriting

Path: `src/S1Interop.Core/Rewriting/`

Rewriters handle source shapes that are narrow enough to transform safely.

Current examples:

- Schedule One using conditionalization.
- SDK type alias rewrites.
- UnityEvent listener bridging.
- Delegate assignment/argument bridging.
- Harmony overload method binding.
- Typed member access fallback rewrites.
- Direct member reflection lookup rewrites.
- IL2CPP object cast helper rewrites.

Rewriters should be idempotent. Running migration more than once must not degrade generated declarations or rewrite already migrated code into an invalid state.

### Migration-Time Generators

Path: `src/S1Interop.Core/Generators/`

These generators write source files into the target mod project during CLI migration. They are allowed to inspect the project on disk and coordinate with source rewrites.

Generated files include:

- Backend-neutral starter declarations.
- SDK/global using facades.
- UnityEvent and delegate bridge helpers.
- Harmony method target declarations.
- Member access target declarations.
- Source-risk reports.

These are not Roslyn source generators. They are part of the CLI migration pipeline.

## Roslyn Generator Layer

Path: `src/S1Interop.Generators/`

`S1Interop.Generators` is the package installed into mod projects through a private `PackageReference`. It consumes attributes such as:

- `S1InteropType`
- `S1InteropMember`
- bridge generation attributes

It emits generated helper source under `S1Interop.Generated`.

Roslyn generators are additive. They can generate source and diagnostics, but they cannot modify existing user source or rewrite IL. Any call-site transformation must happen through source migration before compilation or through a future IL-weaving layer.

## Backend-Neutral Compatibility Strategy

The long-term goal is to reduce manual Mono/IL2CPP conditionals and make more mods backend-neutral.

Current supported strategy:

1. Analyze runtime-specific project references and source risks.
2. Generate type/member declarations and helper surfaces from the game types the mod source actually uses.
3. Rewrite safe call sites to generated helpers.
4. Build and verify in sandboxed copies.
5. Report unsupported or ambiguous cases instead of guessing.

The generated SDK surface is intentionally usage-driven. `sdkgen` and migration-time facade generation inspect source plus local reference metadata, then emit declarations and type-scoped facades for the types a mod touches. When generated facade output contains `S1InteropType` attributes, the applied plan must also install `S1Interop.Generators`; the generated SDK source and Roslyn generator package are one compile-time unit. Avoid manual per-game-type wrappers or static catalogs of every Schedule One type; those do not scale and make drift harder to detect.

Future possible strategy:

- IL weaving may eventually rewrite compiled call sites to generated backend-specific adapters. Treat that as a separate backend with stronger verification requirements. It should not be folded into the Roslyn generator package as if source generation can mutate existing code.

## Test Harness

Path: `tests/S1Interop.Tests/`

The test harness is a console executable with explicit modes:

- `--quick`: fast analyzer, rewriter, migration-planning, and generator checks.
- `--portable`: CI-safe coverage without private local fixtures.
- `--integration`: local real-mod and game-path coverage.
- `--integration-hoverboard`: focused Hoverboard SDK/facade generation coverage.
- `--integration-backend-neutral`: focused real-mod backend-neutral facade coverage.
- `--integration-build-gates`: heavier real-mod build verification gates.
- `--all`: portable plus integration when local workspace dependencies exist.

Keep private local dependencies out of portable tests. Integration tests may depend on local mod copies and game installs, but they must skip cleanly or be explicit about missing prerequisites.

## Safety Boundaries

S1Interop must not redistribute game-owned material. Do not commit:

- Schedule One assemblies.
- MelonLoader generated IL2CPP wrapper assemblies.
- Decompiled source dumps.
- AssetRipper output.
- Game assets, prefabs, scenes, textures, or recordings.
- Machine-specific game paths or local props.

Use ignored locations for local data:

```text
local.build.props
Directory.Build.user.props
s1interop-runs/
s1interop-cache/
artifacts/
.internal/
docs/internal/
*.internal.md
*.local.md
```

## Packaging

The CLI packages as a local/global .NET tool with package id `S1Interop`.

The Roslyn generator package is `S1Interop.Generators` and packages the built generator DLL under `analyzers/dotnet/cs`.

CI verifies:

- restore
- Release build
- portable tests
- CLI package
- generator package
- local tool install
- `s1interop --help` smoke command
