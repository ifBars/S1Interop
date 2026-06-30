# S1Interop

S1Interop is an early Schedule One modding toolchain for moving C# mods toward Mono and IL2CPP support with less manual project setup.

The current tool is not a finished "convert every mod with one command" product. It is already useful for inspecting mod projects, scaffolding dual-runtime build configurations, generating compatibility helpers, and verifying migrations in a temporary sandbox before touching a real project.

## What it does today

- Finds Schedule One mod `.csproj` files in a project or workspace.
- Infers Mono, IL2CPP, and CrossCompat configurations from project metadata, references, packages, and source defines.
- Reports build-surface issues that commonly block IL2CPP migration.
- Adds opt-in IL2CPP configurations for Mono-only projects when `migrate --dual-runtime` can infer the source Mono configs.
- Updates a sibling `.sln` so new IL2CPP configurations show up in Visual Studio.
- Moves local game paths into ignored `local.build.props` scaffolding.
- Conditionalizes simple `ScheduleOne.*` usings into Mono and IL2CPP branches.
- Generates source helpers for handled cases such as public-safe namespace facades and UnityEvent listener bridges.
- Writes a source-risk report for patterns that still need human review, such as Harmony transpilers or unsafe delegate surfaces.
- Runs migration plans in a sandbox with `verify-migration`, then deletes the sandbox.

## Safety boundaries

S1Interop may inspect local project files and user-provided game install paths. It should not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled dumps, prefabs, scenes, textures, or AssetRipper exports.

Generated machine-local files are ignored by default:

- `local.build.props`
- `s1interop-runs/`
- `s1interop-cache/`

Internal project notes should stay out of public commits. Use ignored paths such as `.internal/`, `docs/internal/`, or `*.internal.md` for local planning, validation notes, and workspace-specific findings.

## Build from source

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln
```

## Run from source

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- analyze .
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- lint .
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate . --dual-runtime --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration . --dual-runtime
```

Use `--apply` only after reviewing the dry-run output. Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`.

Rollback uses that manifest:

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Install as a local tool

Until packages are published, pack and install from a local source:

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
.\.tools\s1interop --help
```

After that, run commands through `.\.tools\s1interop`:

```powershell
.\.tools\s1interop analyze .
.\.tools\s1interop migrate . --dual-runtime --dry-run
.\.tools\s1interop verify-migration . --dual-runtime
```

## Local game paths

Public users will have different Schedule One install paths. Pass paths explicitly when build verification needs them:

```powershell
.\.tools\s1interop verify-migration . --dual-runtime --build --il2cpp-game-path "<your IL2CPP Schedule I install>" --mono-game-path "<your Mono Schedule I install>"
```

When a migration creates `local.build.props`, fill in the generated `MonoGamePath` and `Il2CppGamePath` values for your machine. Keep that file local.

## Commands

```text
s1interop analyze [path=.] [--configuration name] [--format text|json]
s1interop lint [path=.] [--configuration name] [--format text|json]
s1interop sdkgen [path=.] [--dry-run|--apply] [--format text|json]
s1interop build-hook [path=.] [--dry-run|--apply] [--format text|json]
s1interop migrate [path=.] [--dry-run|--apply] [--dual-runtime] [--format text|json]
s1interop verify-migration [path=.] [--dual-runtime] [--include-source-migrations] [--build] [--il2cpp-game-path path] [--mono-game-path path] [--build-timeout-seconds n] [--format text|json]
s1interop migrate rollback <manifest.json> [--format text|json]
```

## Tests

Portable tests run without private local fixtures:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --portable
```

Maintainers can also run the integration suite when the expected local mod workspace is available:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --integration
```

The integration suite is allowed to depend on local open-source mod checkouts and local game paths. Do not encode those paths into committed docs or project files.
