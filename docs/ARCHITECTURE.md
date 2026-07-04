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
- ScheduleOne type lookup rewrites for `typeof(...)`, simple `AccessTools.TypeByName(...)`, and `Type.GetType(..., false)` calls.
- ScheduleOne type-facade invocation rewrites for simple object creation plus instance method calls.
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

Member access target discovery is scoped to backend/game surfaces and runs even when no source-risk report remains. Usage-driven declarations from helpers such as `ReflectionUtils.TryGetFieldOrProperty(vehicle, "vehicleName")` are generated as SDK surface because they make backend-neutral code more native-like. Discovery should not emit S1Interop declarations for MelonLoader-owned reflection internals such as `MelonEnvironment`, `MelonLogger`, or `MelonPreferences`; those are mod loader implementation details, not Schedule One Mono/IL2CPP wrapper drift.

These are not Roslyn source generators. They are part of the CLI migration pipeline.

## Roslyn Generator Layer

Path: `src/S1Interop.Generators/`

`S1Interop.Generators` is the package installed into mod projects through a private `PackageReference`. It consumes attributes such as:

- `S1InteropType`
- `S1InteropMember`
- bridge generation attributes

It emits generated helper source under `S1Interop.Generated`.

The target API shape is type-first. `S1InteropType` is enough to request a backend-neutral facade for a game type and now discovers compatible public fields/properties plus unambiguous public methods from referenced Mono and IL2CPP metadata. `S1InteropMember` is the override path for private members, readable aliases, ambiguous overloads, pinned method bindings, or specific migration-inferred reflection bindings. Avoid designing new features that make developers manually enumerate every common public field/property or simple method after they already opted into the type.

Roslyn generators are additive. They can generate source and diagnostics, but they cannot modify existing user source or rewrite IL. Any call-site transformation must happen through source migration before compilation or through a future IL-weaving layer.

Compiler diagnostics are split into two groups:

- Declaration diagnostics: `S1I001` through `S1I003` validate `S1InteropType` declarations and `S1InteropMember` overrides against referenced Mono/IL2CPP game assemblies.
- IL2CPP source-boundary diagnostics: `S1I004` through `S1I007` catch high-confidence runtime failures that Roslyn can see early, currently Harmony transpilers, managed collection callback signatures, managed byte buffers passed to native/game fill APIs, and object/proxy casts that should route through `S1InteropObjectCast`. `S1I004` through `S1I006` are errors; `S1I007` is a warning until migration has a safe object-cast rewrite.

The source-boundary diagnostics mirror existing migration/analyze risk categories. Keep them narrow: they should fail builds for code that is already known to be unsafe at an IL2CPP boundary, not for ordinary managed helper code that never crosses into game wrappers.

## Backend-Neutral Compatibility Strategy

The long-term goal is to reduce manual Mono/IL2CPP conditionals and make more mods backend-neutral.

Current supported strategy:

1. Analyze runtime-specific project references and source risks.
2. Generate type declarations, member overrides, and helper surfaces from the game types the mod source actually uses.
3. Rewrite safe call sites to generated helpers.
4. Build and verify in sandboxed copies.
5. Report unsupported or ambiguous cases instead of guessing.

The generated SDK surface is usage-driven by default. `sdkgen` and migration-time facade generation inspect source plus local reference metadata, then emit declarations and type-scoped facades for the types a mod touches. Discovery includes ordinary code references, source aliases, namespace-scoped type usage, and string-held game type names used by reflection-heavy mods. String-held names produce registry declarations, not broad global aliases. Member-surface generation from type metadata includes compatible public fields/properties and unambiguous public methods: once a type is included, those helpers are available through the generated facade without hand-authored `S1InteropMember` declarations. Facade namespaces preserve the original runtime namespace root under `S1Interop`: `ScheduleOne.Vehicles.LandVehicle` becomes `S1Interop.ScheduleOne.Vehicles.LandVehicle`, and a future `FishNet.Runtime.*` surface should become `S1Interop.FishNet.Runtime.*`. Do not emit shortened duplicate namespaces for the same type. Constructor coverage and overloaded method coverage remain next steps because overload selection, backend conversions, and facade naming need stronger rules. Explicit member declarations remain for overrides, unsafe surfaces, and ambiguous bindings.

