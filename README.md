# S1Interop

S1Interop is an experimental compiler toolchain for Schedule I mods. Write ordinary `ScheduleOne.*` C# against Mono game metadata, then build separate Mono and IL2CPP outputs from the same source. The compiler adapts supported runtime differences automatically, using game metadata rather than a maintained catalog of game wrappers.

The compiler is the primary development direction. It does not yet provide unrestricted compatibility: unsupported cases and runtime limits are listed in the [compiler guide](docs/SOURCE_COMPILER.md). Gameplay libraries such as S1API can still be used where their higher-level APIs help your mod.

Licensed under [GPL-3.0-only](https://github.com/ifBars/S1Interop/blob/main/LICENSE).

This checkout prepares **0.1.0-alpha.2**, an unpublished candidate. The published version remains **0.1.0-alpha.1**. See [release readiness](https://github.com/ifBars/S1Interop/blob/main/docs/RELEASING.md) for the stable-release gates.

## Start here

Use the [compiler setup guide](docs/SOURCE_COMPILER.md#try-the-source-checkout) and [sample project](samples/SourceCompiler). This is currently a source-checkout workflow; the published alpha.1 package does not contain the compiler.

```powershell
dotnet build src/S1Interop.Cli/S1Interop.Cli.csproj -c Release
dotnet run --project src/S1Interop.Cli -c Release --no-build -- compiler --help
```

Configure matching Mono and IL2CPP game installations as described in the guide, then build the sample for each runtime. There is one CLI, `s1interop`; compiler build commands live under `compiler`. `s1interop new <path> --apply` creates a compiler-enabled project with a pinned local tool manifest and matching build files. Follow its generated README or the first-mod guide to restore the candidate tool and build both runtimes.

## Install and create a mod

Use the [candidate installation guide](docs/docfx/articles/getting-started.md#build-and-install-the-candidate-from-source) to build and install alpha.2 with a .NET 8 SDK. Then:

```powershell
s1interop new MyMod --apply
cd MyMod
dotnet tool restore --add-source <candidate-feed>
dotnet tool run s1interop -- setup . --mono-game-path <mono-install> --il2cpp-game-path <il2cpp-install> --apply
dotnet tool run s1interop -- doctor .
dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

Replace the placeholders with your package feed and matching game directories. Follow [Build your first mod](docs/docfx/articles/first-mod.md) for loading and testing, or [Adopt the compiler](docs/docfx/articles/compiler-adoption.md) for an existing mod. Compiler builds do not deploy automatically.

## Earlier package workflows

Published alpha.1 does not include the compiler. Existing users can follow the [legacy generator walkthrough](docs/docfx/articles/legacy-generator-first-mod.md). The candidate preserves that scaffold through `new --legacy-generator`; `new --backend-neutral` retains the experimental facade route. Neither is required for compiler authoring. `S1Interop.Generators` is a separate library package for those workflows, not a second CLI.

## Safety model

- `analyze`, `lint`, and `doctor` do not change the project.
- File-changing commands show a dry-run plan until you add `--apply`.
- Applied migrations write backups and a rollback manifest under `s1interop-runs/<run-id>/`.
- `verify-migration` works in a temporary copy instead of the source project.
- Game installs stay local. Do not commit assemblies, generated IL2CPP wrappers, decompiled output, game assets, or `local.build.props`.

## Documentation

The documentation starts with one route by experience and outcome: [Start here](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/adoption-guide.md).

- [Core concepts](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/core-concepts.md)
- [Common tasks](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/common-tasks.md)
- [Commands](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/commands.md)
- [Troubleshooting](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/troubleshooting.md)
- [Test and distribute a mod](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/distributing-mods.md)
- [Release readiness](https://github.com/ifBars/S1Interop/blob/main/docs/RELEASING.md)
- [Contributing](https://github.com/ifBars/S1Interop/blob/main/docs/CONTRIBUTING.md)

## Repository layout

```text
src/S1Interop.Cli/          command parsing and user-facing reporting
src/S1Interop.Compiler/     metadata-driven source lowering and runtime support
src/S1Interop.Core/         scaffolding, setup, analysis, migration and verification
tests/S1Interop.Compiler.Tests/  compiler contracts without game installations
tests/S1Interop.Tests/      portable and local integration coverage
docs/docfx/                 public documentation site
```
