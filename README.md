# S1Interop

S1Interop is an iterative Schedule One Mono-to-IL2CPP developer toolchain.

Status: early proof-of-concept. The current focus is deterministic migration scaffolding, generated compatibility facades, sandbox verification, and real-mod integration testing. It is not a finished one-command converter for every mod yet.

Current slice:

- Discover real mod `.csproj` files.
- Infer Mono, IL2CPP, and CrossCompat build configurations from structured project metadata.
- Model configuration-specific assembly references and package references, including RefGen.Schedule-I Mono/IL2CPP packages.
- Report build-surface diagnostics that block reliable migration.
- Detect source/project runtime-symbol mismatches such as source using `#if IL2CPP` while the project only defines `MOD_IL2CPP`.
- Detect IL2CPP injected types, required constructors, generated-wrapper bridge usage, and managed helper surfaces that need `HideFromIl2Cpp` or a primitive-safe bridge.
- Report advisory IL2CPP source risks such as Harmony transpilers, direct UnityEvent listeners, and direct delegate combine/remove patterns.
- Detect unresolved property-backed reference paths that collapse to drive-root paths, then generate ignored local build property scaffolding.
- Generate a reversible UnityEvent bridge and rewrite simple direct listener calls through it.
- Generate a reversible source-risk report for migration cases that need deliberate IL2CPP helper or patch rewrites.
- Scaffold opt-in dual-runtime build configurations from Mono-only mod projects.
- Conditionalize unconditional `ScheduleOne.*` source usings into `ScheduleOne.*` / `Il2CppScheduleOne.*` branches.
- Generate a public-safe source facade for detected `ScheduleOne.*` namespaces.
- Produce dry-run migration operations for deterministic build-surface fixes.
- Install a reversible project-local MSBuild validation hook that runs S1Interop before compile.
- Validate migrations in a temporary sandbox before touching the real mod project.
- Optionally compile migrated sandbox projects to catch compiler-only migration failures.
- Validate inference against real local mods in this workspace.
- Run from a mixed workspace root while skipping editor metadata, generated outputs, tests, and cloned tool/runtime projects that are not mod migration targets.

Public-safety boundary:

- The tool may inspect local project files, logs, and user-owned game paths.
- It must not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled dumps, prefabs, scenes, textures, or AssetRipper exports.

