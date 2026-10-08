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

For a compiler project, restore its pinned tool, then configure matching installations:

```powershell
dotnet tool run s1interop -- setup . --mono-game-path 'D:\Games\Schedule I_alternate' --il2cpp-game-path 'D:\Games\Schedule I_public' --apply
dotnet tool run s1interop -- doctor .
```

Setup creates the ignored file and never overwrites an existing one. Alternatively, copy the committed example:

```powershell
Copy-Item .\local.build.props.example .\local.build.props
```

Then edit the copy:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>D:\Games\Schedule I_alternate</MonoGamePath>
    <Il2CppGamePath>D:\Games\Schedule I_public</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

Compiler authoring requires `MonoGamePath`, including when building for IL2CPP. An IL2CPP build also requires `Il2CppGamePath` with matching game metadata and generated wrappers. Set both for the dual-runtime workflow. A path must point at the game root, not its `Managed` or `Il2CppAssemblies` subdirectory. Once the file exists, edit it directly when paths change.

The earlier `new --legacy-generator` scaffold can build either installed runtime independently, and candidate setup accepts either for that workflow. Published alpha.1's automatic setup requires Mono. Its manual configuration also works for an IL2CPP-only install. The experimental backend-neutral shipping build requires Mono references.

Compiler projects use the pinned `S1Interop` local tool and `.s1interop` build imports. `S1Interop.Generators` is only needed by the earlier generator/facade workflow. This machine-local file contains game paths, not package configuration.

## What S1Interop reads below each path

Compiler imports read Mono game and Unity metadata from:

```text
<MonoGamePath>\Schedule I_Data\Managed
```

They exclude the game's copies of managed framework libraries. For IL2CPP output, target metadata comes from:

```text
<Il2CppGamePath>\MelonLoader\Il2CppAssemblies
```

It references MelonLoader from `MelonLoader\net35` for Mono and `MelonLoader\net6` (including Il2CppInterop) for IL2CPP. If the IL2CPP assemblies folder is missing, launch that game install with MelonLoader once and check `MelonLoader\Latest.log`.

Compiler build errors include `S1C902` for missing Mono metadata and `S1C903` for missing native wrappers. Version/provenance verification also rejects mismatched game pairs. Restore the project's local tool and follow the first build diagnostic; do not combine compiler imports with generator game-reference integration.

### Earlier generator diagnostics

The legacy scaffold uses `S1InteropGameReferences` from `S1Interop.Generators` and reports these separate errors:

| Error | Meaning |
| --- | --- |
| `S1I101` | The configuration does not set `S1InteropTargetRuntime`. |
| `S1I102` | No game path is configured for that runtime. Run `s1interop setup . --apply`. |
| `S1I103` | The install has no MelonLoader. |
| `S1I104` | `MonoGamePath` does not point at a Mono (`alternate`) install. |
| `S1I105` | `Il2CppGamePath` has no generated assemblies yet, or is not an IL2CPP install. |

To build against a different copy for one command, pass the property: `dotnet build -c "Debug Mono" -p:MonoGamePath="D:\Games\Schedule I_old"`.

## Pass paths to sandbox verification

You can provide paths for one verification run without editing a props file:

```batch
s1interop verify-migration . --dual-runtime --build ^
  --mono-game-path "D:\Games\Schedule I_alternate" ^
  --il2cpp-game-path "D:\Games\Schedule I_public"
```

The verifier uses those paths in its temporary project copy. It does not copy game assemblies into your repository.

## Keep local files local

Do not commit `local.build.props`, game assemblies, generated IL2CPP wrappers, decompiled output, or game assets. If an older mod already uses names such as `GameInstallPath` or `ScheduleOnePath`, you can keep compatibility aliases while migrating; generated S1Interop files use `MonoGamePath` and `Il2CppGamePath`.