During migration, narrow type lookup calls such as `typeof(Il2CppScheduleOne...)`, `AccessTools.TypeByName("Il2CppScheduleOne...")`, and `Type.GetType("ScheduleOne...", false)` are rewritten to generated registry properties; arbitrary constants, comments, and unsupported reflection expressions are left alone. Simple command-style object creation and instance calls can also move to generated type facades, for example creating a game console command through `S1Interop.ScheduleOne.Console.SetWeather.Create()` and invoking `Execute` through the generated reflection path. These invocation rewrites are code-segment aware and skip strings, line comments, and block comments. Direct member metadata cache assignments are also supported for simple typed receivers, including IL2CPP-prefixed receiver types normalized back to `ScheduleOne` owner declarations; property accessor chains keep `.GetMethod` or `.SetMethod`, and field/property fallback pairs are left to the higher-level fallback rewriter. Untyped runtime `GetType()` reflection is not reported as a direct member migration risk unless the receiver can be tied to a backend/game type, and MelonLoader-owned reflection owners such as `MelonEnvironment`, `MelonLogger`, and `MelonPreferences` are skipped so generated reports stay focused on actionable Mono/IL2CPP wrapper drift. Object-cast migration handles simple `if (value is T typed)` blocks and simple `return value is T typed ? ... : ...;` expressions through the generated `S1InteropObjectCast` helper; risk detection reads code segments and ignores strings/comments before matching cast patterns, so harmless Unity calls such as `GetComponent<T>()` comments and error messages like `"is not a UniversalRenderPipelineAsset"` are not reported. Byte-buffer risk detection is limited to known packet/native read paths and read/receive/fill-style calls that write into a managed `byte[]`; parser helpers such as `MiniMessageSerializer.GetMessageType(data)` are not treated as IL2CPP buffer-fill risks. Source-boundary compiler diagnostics cover the same high-confidence categories when the generator runs in an IL2CPP build. This is intentionally limited to simple local shapes so broad object construction or reflection flow is not guessed incorrectly. `sdkgen --full-sdk` is the explicit blank-project path: it emits backend-neutral declarations for all discoverable `ScheduleOne` types in the local reference surface, without adding broad global type aliases. When generated facade output contains `S1InteropType` attributes, the applied plan must also install `S1Interop.Generators`; the generated SDK source and Roslyn generator package are one compile-time unit. The generator validates type declarations and member overrides against referenced Mono/IL2CPP game assemblies when those reference surfaces are present, including alias-resolved method parameter signatures, reporting compiler diagnostics instead of deferring obvious typos to runtime. Focused real-mod tests should compile the public CLI-generated SDK source against both Mono and IL2CPP reference surfaces rather than only validating in-memory alias plans. Avoid manual per-game-type wrappers or static catalogs of every Schedule One type; generated SDKs must come from local reference metadata so drift is detectable.

Future possible strategy:

- IL weaving may eventually rewrite compiled call sites to generated backend-specific adapters. Treat that as a separate backend with stronger verification requirements. It should not be folded into the Roslyn generator package as if source generation can mutate existing code.

## Test Harness

Path: `tests/S1Interop.Tests/`

The test harness is a console executable with explicit modes:

- `--quick`: fast analyzer, rewriter, migration-planning, and generator checks.
- `--portable`: CI-safe coverage without private local fixtures.
- `--integration`: local real-mod and game-path coverage.
- `--integration-hoverboard`: focused Hoverboard `sdkgen` coverage that compiles generated SDK source against Mono and IL2CPP references.
- `--integration-backend-neutral`: focused real-mod backend-neutral facade coverage, including public CLI-generated SDK and migration-generated member-access source compiled against Mono and IL2CPP references.
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

Migration moves committed Schedule One install paths into `local.build.props`. Runtime game roots use stable property names:

- `MonoGamePath`
- `Il2CppGamePath`

Project files should keep configuration-local `GamePath` aliases that point at those runtime properties. This keeps Visual Studio, Rider, and command-line overrides predictable for custom configurations such as `MonoStable`, `MonoDevelopment`, `Il2cppStable`, and `Il2cppDevelopment`.

Generated local props also include `S1InteropGeneratorPackageSource` and the corresponding `RestoreAdditionalProjectSources` bridge. That keeps unpublished alpha generator packages restorable from IDE builds without committing machine-local package feeds.

## Packaging

The CLI packages as a local/global .NET tool with package id `S1Interop`.

The Roslyn generator package is `S1Interop.Generators` and packages the built generator DLL under `analyzers/dotnet/cs`.

Source code that emits `S1Interop.Generators` package references should use `S1InteropPackageInfo` instead of hardcoding package id, version, or analyzer asset metadata. Portable tests compare that source-side metadata with the CLI and generator package projects so alpha version bumps fail fast when only one side is updated.

CI verifies:

- restore
- Release build
- portable tests
- CLI package
- generator package
- local tool install
- `s1interop --help` smoke command
