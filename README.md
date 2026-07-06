# S1Interop

S1Interop is an alpha toolchain for Schedule One mod developers who want to move Mono mods toward IL2CPP, dual-runtime builds, or a backend-neutral single-assembly shape without hand-editing every project file and wrapper difference.

The intended role is an interop surface, not a hand-maintained high-level modding API. S1Interop should make direct game-wrapper work safer and easier by generating backend-neutral facades from local reference metadata. Higher-level APIs such as S1API can still provide domain workflows for items, NPCs, shops, saveables, and UI; S1Interop is for the direct game access that remains in a mod, whether that mod is standalone, S1API-based, or a mix of both.

That matters for real Schedule One projects because most mods are not blank SDK samples. A small Harmony patch mod, an S1API content mod, a MAPI building mod, a SteamNetworkLib multiplayer mod, and a dedicated server addon all have different owners for gameplay, networking, assets, and packaging. S1Interop should only take over the direct `ScheduleOne.*` / `Il2CppScheduleOne.*` interop seams that make those projects hard to keep portable.

The main product direction is a generated backend-neutral SDK:

- `s1interop new` starts a backend-neutral mod project.
- `s1interop sdkgen` generates facades from local game reference metadata.
- `s1interop migrate --dual-runtime` helps existing Mono mods move toward two-assembly dual-runtime support.
- `s1interop analyze`, `lint`, and `verify-migration` report unsafe IL2CPP boundary cases before they become runtime failures.

It is not a finished "convert every mod with one command" tool yet. The current alpha already handles real project analysis, SDK facade generation, rollbackable migrations, and sandbox verification, but unsupported or ambiguous cases are reported instead of guessed.

Backend-neutral authoring means one source model and generated `S1Interop.ScheduleOne.*` facades. It does not mean developers should never build against Mono and IL2CPP references. Per-runtime build configurations remain useful validation targets so generator diagnostics can catch missing types, wrapper drift, and IL2CPP boundary failures before runtime.

## Documentation

The full docs site lives under [`docs/docfx`](docs/docfx):

- [Introduction](docs/docfx/articles/introduction.md)
- [Core concepts](docs/docfx/articles/core-concepts.md)
- [Adoption guide](docs/docfx/articles/adoption-guide.md)
- [S1API and S1Interop](docs/docfx/articles/s1api-and-s1interop.md)
- [Getting started](docs/docfx/articles/getting-started.md)
- [Commands](docs/docfx/articles/commands.md)
- [Generated output](docs/docfx/articles/generator-package.md)
- [Backend-neutral SDK](docs/docfx/articles/backend-neutral-sdk.md)
- [Declarations](docs/docfx/articles/backend-neutral-declarations.md)
- [SDK generation](docs/docfx/articles/sdk-generation.md)
- [Migration overview](docs/docfx/articles/migrating-mono-mods.md)
- [Migrate to backend-neutral](docs/docfx/articles/migrate-to-backend-neutral.md)
- [Migrate to dual-runtime](docs/docfx/articles/migrate-to-dual-runtime.md)
- [Diagnostics](docs/docfx/articles/diagnostics.md)
- [Troubleshooting](docs/docfx/articles/troubleshooting.md)
- [FAQ](docs/docfx/articles/faq.md)
- [API reference scope](docs/docfx/articles/api-reference.md)
- [Testing](docs/docfx/contributors/testing.md)

Build the DocFX site locally:

```powershell
dotnet build .\S1Interop.sln -c Release
docfx .\docs\docfx\docfx.json
```

The generated site is written to `docs/docfx/_site`. The GitHub Pages workflow in [`.github/workflows/docs.yml`](.github/workflows/docs.yml) builds and publishes the same site from `main`.

## Quick start

Choose the workflow first:

| You are... | First command after install |
| --- | --- |
| New to Schedule One modding | `s1interop new .\MyFirstMod --apply` |
| Bringing an existing Mono mod | `s1interop analyze .` |
| Exploring local game API coverage | `s1interop sdkgen . --full-sdk --apply` |
| Unsure whether migration is safe | `s1interop verify-migration . --dual-runtime --include-source-migrations` |

See the [adoption guide](docs/docfx/articles/adoption-guide.md) for the beginner path, existing-mod path, and safety model.

Build from source:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln
```

Run the CLI from source:

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- analyze .
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- new .\MyBackendNeutralMod --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- init . --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- sdkgen . --full-sdk --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate . --dual-runtime --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration . --dual-runtime
```

Pack and install the local alpha packages:

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
.\.tools\s1interop --help
.\.tools\s1interop --version
```

The CLI tool and Roslyn generator package are separate packages. During local alpha testing, keep both packages in the same local feed and set `S1InteropGeneratorPackageSource` in generated or migrated projects when they need to restore unpublished `S1Interop.Generators` builds.

## Local game paths

Every developer has different Schedule One install paths. Keep those paths out of source control.

If S1Interop creates `local.build.props`, copy or edit the generated file and set:

```xml
<MonoGamePath>...</MonoGamePath>
<Il2CppGamePath>...</Il2CppGamePath>
```

For projects created with `s1interop new`, copy `local.build.props.example` to `local.build.props`, fill in both game paths, and open the generated `.sln` in Visual Studio or Rider.

When using local unpublished packages, also set:

```xml
<S1InteropGeneratorPackageSource>...\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
```

## Repository layout

```text
src/S1Interop.Cli/          CLI commands and reporting
src/S1Interop.Core/         analysis, migration, generation, rewriting, verification
src/S1Interop.Generators/   Roslyn source generator and diagnostics package
tests/S1Interop.Tests/      portable and local integration coverage
docs/docfx/                 public documentation site
docs/                       maintainer notes and project direction
```

## Safety model

S1Interop works on your mod source and project files. It should not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled dumps, prefabs, scenes, textures, or AssetRipper exports.

Applied migrations write a manifest and backups under `s1interop-runs/<run-id>/`, so generated changes can be rolled back:

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

Machine-local files such as `local.build.props`, `Directory.Build.user.props`, `s1interop-runs/`, and `s1interop-cache/` are ignored by default.
