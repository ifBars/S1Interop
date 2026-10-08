---
title: What S1Interop does
description: Compile ordinary Schedule One C# for Mono and IL2CPP using local game metadata.
uid: s1interop.introduction
---

# What S1Interop does

S1Interop's source compiler lets you write ordinary C# against the Mono game's `ScheduleOne.*` API and build separate Mono and IL2CPP mod assemblies. It reads local game metadata and adapts supported operations during the build. You do not select game types from a maintained wrapper catalog or write facade declarations for this workflow.

The compiler is experimental. Use the locally built candidate described in [Install S1Interop](getting-started.md); the earlier published package does not have this default workflow. The candidate provides one command, `s1interop`, with compiler operations under `s1interop compiler`.

## What authoring looks like

The generated starter uses ordinary game and Unity namespaces:

```csharp
using ScheduleOne.NPCs;
using UnityEngine;

// Inside the mod's OnUpdate callback:
if (Input.GetKeyDown(KeyCode.F8))
    MelonLoader.MelonLogger.Msg($"NPCs: {NPCManager.NPCRegistry.Count}");
```

Build the same source for either backend. The compiler selects compatible type references and lowers supported casts, collections, callbacks, and other runtime differences. Mono metadata is required for both builds; IL2CPP builds additionally need the matching native installation's generated interop assemblies.

Follow [Build your first mod](first-mod.md) or [Adopt the compiler in an existing mod](compiler-adoption.md).

## What you ship

Ship a separate mod DLL for each supported runtime. The IL2CPP output also requires its generated `S1Interop.Runtime.dll` in `UserLibs`. Compiler-built mods must use compatible support generations. Players do not install the CLI or generator package.

[Distribution](distributing-mods.md) explains the output paths, dependencies, and runtime checks.

## Current limits

Automatic metadata discovery does not establish unlimited compatibility. Some language constructs, reflection operations, native storage flows, content workflows, and third-party dependencies still need implementation or validation. The compiler cannot restore native code removed from the game build. Compiler and loader behavior still require maintenance when runtime contracts change.

Read the [support and evidence guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) before relying on an operation. Successful compilation, initialization, gameplay, and multiplayer tests prove different things.

S1Interop does not provide its own item, quest, save, or building framework. You may use higher-level libraries alongside it, with compatible dependencies for each runtime. It does not distribute proprietary game files.

## Existing tools remain available

`analyze`, `lint`, reversible migrations, and the generator package remain useful for projects retaining their existing build architecture. The earlier scaffold is selected explicitly with `new --legacy-generator`; generated facades use `new --backend-neutral`. Those are separate workflows, documented under **Legacy generator workflows**, and are not prerequisites for the source compiler.
