---
title: Common tasks
description: Add a game type, check IL2CPP references, analyze an existing mod, and choose the right Schedule I library.
uid: s1interop.common-tasks
---

# Common tasks

Start here after the [first mod walkthrough](first-mod.md). Each task adds one idea without changing the basic one-DLL project shape.

## Add one Schedule I game type

A facade is the generated C# type your mod uses instead of choosing between a Mono class and its IL2CPP wrapper. A declaration tells the generator which facade to create.

Open `S1Interop.Generated\S1Interop.BackendNeutral.cs` and add this above the namespace:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera")]
```

Build once. The generator creates `S1Interop.ScheduleOne.PlayerScripts.PlayerCamera` inside your mod assembly.

You can now resolve the runtime type without writing separate Mono and IL2CPP names:

```csharp
using S1Interop.ScheduleOne.PlayerScripts;

LoggerInstance.Msg($"PlayerCamera resolves to {PlayerCamera.TypeName}.");
```

On Mono, the message uses `ScheduleOne.PlayerScripts.PlayerCamera`. On IL2CPP, it uses `Il2CppScheduleOne.PlayerScripts.PlayerCamera`.

Add `S1InteropType` only for types your mod uses. The generator will also expose compatible public members when both reference surfaces provide a safe shape. Use [Declarations](backend-neutral-declarations.md) when you need the full rules.

## Check the IL2CPP reference surface

The shipping build uses Mono references and detects the backend at runtime. An IL2CPP reference build checks the same source against the generated IL2CPP assemblies on your machine:

```powershell
dotnet build .\MyFirstMod.sln -c Debug `
  -p:S1InteropReferenceRuntime=Il2Cpp `
  -p:S1InteropTargetRuntime=Il2Cpp
```

This requires `Il2CppGamePath` in `local.build.props`. Launch the IL2CPP game with MelonLoader once if `MelonLoader\Il2CppAssemblies` has not been generated.

Treat this as a compile-time check. Keep shipping the normal DLL from `bin\Single`, not the validation output under `bin\Il2Cpp`.

The check can finish successfully while printing `MSB3277` warnings about different .NET assembly versions. Those warnings come from comparing a `netstandard2.1` mod with MelonLoader's .NET 6 IL2CPP reference set. For this check, use the final `Build succeeded` or `Build FAILED` line to decide whether the source compiled.

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

Commands that can edit a project support a dry run. Keep the dry run and apply steps separate:

```powershell
s1interop init . --dry-run
s1interop init . --apply
```

Applied migrations write backups and a manifest under `s1interop-runs\<run-id>`. See [Migration overview](migrating-mono-mods.md) before changing an established mod.

## Use S1API for gameplay systems

S1Interop is for low-level access to game types, member bindings, patches, and runtime differences. It does not provide item builders, NPC creation, quests, phone apps, or save data APIs.

Use [S1API and S1Interop](s1api-and-s1interop.md) when you need one of those systems. A mod can use S1API for the gameplay feature and S1Interop for one direct game call that S1API does not cover.

## Read the right page next

- [Generated output](generator-package.md) explains where facades come from and why IntelliSense can lag behind a declaration change.
- [Backend-neutral SDK](backend-neutral-sdk.md) explains `Handle`, `As`, member access, and fallback helpers.
- [Backend-neutral Harmony patching](harmony-patching.md) is the next step for direct game patches.
- [Troubleshooting](troubleshooting.md) maps common build and generator errors to fixes.
