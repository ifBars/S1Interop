# S1Interop

S1Interop is an alpha .NET toolchain for Schedule I mod developers working across Mono and IL2CPP. It analyzes projects, reports known interop risks, plans reversible migrations, and can generate selected backend-neutral helpers.

It is low-level tooling. Use S1API, MAPI, networking libraries, and dedicated-server APIs for the workflows they already own. Use S1Interop for direct `ScheduleOne.*` and `Il2CppScheduleOne.*` seams that would otherwise need duplicated runtime code.

Licensed under [GPL-3.0-only](LICENSE).

## Start here

Use the [Start here guide](docs/docfx/articles/adoption-guide.md). It gives new modders a complete first-mod route, lets experienced mod authors begin with read-only analysis, and sends tool authors directly to the relevant reference.

| Your situation | First route |
| --- | --- |
| New to Schedule I modding | [Install, build, and load a small mod](docs/docfx/articles/adoption-guide.md#new-to-schedule-i-modding) |
| Already maintaining a mod | [Inspect it without changing files](docs/docfx/articles/adoption-guide.md#already-maintaining-a-mod) |
| Already know the S1Interop feature you need | [Jump to a task or reference](docs/docfx/articles/adoption-guide.md#already-know-what-you-need) |

The default new-project path produces explicit Mono and IL2CPP builds. Generated backend-neutral facades are experimental. Keep a dual-runtime fallback until the shipped assembly has in-game evidence on both runtime branches.

## Install

Install the current alpha with .NET SDK 8 or newer:

```powershell
dotnet tool install --global S1Interop --version 0.1.0-alpha.1
s1interop --help
```

Use `dotnet tool update --global S1Interop --version 0.1.0-alpha.1` when the tool is already installed. See [Install S1Interop](docs/docfx/articles/getting-started.md) for local tool installation and contributor package builds.

## Safety model

- `analyze`, `lint`, and `doctor` do not change the project.
- File-changing commands show a dry-run plan until you add `--apply`.
- Applied migrations write backups and a rollback manifest under `s1interop-runs/<run-id>/`.
- `verify-migration` works in a temporary copy instead of the source project.
- Game installs stay local. Do not commit assemblies, generated IL2CPP wrappers, decompiled output, game assets, or `local.build.props`.

## Documentation

The documentation starts with one route by experience and outcome: [Start here](docs/docfx/articles/adoption-guide.md).

- [Core concepts](docs/docfx/articles/core-concepts.md)
- [Common tasks](docs/docfx/articles/common-tasks.md)
- [Commands](docs/docfx/articles/commands.md)
- [Troubleshooting](docs/docfx/articles/troubleshooting.md)
- [Contributing](docs/CONTRIBUTING.md)

## Repository layout

```text
src/S1Interop.Cli/          command parsing and user-facing reporting
src/S1Interop.Core/         analysis, migration, generation, rollback, verification
tests/S1Interop.Tests/      portable and local integration coverage
docs/docfx/                 public documentation site
```
