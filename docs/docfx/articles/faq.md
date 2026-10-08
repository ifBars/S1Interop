# FAQ

## Which workflow should I start with?

Use the locally built candidate's source compiler: [install it](getting-started.md), then follow [the first-mod guide](first-mod.md) or [existing-mod adoption](compiler-adoption.md). It builds ordinary game source for both runtimes without facade declarations. The earlier published package has different defaults.

Use `--legacy-generator` or `--backend-neutral` only when you deliberately want the older helper or facade project. The `migrate` commands still target those older workflows; they do not enable the compiler.

## Do I need both the CLI and the generator package?

The default compiler project needs the pinned `s1interop` local tool and its matching build imports. Those imports call `s1interop compiler` during builds. It does not need `S1Interop.Generators`.

The generator package is for the older declaration, helper, and diagnostics workflow. You can continue using that package without adopting the compiler.

## Do players install S1Interop?

Players do not install the CLI or generator. Compiler-built IL2CPP mods require the matching `S1Interop.Runtime.dll` in `UserLibs`; Mono outputs do not. Use compatible support generations across installed compiler-built mods. See [distribution](distributing-mods.md).

## Do I need a Mono installation to build an IL2CPP mod?

Yes. The compiler binds author source against Mono metadata, then adapts it to the matching IL2CPP reference surface. The native installation must have generated MelonLoader interop assemblies. The build verifies the game versions and interop generation provenance.

## Do I have to use the generated SDK or runtime conditionals?

No. New compiler projects use ordinary `ScheduleOne.*` source. Existing `MONO` branches are selected for both outputs; the compiler adapts supported differences. Framework-specific conditionals remain framework-specific. No manual facade catalog is required.

## Will S1Interop convert my entire mod automatically?

That is not established. The compiler has known unsupported operations and unverified runtime behavior. Missing dependencies and source written for a different game API must also be distinguished from lowering failures. Read the [support and evidence guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md). Build success alone does not prove gameplay or multiplayer compatibility.

## Does S1Interop redistribute Schedule One game files?

No. Keep game assemblies, reference-only copies, generated IL2CPP proxies, decompiled source, and game assets local. Do not commit machine-specific `local.build.props`. Distribute only your mod and its permitted runtime dependencies.

## Does S1Interop replace S1API or other helper libraries?

It adapts direct game access, rather than defining gameplay systems. Higher-level libraries may remain useful, but their dependency surfaces must be compatible with the chosen compiler build. See [S1API and S1Interop](s1api-and-s1interop.md).

## What does --dry-run do vs --apply?

Project-changing commands preview their plan until `--apply` is supplied; `--dry-run` requests an explicit preview. Applied legacy migrations record backups and rollback manifests. Compiler builds generate intermediate files and output assemblies normally; they are not migration previews and do not deploy the default scaffold.

## Why are my declaration diagnostics silent?

This concerns the older generator workflow. Declaration diagnostics require actual game reference metadata. See [generator diagnostics](diagnostics.md) and [local paths](local-paths.md). Silence is not proof of compatibility.

## When do generated facades update?

This also concerns the older generator workflow. An IDE design-time build or normal compilation runs the generator after declarations change. Restore alone does not generate those symbols. See [generated output](generator-package.md).
