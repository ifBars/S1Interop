---
title: What S1Interop does
description: Understand the problem S1Interop solves, when to use it, and what it leaves to other modding libraries.
uid: s1interop.introduction
---

# What S1Interop does

Schedule I has two game backends that matter to modders:

- the public `none` and `beta` Steam branches use IL2CPP;
- the `alternate` and `alternate-beta` branches use Mono.

The same game class has a different C# shape on each backend. A Mono mod might use `ScheduleOne.PlayerScripts.PlayerCamera`; an IL2CPP mod sees an Il2CppInterop wrapper such as `Il2CppScheduleOne.PlayerScripts.PlayerCamera`. Casts, delegates, reflection, and Harmony targets can differ too.

S1Interop puts that low-level difference behind generated code. Your mod can use a facade such as `S1Interop.ScheduleOne.PlayerScripts.PlayerCamera`, while the generated code resolves the real Mono or IL2CPP type when the mod runs.

## When to use it

S1Interop is useful when you want to:

- start a MelonLoader mod that ships one DLL for Mono and IL2CPP;
- inspect an existing mod before adding IL2CPP support;
- keep separate Mono and IL2CPP builds but catch unsafe code earlier;
- generate type and member bindings instead of maintaining two reflection paths;
- resolve a Harmony target on either backend;
- try a migration in a temporary copy before touching the real project.

You can adopt one part. An existing mod can use only `analyze` and `lint`; it does not have to switch to generated facades.

## What it does not do

S1Interop does not create items, NPCs, quests, phone apps, buildings, multiplayer rules, or save systems. Use a higher-level library when one already owns that job:

- S1API for gameplay and content workflows;
- MAPI for buildings, procedural meshes, and models;
- SteamNetworkLib for higher-level multiplayer messaging and synchronization;
- DedicatedServerMod APIs for server and client addon lifecycles.

S1Interop can sit beside those libraries when a mod still needs one direct game call or patch.

It also does not convert every mod automatically. Unsafe or ambiguous changes are reported for review. The tool does not redistribute game assemblies, generated IL2CPP wrappers, decompiled code, or game assets.

## The CLI and the generator

S1Interop has two packages because they run at different times.

| Package | What it does | When it runs |
| --- | --- | --- |
| `S1Interop` | Provides the `s1interop` command. It analyzes projects, creates scaffolds, plans and applies migrations, writes declarations, and verifies temporary copies. | When you run a terminal command. |
| `S1Interop.Generators` | Reads declarations and generates facades, runtime helpers, patch bindings, and compiler diagnostics inside the mod assembly. | While the mod project builds. |

The generator is a build dependency. Players do not install a separate S1Interop runtime DLL for a generated mod.

## A small example

This declaration asks for a facade around one game type:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera")]
```

After a build, mod code can use the generated facade:

```csharp
using S1Interop.ScheduleOne.PlayerScripts;

string runtimeTypeName = PlayerCamera.TypeName;
```

The facade keeps the mod source the same. Its runtime type name changes to match Mono or IL2CPP.

New modders should continue to [Install S1Interop](getting-started.md), then [Build your first mod](first-mod.md). Existing mod authors can go to [Choose an adoption path](adoption-guide.md).