Run:

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- analyze ..\AlwaysJackpot\AlwaysJackpot.csproj
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- lint ..\JackpotEveryTime\JackpotEveryTime.csproj
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- sdkgen ..\GunsAlwaysAccurate\GunsAlwaysAccurate.csproj --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- sdkgen <copied-fixture.csproj> --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- build-hook ..\GunsAlwaysAccurate\GunsAlwaysAccurate.csproj --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- build-hook <copied-fixture.csproj> --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate ..\JackpotEveryTime\JackpotEveryTime.csproj --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate ..\DedicatedServerAddons\S1DS-PlayerList\S1DS-PlayerList.csproj --dual-runtime --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration ..\S1FuelMod\S1FuelMod.csproj
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration ..\snl-consumers\OTC-S1-Mod\OverTheCounter\OverTheCounter.csproj --include-source-migrations
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration ..\S1FuelMod\S1FuelMod.csproj --build
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration ..\GunsAlwaysAccurate\GunsAlwaysAccurate.csproj --dual-runtime --build --il2cpp-game-path "<your IL2CPP Schedule I install>" --mono-game-path "<your Mono/alternate Schedule I install>"
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration ..\DedicatedServerAddons --dual-runtime
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate <copied-fixture.csproj> --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate rollback <manifest.json>
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --portable
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --integration
```

The solution should build from a normal clone with `dotnet build S1Interop.sln`. `--portable` runs the public CI-safe synthetic tests. `--integration` runs the local real-mod fixture suite and expects this repository to live inside the broader Schedule One modding workspace used during development. Running the test project without arguments runs portable tests first, then runs integration fixtures only when that workspace is detected.

`sdkgen --apply` writes `S1Interop.Generated/S1Interop.GlobalUsings.g.cs`.
`build-hook --apply` writes portable `S1Interop.Build.targets`, ignored `S1Interop.Build.local.props`, and a `.gitignore` entry for the local props file. The target runs `s1interop lint` before reference resolution/compilation and can be disabled with `/p:S1InteropBuildValidationEnabled=false`.
Simple UnityEvent listener risks can be migrated automatically: `migrate --apply` writes `S1Interop.Generated/S1Interop.UnityEventBridge.g.cs` and rewrites eligible `AddListener`/`RemoveListener` calls through it, including safe multi-line listener openings such as `new Action(() =>`, `delegate()`, and cached `System.Action` variables. Runtime-specific listener code, `UnityAction` listener variables, `ToUnityAction` helper calls, ambiguous delegate variables, static `Delegate.Combine` surfaces, and Harmony transpilers remain manual or are treated as already handled depending on the source shape.
Remaining source risks are advisory analysis findings rather than blocking diagnostics. They appear in `analyze` output and as non-automatic `migrate` plan items so developers get a concrete IL2CPP review checklist without S1Interop pretending to rewrite unsafe patch/delegate logic automatically.
Runtime-guarded source such as `#if MONO` / `#elif IL2CPP` branches and explicit `Il2CppSystem.Delegate` paths are treated as already-handled runtime code rather than unhandled migration risk.
When source risks exist, `migrate --apply` also writes `S1Interop.Generated/S1Interop.SourceRisks.md` with grouped evidence and remediation notes. The file is tracked in the migration manifest and removed by rollback if it was generated by S1Interop.
When reference `HintPath` values collapse to paths such as `\Assembly-CSharp.dll` because local MSBuild properties are unset, `migrate --apply` writes `local.build.props` and `local.build.props.example` with the terminal property names the developer needs to fill. The generated local props file is ignored and reversible. Build verification reports any still-empty values as `LocalBuildPropertiesUnset` / `DependencyNotReady`, separate from migration compile failures.
`verify-migration` copies each discovered project into a temporary sandbox, applies the current migration plan there, re-analyzes, reports residual diagnostics, and deletes the sandbox. By default, it verifies project/build-surface migrations and leaves advisory source-risk migrations out of the plan so historical readiness counts stay stable. Add `--include-source-migrations` when you want the sandbox verifier to also generate source helpers such as the UnityEvent bridge, rewrite eligible listener calls, and write the generated source-risk report without touching the real mod source. Add `--build` to also run MSBuild's `Compile` target for each inferred configuration inside the migrated sandbox while disabling S1Interop's own build hook, common deployment properties, project-reference builds, and post-build events. Build verification is opt-in because real mods often depend on local game/reference paths. Pass `--il2cpp-game-path` and `--mono-game-path` with your own local Schedule I installs when you want S1Interop to hydrate sandbox-only MSBuild properties such as `S1Dir`, `GamePath`, `ManagedDllPath`, `MonoManagedDllPath`, `MelonLoaderNet6Path`, and `MonoMelonLoaderPath`; these are dev-machine inputs and are not written back to the real mod project. Build failures include `ReadinessStatus`, `Attribution`, `FailureKind`, `Summary`, and structured `Issues` fields so missing packages/references are separated from compiler migration failures. Package-feed issues include the package id, version, and declaring project/import path when S1Interop can match them to a configuration-specific `PackageReference`. Use `--build-timeout-seconds <n>` to change the per-configuration timeout from the 120-second default. For one discovered project, JSON output keeps the single-project result shape; for a directory with multiple projects, JSON output returns a workspace summary with per-project results.
When pointed at a mixed workspace root, discovery skips directories such as `.cursor`, `.git`, `artifacts`, `bin`, `obj`, `tests`, `tools`, `S1Interop`, `Cpp2IL`, `Il2CppInterop`, `MelonLoader`, `UnityExplorer`, and `UniverseLib`; pass a specific `.csproj` path when you intentionally want to inspect a skipped project.
`migrate --dual-runtime` can add IL2CPP configurations and rewrite source files, so run it as a dry-run first and apply it to a copied fixture or committed worktree.
`migrate --apply` writes backups and a manifest under `s1interop-runs/<run-id>/`.
Rollback restores only if migrated files still match the manifest hashes.
