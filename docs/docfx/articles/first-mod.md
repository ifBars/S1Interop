---
title: Build your first mod
description: Create a compiler-enabled mod and build the same game source for Mono and IL2CPP.
uid: s1interop.first-mod
---

# Build your first mod

Create a mod that logs a message at startup and reports the NPC count when you press F8. You will build and test the same source on Mono and IL2CPP.

## Prerequisites

Complete [Install S1Interop](getting-started.md) and keep that PowerShell window open. Prepare matching Mono and IL2CPP Schedule I installations with [MelonLoader](https://github.com/LavaGang/MelonLoader#how-to-use-the-installer). Launch each installation once. Wait for IL2CPP to finish generating `MelonLoader/Il2CppAssemblies`, then close both games.

The `alternate` branches are Mono; the default and `beta` branches are IL2CPP. Match exact game patch versions. The compiler checks version and native generation provenance, but those checks do not prove gameplay compatibility.

## Create and configure

Create the project in your Documents folder. Run these PowerShell commands separately:

```powershell
Set-Location ([Environment]::GetFolderPath('MyDocuments'))
s1interop new .\MyMod
s1interop new .\MyMod --apply
Set-Location .\MyMod
```

The first command previews the files. Apply requires an empty destination.

Restore the project's pinned compiler tool. For an unpublished candidate, use the package directory produced by the installation guide:

```powershell
dotnet tool restore --add-source $candidateFeed
```

Commit `.config/dotnet-tools.json` and the matching `.s1interop` build files. Builds use the local tool rather than a globally installed version.

Configure the installations using the project's restored tool. Replace the example paths with your installations:

```powershell
dotnet tool run s1interop -- setup . --mono-game-path 'C:\Games\ScheduleI-Mono' --il2cpp-game-path 'C:\Games\ScheduleI-Il2Cpp' --apply
dotnet tool run s1interop -- doctor .
```

`setup` writes an ignored `local.build.props` and refuses to overwrite an existing file. See [Local game paths](local-paths.md) when updating an existing configuration. `doctor` checks local reference availability; the build also checks matching game versions and native generation provenance.

## Build both runtimes

```powershell
dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

Run these serially. Their outputs are:

| Runtime | Mod DLL |
| --- | --- |
| Mono | `bin/Release/Mono/netstandard2.1/MyMod.dll` |
| IL2CPP | `bin/Release/Il2Cpp/net6.0/MyMod.dll` |

Builds do not deploy or launch the game. Copy the matching DLL into that installation's `Mods` directory. For IL2CPP, also copy the adjacent `S1Interop.Runtime.dll` into `UserLibs`. Do not ship game references, the compiler tool, or authoring metadata. Compiler-built mods must use compatible runtime support generations.

## Verify and edit

Launch each installation separately and check MelonLoader for `MyMod loaded.`. After loading a save, press F8: the starter logs the NPC count using ordinary game API access from `Mod.cs`. Build success and the load message alone do not prove this gameplay behavior; verify it on both runtimes.

Change the log message in `Mod.cs`, rebuild the selected runtime, replace its deployed DLL with the game closed, and confirm the new message after launch. There is no Unity Editor project or generated wrapper catalog to edit.

Continue with [Everyday development](common-tasks.md). Before sharing your mod, follow [Test and distribute a mod](distributing-mods.md).
