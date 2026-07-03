# Contributing

S1Interop is alpha software. Keep changes small, verifiable, and honest about current limitations.

## Prerequisites

- .NET 8 SDK.
- Windows for CI-equivalent local validation.
- Optional local Schedule One Mono and IL2CPP installs for integration tests.
- Optional sibling open-source mod checkouts for real-mod migration fixtures.

## First-Time Setup

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Debug
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --quick
```

## Development Loop

Use the fastest test lane that proves the change:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --quick
```

Run portable tests before opening or pushing a general change:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --portable
```

Run the Release CI-equivalent path before pushing changes that affect packaging, CLI behavior, build verification, or generators:

```powershell
dotnet build .\S1Interop.sln --no-restore --configuration Release
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj --configuration Release --no-build -- --portable
```

Run integration tests when changing real-mod migration behavior:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration
```

## Coding Guidelines

- Keep `S1Interop.Cli` focused on command flow and reporting.
- Put reusable behavior in `S1Interop.Core`.
- Keep analysis, migration, rewriting, generation, and verification concerns separate.
- Prefer explicit models and small focused helpers over broad string manipulation.
- Use XML APIs for project file edits when practical.
- Make migrations reversible and idempotent.
- Preserve user-authored project structure where possible.
- Report ambiguous cases instead of guessing.

## Test Guidelines

- Add tests with the smallest useful scope.
- Put fast analyzer/rewriter/generator behavior in portable tests.
- Put MSBuild, package, CLI, and build-gate behavior in portable tests only when it has no private local dependency.
- Put real-mod and local game-path validation in integration tests.
- Never mutate real sibling mod projects. Copy fixtures into temp folders.
- Clean temporary folders after tests and diagnostics.

## Documentation Guidelines

- Update public docs when commands, modes, package shape, migration behavior, or architecture boundaries change.
- Keep public docs repeatable and generic.
- Do not include private local game paths, unpublished investigation notes, or machine-specific state in public docs.
- Use ignored internal docs for local notes.

## Commit Style

Prefer concise conventional commits:

```text
feat(S1Interop): add backend-neutral member invokers
fix(S1Interop): preserve generated member targets in verifier
test(S1Interop): add quick fixture lane
docs(S1Interop): document architecture boundaries
```

## Pull Request Checklist

- The change is scoped to one behavior or documentation concern.
- Public docs are updated if workflow or behavior changed.
- `dotnet build` passes.
- Relevant test lane passes.
- CI-equivalent Release path passes for packaging, CLI, generator, or verifier changes.
- Real-mod integration coverage was run or intentionally skipped with a clear reason.
