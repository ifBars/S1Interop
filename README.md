# S1Interop

S1Interop helps Schedule I mod developers catch Mono/IL2CPP problems earlier and change projects more safely.

Licensed under [GPL-3.0-only](LICENSE).

Its dependable early value is:

- compile-time diagnostics for known IL2CPP boundary problems;
- read-only project and source analysis;
- explainable migration plans with dry-run defaults;
- rollbackable, narrow source and project transformations;
- disposable sandbox verification and explicit Mono/IL2CPP build targets.

Backend-neutral generated facades are an experimental opt-in. They are not the default scaffold or the primary product promise. Keep a dual-runtime fallback until a mod has sustained in-game validation on both runtime branches.

## Choose a path

### New to modding — build your first mod

Create the recommended dual-runtime starter:

```powershell
s1interop new .\MyFirstMod
s1interop new .\MyFirstMod --apply
Set-Location .\MyFirstMod
```

The first command previews every file. The second creates the project only when the target directory is empty.

Detect and validate local prerequisites:

```powershell
s1interop doctor .
s1interop setup .
s1interop setup . --apply
```

`doctor` is read-only. `setup` is also a dry run unless `--apply` is present. It writes only an ignored `local.build.props`, never installs software, never edits the project, and never overwrites existing local configuration.

Build for your installed runtime:

```powershell
dotnet build .\MyFirstMod.sln -c "Debug Mono"
dotnet build .\MyFirstMod.sln -c "Debug Il2Cpp"
```

The matching DLL is written under:

```text
bin\Mono\Debug Mono\netstandard2.1\MyFirstMod.dll
bin\Il2Cpp\Debug Il2Cpp\net6.0\MyFirstMod.dll
```

Copy the matching DLL to that game install's `Mods` folder. A successful launch logs:

```text
MyFirstMod loaded on Mono.
```

or:

```text
MyFirstMod loaded on Il2Cpp.
```

Continue with [Build your first mod](docs/docfx/articles/first-mod.md) and [Common tasks](docs/docfx/articles/common-tasks.md).

### Existing or advanced mod — analyze safely

Start without changing files:

```powershell
s1interop analyze .
s1interop lint .
s1interop migrate . --dual-runtime --dry-run
s1interop verify-migration . --dual-runtime --include-source-migrations --build
```

Use the smallest feature that solves the current problem:

| Goal | Start with |
| --- | --- |
| Understand configurations, references, and source risks | `s1interop analyze .` |
| Fail CI on supported high-confidence diagnostics | `s1interop lint .` or `build-hook` |
| Preview separate Mono/IL2CPP project support | `s1interop migrate . --dual-runtime --dry-run` |
| Test changes without touching the project | `s1interop verify-migration . --dual-runtime` |
| Add selected generated diagnostics or helpers | `s1interop init . --dry-run` |
| Experiment with backend-neutral facades | Read the warning below, then use `sdkgen` narrowly |

See [Choose an adoption path](docs/docfx/articles/adoption-guide.md), [Use cases](docs/docfx/articles/use-cases.md), and [Commands](docs/docfx/articles/commands.md).

## Install the current alpha

S1Interop is distributed as a .NET tool on NuGet.org. You need .NET SDK 8 or newer:

```powershell
dotnet tool install --global S1Interop --version 0.1.0-alpha.1
s1interop --version
s1interop --help
```

Use `dotnet tool update --global S1Interop --version 0.1.0-alpha.1` when that version is already installed. Generated projects restore `S1Interop.Generators` from NuGet.org through their normal `PackageReference`; local game configuration contains only game paths.

Full installation details are in [Install S1Interop](docs/docfx/articles/getting-started.md).

## Common tasks

```powershell
# Read-only reports
s1interop doctor .
s1interop analyze .
s1interop lint .

# Preview or write ignored local configuration
s1interop setup .
s1interop setup . --apply

# Preview safe project changes
s1interop init . --dry-run
s1interop migrate . --dual-runtime --dry-run

# Verify in a disposable copy
s1interop verify-migration . --dual-runtime --include-source-migrations --build
```

`--dry-run` and `--apply` are mutually exclusive. File-changing commands keep dry-run behavior as the default.

Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`:

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Experimental backend-neutral facades

The one-DLL generated-facade path is experimental and fragile.

Use it only when:

- the mod's direct game access is narrow enough for the current generated surface;
- both Mono and IL2CPP reference builds pass;
- the exact shipped assembly is smoke-tested on both runtime branches;
- a dual-runtime build remains available as the safe fallback.

Create an experimental scaffold explicitly:

```powershell
s1interop new .\MyExperiment --backend-neutral --apply
```

For existing projects, prefer narrow usage-driven generation:

```powershell
s1interop init . --dry-run
s1interop sdkgen . --dry-run
```

`sdkgen --full-sdk` is for local exploration, not the default beginner or production workflow. Unsupported, ambiguous, overloaded, generic, collection, cast, and runtime-wrapper shapes can still require manual code or separate runtime builds.

Read [Backend-neutral SDK](docs/docfx/articles/backend-neutral-sdk.md), [SDK generation](docs/docfx/articles/sdk-generation.md), and [Real mod evidence](docs/REAL_MOD_EVIDENCE.md) before adopting it.

## Scope and safety

S1Interop is low-level tooling. It does not replace gameplay libraries:

- use S1API for items, NPCs, shops, saveables, UI, and other domain workflows;
- use MAPI for building and model workflows;
- use SteamNetworkLib for higher-level networking;
- use DedicatedServerMod APIs for server/client addon lifecycles.

S1Interop must not commit or redistribute game assemblies, generated IL2CPP wrappers, decompiled output, AssetRipper exports, prefabs, scenes, textures, or local game paths.

## Architecture

```text
CLI
  -> read-only analysis and diagnosis
  -> migration planning
  -> optional rollbackable apply
  -> optional disposable verification/build

S1Interop.Generators
  -> compile-time diagnostics
  -> selected generated helpers
  -> experimental facades when explicitly declared
```

Repository layout:

```text
src/S1Interop.Cli/          commands and user-facing reporting
src/S1Interop.Core/         analysis, setup, migration, generation, rollback, verification
src/S1Interop.Generators/   Roslyn generator and diagnostics package
tests/S1Interop.Tests/      portable and local integration coverage
docs/docfx/                 public documentation site
```

See [Architecture](docs/docfx/articles/architecture.md), [Testing](docs/TESTING.md), and [Contributing](docs/CONTRIBUTING.md).
