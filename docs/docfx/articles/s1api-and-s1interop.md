# S1API and S1Interop

Use S1API for gameplay workflows it already owns. Use S1Interop for direct `ScheduleOne.*` / `Il2CppScheduleOne.*` access that you do not want to hand-maintain across Mono and IL2CPP.

That direct access can live in a patch mod, an S1API content mod, or a hybrid project with both.

## What S1API owns in your mod

S1API is a curated gameplay API. Keep using it when it already models what your mod is doing.

Use S1API for:

- items, products, stations, shops, money, and inventory workflows;
- custom NPCs, dealers, customers, schedules, dialogue, relationships, and appearance defaults;
- quests, phone apps, TV apps, UI helpers, law, cartel state, buildings, delivery locations, parking, and vehicles where the S1API wrapper is enough;
- save/load through `Saveable` and `SaveableField`;
- lifecycle events such as `GameLifecycle.OnPreLoad`, `OnLoadComplete`, `OnSaveStart`, and `OnSaveComplete`;
- packaged player installs where `S1APILoader` chooses the matching Mono or IL2CPP framework build at startup.

S1API owns registration timing, save/load semantics, content builders, loader packaging, and IL2CPP delegate quirks.

## What S1Interop owns in a mod

S1Interop is generated interop. It is not an item builder, NPC scheduler, phone app framework, or save system.

Use S1Interop for:

- generated facades for direct `ScheduleOne.*` and `Il2CppScheduleOne.*` types;
- reducing duplicated `#if MONO` / `#if IL2CPP` code in direct patch mods, S1API-specific mods, and mixed projects;
- backend-neutral Harmony patch targets for mods that patch vanilla Schedule One methods directly;
- moving an existing Mono mod toward IL2CPP support, dual-runtime builds, or backend-neutral builds;
- generating typed fields, properties, enum mirrors, constructors, and simple methods from local metadata when safe;
- diagnostics for missing declarations, bad member bindings, and IL2CPP boundary issues;
- migration, rollback manifests, local path setup, and sandbox verification;
- replacing copied or local reflection helpers when the mod only needs a stable generated member binding.

S1Interop should stay boring: make direct game access portable, then get out of the way.

## Why both can exist

S1API makes domain decisions: NPC builder behavior, saveable load order, item ID validation, hidden prefab references, and similar gameplay rules. Generated code should not invent those rules.

S1Interop covers repetitive backend glue: runtime type names, member bindings, direct casts, cached reflection, and Harmony targets that differ between Mono and IL2CPP.

| Situation | Use S1API for | Use S1Interop for |
| --- | --- | --- |
| Adding an item, NPC, quest, phone app, save data, or content workflow | The module that owns the workflow. | Direct game access the module does not expose. |
| Existing Mono and IL2CPP configurations | Gameplay workflows S1API owns. | Shared direct game access that can move behind generated facades while both configurations stay as validation targets. |
| S1API mod with a vanilla Harmony patch | Content registration and lifecycle. | Backend-neutral patch targets and nearby game member reads. |
| Copied reflection helper or cached `AccessTools` binding | Higher-level workflow, when one exists. | Generated bindings for Schedule One types and members. |
| One player-facing dependency with content helpers and loader behavior | Runtime packages and `S1APILoader`. | Not this use case. The generator package is a build-time tool, not a gameplay framework. |
| Backend-neutral direct game SDK for types S1API does not wrap | Not this use case unless S1API adds that domain. | Type declarations and generated facades from local metadata. |

## How to choose

If S1API supports the content workflow, keep that code in S1API. If the same mod needs a direct Schedule One type that S1API does not expose, use S1Interop for that gap instead of adding hand-written Mono/IL2CPP branches.

For an existing mod, start with `s1interop analyze .`. Keep your MelonLoader entry point, Harmony patches, logging, config, deployment scripts, and any S1API/MAPI/SteamNetworkLib dependencies. The first useful migration is usually one direct game access point, not the whole project.

For native Mono/IL2CPP mods, start with code that reads or invokes the same game member on both backends. For S1API-specific mods, start outside the S1API workflow: a Harmony target lookup, cached `FieldInfo`, local `ReflectionUtils.TryGetFieldOrProperty(...)`, or direct game-wrapper cast. For hybrid mods, move in small pieces and keep both runtime builds as proof.

## Boundary to keep clear

S1Interop works from local reference metadata. Do not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled source, prefabs, scenes, textures, or exported Unity projects.

S1API and S1Interop can both inspect local game references during development. Public artifacts should contain mod/API source, generated declarations, and compiled mod libraries only when those libraries do not include proprietary game files.
