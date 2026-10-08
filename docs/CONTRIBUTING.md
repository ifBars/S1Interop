# Contributing

S1Interop is an experimental compiler for Schedule I mods. Keep changes focused and verify the behavior they claim to support.

## Set up the checkout

Install the .NET 8 SDK. Use Windows for CI-equivalent validation.

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Release --no-restore
dotnet run --project .\tests\S1Interop.Compiler.Tests -c Release --no-build
```

Compiler contracts use authored fixtures and do not need game installations. Real-reference and live tests need matching Mono and IL2CPP installations. Some integration fixtures also need sibling mod checkouts.

## Develop and verify a change

Use `--list` to find compiler cases, then run the relevant filter:

```powershell
dotnet run --project tests/S1Interop.Compiler.Tests -c Release -- --filter Reflection
```

For CLI, migration, and generator fixtures, use `tests/S1Interop.Tests` with `--list-tests` or `--filter <name>`.

Before pushing changes to CLI behavior, packaging, build verification, or generators, run the Release gate:

```powershell
dotnet build .\S1Interop.sln -c Release --no-restore
dotnet run --project .\tests\S1Interop.Tests -c Release --no-build -- --portable
```

Run the full compiler contracts after compiler changes. Packaging changes also need isolated package checks. [Testing](https://github.com/ifBars/S1Interop/blob/main/docs/TESTING.md) defines the available lanes; [Release readiness](https://github.com/ifBars/S1Interop/blob/main/docs/RELEASING.md) defines publication gates.

Use the [checkout sample](https://github.com/ifBars/S1Interop/blob/main/samples/SourceCompiler/README.md) when testing local compiler builds. Keep compile evidence, live runtime checks, and gameplay or multiplayer evidence separate.

## Keep module responsibilities clear

- `S1Interop.Cli` handles commands, input files, and reporting.
- `S1Interop.Compiler` handles metadata mapping, source adaptation, and compiler diagnostics.
- `S1Interop.Core` handles scaffolding, setup, analysis, migration, and verification.
- `S1Interop.Generators` supports the legacy declaration and helper workflow.

Use metadata and reusable transformations rather than a catalog of game-specific wrappers. Preserve identity, aliasing, mutations, and lifetimes across runtime boundaries. Report unsupported operations rather than emitting a translation that silently changes behavior.

Keep migrations reversible. Use structured C# and XML APIs for source and project changes. Keep CLI handlers thin.

## Test real mods safely

Copy sibling mod sources into temporary evaluation projects. Never run their deployment scripts or edit the originals during compiler evaluation.

Record source selection, exclusions, dependencies, and source hashes. Test unchanged source first so game API mismatches remain distinct from compiler failures. Remove temporary copies after validation.

Keep proprietary binaries, local game paths, decompiled output, and investigation logs in ignored locations.

## Update documentation

Keep new-project and existing-mod guidance on the same compiler workflow. Put optional integrations and legacy tools under Advanced.

Update the page that owns a procedure rather than duplicating instructions. Keep public examples repeatable and use generic paths. Check links and build the docs after moving content.

## Submit a change

Use a scoped conventional commit, such as `fix(compiler): preserve native array aliases`.

In the pull request, explain the behavior change and the relevant verification. Identify runtime checks you could not perform. Include documentation updates when commands, outputs, or supported behavior change.

## Publish a release

Publication requires an explicit release request. Follow [Release readiness](https://github.com/ifBars/S1Interop/blob/main/docs/RELEASING.md) for version alignment, validation, tagging, and verification of published packages.

Do not reuse a published NuGet version. Keep release instructions in that guide so the workflow and its documentation have one source of truth.
