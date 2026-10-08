---
title: Local game paths
description: Configure matching game installations without committing machine-specific paths.
uid: s1interop.local-paths
---

# Local game paths

S1Interop reads game references from your installed copies. The project stores their paths in the ignored `local.build.props` file.

## Which folder to use

Use the root containing `Schedule I.exe`, `Schedule I_Data`, and `MelonLoader`.

| Steam branch | Runtime | Property |
| --- | --- | --- |
| Default or `beta` | IL2CPP | `Il2CppGamePath` |
| `alternate` or `alternate-beta` | Mono | `MonoGamePath` |

Keep separate installations at the same game patch version. Switching Steam branches replaces files in its installation folder.

Both compiler outputs require Mono metadata. IL2CPP output additionally requires the matching native installation and its generated interop assemblies.

## Configure a new project

After restoring the project's pinned tool, run:

```powershell
dotnet tool run s1interop -- setup . --mono-game-path 'C:\Games\ScheduleI-Mono' --il2cpp-game-path 'C:\Games\ScheduleI-Il2Cpp' --apply
dotnet tool run s1interop -- doctor .
```

Replace both example paths. Setup creates `local.build.props` only if Git ignores it and the file does not already exist.

## Change an existing path

Edit `local.build.props` directly:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>C:\Games\ScheduleI-Mono</MonoGamePath>
    <Il2CppGamePath>C:\Games\ScheduleI-Il2Cpp</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

Run doctor again after changing paths. Keep package-feed configuration out of this file.

## Check reference folders

| Reference | Location beneath the game root |
| --- | --- |
| Mono game and Unity assemblies | `Schedule I_Data\Managed` |
| Mono MelonLoader | `MelonLoader\net35` |
| IL2CPP game and Unity interop assemblies | `MelonLoader\Il2CppAssemblies` |
| IL2CPP MelonLoader and Il2CppInterop | `MelonLoader\net6` |

If IL2CPP assemblies are missing, launch that installation with MelonLoader and wait for generation to finish. Close the game before rebuilding.

Compiler builds report `S1C902` for missing Mono metadata and `S1C903` for missing native wrappers. They also check game versions and native generation provenance. Follow the first error in [Troubleshooting](troubleshooting.md).

Keep local paths, game assemblies, generated references, decompiled output, and assets out of source control.
