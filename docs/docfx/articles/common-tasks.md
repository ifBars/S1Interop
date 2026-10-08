---
title: Common tasks
description: Diagnose local inputs, validate both runtimes, analyze an existing mod, and preview safe changes.
uid: s1interop.common-tasks
---

# Common tasks

Start here after the [first mod walkthrough](first-mod.md). The supported path keeps Mono and IL2CPP outputs explicit so failures are easy to diagnose.

## Recheck local prerequisites

Run the read-only doctor whenever the game or MelonLoader changes:

```batch
s1interop doctor .
```

It checks the project, ignored local configuration, game executables, managed game references, and MelonLoader references. Package restore uses NuGet.org normally. Doctor does not install software or edit the project.

If no local configuration exists, preview and then create the ignored local file:

```batch
s1interop setup . --mono-game-path "D:\SteamLibrary\steamapps\common\Schedule I" ^
  --il2cpp-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I"
s1interop setup . --mono-game-path "D:\SteamLibrary\steamapps\common\Schedule I" ^
  --il2cpp-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I" --apply
```

`setup` refuses to write unless `local.build.props` is ignored. It never overwrites an existing local file.

If a configured install moves, edit its path in `local.build.props` and rerun `doctor`. The default compiler scaffold needs Mono metadata even for an IL2CPP build. Building IL2CPP also needs a matching IL2CPP installation and its generated MelonLoader interop assemblies.

## Build both reference surfaces

Restore the pinned local tool as described in [installation](getting-started.md), including its candidate feed, before building a fresh checkout. Run the two builds serially; they share project restore state.

```batch
dotnet build -c "Debug Mono"
dotnet build -c "Debug Il2Cpp"
```

Builds do not deploy or launch the game. For the default compiler scaffold, the commands above produce:

```text
bin\Debug Mono\Mono\netstandard2.1\MyFirstMod.dll
bin\Debug Il2Cpp\Il2Cpp\net6.0\MyFirstMod.dll
```

Copy the selected mod DLL into `Mods`; for IL2CPP, also copy its adjacent `S1Interop.Runtime.dll` into `UserLibs`. The starter logs `MyFirstMod loaded.`. Use [Test and distribute a mod](distributing-mods.md) before sharing outputs.

## Write code for both runtimes

Use ordinary `using ScheduleOne...` imports and C# against the Mono game API. The compiler adapts supported casts, collections, and callbacks for IL2CPP. No facade declaration or runtime conditional is required for the starter. See the [compiler support and limitations](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) and [existing-mod adoption](compiler-adoption.md). The [helper-based authoring guide](dual-runtime-code.md) applies to the older generator workflow.

## Analyze an existing mod

Run `analyze` from the mod folder or pass its path:

```batch
s1interop analyze .
```

This reads project files and source without changing them. It reports the build configurations it found, the runtime evidence behind each classification, and source patterns that may fail on IL2CPP.

If the report is too noisy, analyze one configuration:

```batch
s1interop analyze . --configuration Mono
```

Return to [Start here](adoption-guide.md) before applying migration commands.

## Preview every file-changing command

Commands that can edit a project preview by default. Keep preview and apply as separate steps:

```batch
s1interop init .
s1interop init . --apply
```

`--dry-run` and `--apply` are mutually exclusive. Applied migrations write backups and a manifest under `s1interop-runs\<run-id>`. See [Migration overview](migrating-mono-mods.md) before changing an established mod.

## Experiment with one generated facade

> [!WARNING]
> Backend-neutral facades are opt-in, fragile, and not the default compatibility promise. Keep the explicit Mono/IL2CPP project or conditional implementation until your mod has sustained in-game validation on both runtime branches.

When the experiment is appropriate, add `S1InteropType` only for a type your mod uses. The generator can expose members only where both local reference surfaces provide a compatible shape. Start with [Backend-neutral SDK](backend-neutral-sdk.md) and [Declarations](backend-neutral-declarations.md), and review every skipped or ambiguous member.

## Use S1API for gameplay systems

S1Interop is for low-level access to game types, member bindings, patches, diagnostics, migrations, and runtime validation. It does not provide item builders, NPC creation, quests, phone apps, or save data APIs.

Use [S1API and S1Interop](s1api-and-s1interop.md) when you need one of those systems. A mod can use S1API for the gameplay feature and S1Interop for one direct game call that S1API does not cover.

## Read the right page next

- [Diagnostics](diagnostics.md) covers compile-time findings for declarations and IL2CPP boundaries.
- [Migration overview](migrating-mono-mods.md) covers plans, backups, and verification.
- [Generated output](generator-package.md) explains what the opt-in facade generator emits.
- [Troubleshooting](troubleshooting.md) maps common build and generator errors to fixes.
