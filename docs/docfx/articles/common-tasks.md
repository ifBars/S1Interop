---
title: Common tasks
description: Diagnose local inputs, validate both runtimes, analyze an existing mod, and preview safe changes.
uid: s1interop.common-tasks
---

# Common tasks

Start here after the [first mod walkthrough](first-mod.md). The supported path keeps Mono and IL2CPP outputs explicit so failures are easy to diagnose.

## Recheck local prerequisites

Run the read-only doctor whenever the game or MelonLoader changes:

```powershell
s1interop doctor .
```

It checks the project, ignored local configuration, game executables, managed game references, and MelonLoader references. Package restore uses NuGet.org normally. Doctor does not install software or edit the project.

If a valid input moved, preview and then apply only the ignored local file:

```powershell
s1interop setup . --mono-game-path "D:\SteamLibrary\steamapps\common\Schedule I" `
  --il2cpp-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I"
s1interop setup . --mono-game-path "D:\SteamLibrary\steamapps\common\Schedule I" `
  --il2cpp-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I" --apply
```

`setup` refuses to write unless `local.build.props` is ignored. It never overwrites an existing local file.

## Build both reference surfaces

```powershell
dotnet build .\MyFirstMod.sln -c "Debug Mono"
dotnet build .\MyFirstMod.sln -c "Debug Il2Cpp"
```

The builds produce separate DLLs:

```text
bin\Mono\Debug Mono\netstandard2.1\MyFirstMod.dll
bin\Il2Cpp\Debug Il2Cpp\net6.0\MyFirstMod.dll
```

Deploy the DLL matching the game branch. The starter logs `[MyFirstMod] loaded on Mono.` or `[MyFirstMod] loaded on Il2Cpp.` so the selected runtime is visible.

## Analyze an existing mod

Run `analyze` from the mod folder or pass its path:

```powershell
s1interop analyze .
```

This reads project files and source without changing them. It reports the build configurations it found, the runtime evidence behind each classification, and source patterns that may fail on IL2CPP.

If the report is too noisy, analyze one configuration:

```powershell
s1interop analyze . --configuration Mono
```

Use [Choose an adoption path](adoption-guide.md) before applying migration commands.

## Preview every file-changing command

Commands that can edit a project preview by default. Keep preview and apply as separate steps:

```powershell
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
