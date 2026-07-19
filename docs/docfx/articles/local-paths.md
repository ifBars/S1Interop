---
title: Local game paths
description: Point a mod project at local Mono and IL2CPP game installs without committing machine-specific paths.
uid: s1interop.local-paths
---

# Local game paths

A mod project compiles against DLLs from your own Schedule I install. Those folders differ on every machine, so S1Interop keeps them in `local.build.props`, which the scaffold ignores in git.

## Which folder to use

Set each property to the game root: the folder containing `Schedule I.exe`, `Schedule I_Data`, and `MelonLoader`.

| Steam branch | Backend | Property |
| --- | --- | --- |
| `none` (the public default) | IL2CPP | `Il2CppGamePath` |
| `beta` | IL2CPP | `Il2CppGamePath` |
| `alternate` | Mono | `MonoGamePath` |
| `alternate-beta` | Mono | `MonoGamePath` |

Steam normally keeps one branch in its install folder. If you develop against both backends, keep separate copies and give each one a clear folder name.

## Configure a new project

Copy the committed example:

```powershell
Copy-Item .\local.build.props.example .\local.build.props
```

Then edit the copy:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>D:\Games\Schedule I_alternate</MonoGamePath>
    <Il2CppGamePath>D:\Games\Schedule I_public</Il2CppGamePath>
    <S1InteropGeneratorPackageSource>C:\Code\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
    <RestoreAdditionalProjectSources Condition="'$(S1InteropGeneratorPackageSource)'!=''">$(S1InteropGeneratorPackageSource);$(RestoreAdditionalProjectSources)</RestoreAdditionalProjectSources>
  </PropertyGroup>
</Project>
```

The normal backend-neutral build uses `MonoGamePath`. `Il2CppGamePath` is needed only for an IL2CPP reference build or migration verification that checks IL2CPP.

The generator package source is required while S1Interop packages are local and unpublished. It points NuGet at the folder containing `S1Interop.Generators.0.1.0-alpha.1.nupkg`.

## What S1Interop reads below each path

For Mono, the scaffold resolves game and Unity references from:

```text
<MonoGamePath>\Schedule I_Data\Managed
```

For IL2CPP, it resolves generated wrapper references from:

```text
<Il2CppGamePath>\MelonLoader\Il2CppAssemblies
```

It resolves MelonLoader from `MelonLoader\net35` for Mono and `MelonLoader\net6` for IL2CPP. If the IL2CPP assemblies folder is missing, launch that game install with MelonLoader once and check `MelonLoader\Latest.log`.

## Pass paths to sandbox verification

You can provide paths for one verification run without editing a props file:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "D:\Games\Schedule I_alternate" `
  --il2cpp-game-path "D:\Games\Schedule I_public"
```

The verifier uses those paths in its temporary project copy. It does not copy game assemblies into your repository.

## Keep local files local

Do not commit `local.build.props`, game assemblies, generated IL2CPP wrappers, decompiled output, or game assets. If an older mod already uses names such as `GameInstallPath` or `ScheduleOnePath`, you can keep compatibility aliases while migrating; generated S1Interop files use `MonoGamePath` and `Il2CppGamePath`.
