---
title: Adopt the compiler in an existing mod
description: Evaluate existing Mono source in a compiler project without running the original project's deployment targets.
uid: s1interop.compiler-adoption
---

# Adopt the compiler in an existing mod

Use this route to evaluate the same ordinary Mono source on both backends. The compiler is the candidate's primary workflow. `migrate --dual-runtime` and `migrate --backend-neutral` are older generator workflows and do not enable it.

There is not yet an automatic conversion of arbitrary project files, dependencies, resources, and deployment targets into compiler projects. Start with a separate compiler project so its source coverage and build behavior are explicit. Preserve your original project.

## Create the compiler project

Follow [candidate installation](getting-started.md), then create a sibling evaluation directory:

```powershell
s1interop new ../MyMod-Compiler --apply
```

Inside that new directory, follow its README to restore the pinned local tool and configure `local.build.props` for matching game installations. Use the same candidate feed used for installation. Keep the generated build imports and tool manifest together.

## Bring over author source and dependencies

Remove the generated starter `Mod.cs` from the evaluation project before adding your mod's sources. Copy the source files selected by the original project's compile items and exclusions into a `Source` directory. Do not copy `bin`, `obj`, generated wrapper trees, or the original project/build scripts. The new SDK project includes ordinary C# files under its directory automatically, including `Source`; keep unrelated C# files outside it.

Preserve the original assembly name in the evaluation project's `<AssemblyName>` when source, patches, or dependencies rely on it. Keep the original Melon metadata and entry point; the generated starter must not remain as a second mod. Inventory embedded resources, linked source, content, and source generators separately so a passing build does not silently omit required behavior.

Add necessary third-party references explicitly. The compiler imports provide game, Unity, MelonLoader, and Harmony references. Do not import the original project's auto-deploy, game-launch, or process-termination targets. Do not combine the compiler imports with the older generator's game-reference integration.

For libraries you build through the compiler, use normal `ProjectReference` entries. For compiler-built IL2CPP libraries distributed as binaries, retain their `.s1interop/authoring` companions alongside them. A prebuilt dependency with a different backend-specific API is not automatically adapted merely because its consuming mod uses the compiler.

Keep source unchanged during the initial comparison. Existing runtime conditionals select `MONO` for both outputs; new compiler-authored code does not need those branches. Framework symbols still follow the selected framework. Record missing or changed game APIs separately from compiler failures.

Review existing `#if IL2CPP` and `#if !MONO` branches: they are not selected in either compiler output. Backend-specific proxy work should be handled by compiler adaptation, but independent features implemented only in those branches will not become part of the Mono authoring path automatically. Account for those features in your source inventory and runtime comparison.

## Build and inspect both outputs

Run these serially in the evaluation directory:

```powershell
dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

The DLLs are under `bin/Release/Mono/netstandard2.1` and `bin/Release/Il2Cpp/net6.0`. The native output also contains `S1Interop.Runtime.dll`. Generated lowering inputs remain under `obj`; do not edit them to fix author code.

Check that all intended source files, resources, and dependencies are represented. Resolve author-binding failures before treating later lowering diagnostics as compatibility defects. A mod targeting an older game API does not establish a compiler regression merely because it fails against the current game's Mono metadata.

## Validate behavior before replacing the original workflow

Follow [Test and distribute a mod](distributing-mods.md) with dedicated test installations. Verify loading, then the mod's actual feature, including saves or multiplayer where applicable. Record evidence separately for each backend.

The existing-mod corpus includes unchanged BiggerLobbies, MoreXP, and SteamNetworkLib sources. Its [evidence report](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md#existing-mod-corpus) separates compilation from live checks and identifies author API mismatches. SteamNetworkLib's local transport-override probe does not establish Steam networking or voice behavior.

Once your evaluation is validated, move the reviewed compiler project structure into your normal development workflow and restore only the packaging steps you intend to keep. Until then, the original project remains the comparison point; no rollback command is needed for an untouched original.
