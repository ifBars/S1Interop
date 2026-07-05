# Agent Instructions

These instructions apply to the `S1Interop` repository root. The parent `ScheduleOne` folder is a workspace containing many independent projects; do not treat it as this repository.

## Project Intent

S1Interop is a .NET toolchain for Schedule One mod developers. Its job is to help Mono mods move toward IL2CPP, dual-runtime, and backend-neutral support through project analysis, safe source migrations, generated helper code, and sandboxed verification.

The tool is alpha. Prefer changes that make migration behavior more accurate, explainable, reversible, and testable against real mod copies.

## Repository Boundaries

- Work from this repo root: `S1Interop/`.
- Do not edit sibling mod projects except through temporary copies created for tests or diagnostics.
- Do not commit proprietary game assemblies, generated IL2CPP wrappers, decompiled output, AssetRipper exports, local game paths, demo recordings, or machine-specific notes.
- Keep private/local material in ignored paths such as `.internal/`, `docs/internal/`, `*.local.md`, `local.build.props`, `s1interop-runs/`, or `%TEMP%`.
- Delete temporary verification copies and sandboxes when they are no longer needed.

## Build and Test Commands

Use the repository's .NET solution:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Debug
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --quick
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --portable
```

Before pushing changes that touch migration, build verification, CLI packaging, or generators, also run the CI-equivalent Release path:

```powershell
dotnet build .\S1Interop.sln --no-restore --configuration Release
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj --configuration Release --no-build -- --portable
```

Local integration tests require the broader Schedule One workspace and local game installs. They are not required for every change, but they are expected for real-mod migration behavior:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration
```

Prefer focused integration lanes while iterating:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-hoverboard
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-backend-neutral
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-build-gates
```

Use the full `--integration` lane only when broad release-facing local validation is worth the runtime.

## Architecture Rules

- `S1Interop.Cli` owns command parsing, command dispatch, and user-facing reporting.
- `S1Interop.Core` owns project analysis, migration planning, migration application, rollback, source rewriting, sandbox verification, and migration-time code emission.
- `S1Interop.Generators` is the Roslyn source-generator package consumed by mod projects. Keep it additive: it can emit generated source and diagnostics, but it must not assume it can rewrite existing user code.
- Generator diagnostics that validate game type/member strings must only fail when the relevant Mono/IL2CPP reference surface is available in the current compilation.
- Keep CLI code thin. Put reusable behavior in Core.
- Keep generated project templates and backend-neutral `new` project shape in Core scaffolding, not inside CLI command handlers.
- Keep Core analysis, migration, rewriting, and code-emission boundaries separate. `S1Interop.Core.CodeGeneration` is migration-time source emission for files the CLI writes into target projects; reserve `S1Interop.Generators` for the packaged Roslyn generator/analyzer assembly.
- Prefer specific analyzers/catalogs/rewriters over broad string hacks. Use XML APIs for project files and structured C# logic where practical.
- Keep Roslyn generator entry points thin. `S1InteropTypeRegistryGenerator.cs` should wire incremental providers and source outputs; runtime selection, model contracts, discovery, diagnostics, support helpers, and source rendering belong in focused internal folders.
- Treat backend-neutral type coverage as generated SDK output from source usage and reference metadata. Do not build or maintain a hand-written wrapper catalog for every Schedule One type.
- Design SDK work around type-first facades: `S1InteropType` should eventually expose compatible public members for that type, while `S1InteropMember` is an override path for private members, aliases, ambiguous overloads, and migration-inferred reflection seams.
- Keep `sdkgen --full-sdk` metadata-driven and opt-in. It is the blank-project seeding path, not a committed catalog of game APIs.
- Migration changes must be reversible through manifest and backup behavior.
- Build verification must run against sandbox copies, never directly against user projects.

## Testing Expectations

- Add or update focused tests when adding diagnostics, migration operations, rewrite behavior, generator output, CLI commands, or verifier classification.
- Use `--quick` for fast analyzer/rewriter/generator iteration.
- Use `--portable` for public CI-safe coverage.
- Use focused integration lanes first when validating behavior against real local mod copies or local game assemblies; reserve full `--integration` for broad sweeps.
- Prefer focused CLI-output tests over duplicated in-memory scaffold builds when validating generated SDK behavior against both runtime reference surfaces.
- When adding generated SDK coverage for real mods, prefer compiling `sdkgen --apply` output from a temp copy before adding another hand-authored alias scaffold.
- If a test needs local game paths or sibling repositories, keep it in integration coverage or skip cleanly when dependencies are absent.
- Tests must not mutate real sibling projects. Copy fixtures into temp directories and clean them afterwards.

## Documentation Expectations

- Keep public docs accurate to the current alpha behavior. Do not promise one-command conversion for every mod.
- Public docs should describe repeatable workflows and generic path placeholders.
- Put local investigation notes and unreleased planning in ignored internal files.
- Update `README.md`, `docs/ARCHITECTURE.md`, `docs/CONTRIBUTING.md`, or `docs/TESTING.md` when changing public workflow, repo boundaries, or validation commands.

## Git Workflow

- Keep commits scoped and conventional when possible, for example `feat(S1Interop): ...`, `fix(S1Interop): ...`, `test(S1Interop): ...`, or `docs(S1Interop): ...`.
- Check current status before editing and before committing.
- Fetch before pushing to avoid racing `origin/main`.
- Do not rewrite history unless the user explicitly asks.
