# S1API and S1Interop

Keep S1API when it owns the modding workflow. Use S1Interop for direct `ScheduleOne.*` / `Il2CppScheduleOne.*` access that you do not want to hand-maintain across Mono and IL2CPP. That access can live in a patch mod, an S1API content mod, or a hybrid project with both.

## What S1API owns in your mod

S1API is a curated gameplay API. Use it when it already models the thing you are building.

Use S1API for:

- items, products, stations, shops, money, and inventory workflows;
- custom NPCs, dealers, customers, schedules, dialogue, relationships, and appearance defaults;
- quests, phone apps, TV apps, UI helpers, law, cartel state, buildings, delivery locations, parking, and vehicles where the S1API wrapper is enough;
- save/load through `Saveable` and `SaveableField`;
- lifecycle events such as `GameLifecycle.OnPreLoad`, `OnLoadComplete`, `OnSaveStart`, and `OnSaveComplete`;
- packaged player installs where `S1APILoader` chooses the matching Mono or IL2CPP framework build at startup.

S1API owns gameplay decisions: registration timing, save/load semantics, content builders, loader packaging, and IL2CPP delegate quirks.

## What S1Interop owns in a mod

S1Interop is generated interop. It is not an item builder, NPC scheduler, phone app framework, or save system.

Use S1Interop for:

- generated facades for direct `ScheduleOne.*` and `Il2CppScheduleOne.*` types;
- reducing duplicated `#if MONO` / `#if IL2CPP` code in direct patch mods, S1API-specific mods, and mixed projects;
- backend-neutral Harmony patch targets for mods that patch vanilla Schedule One methods directly;
- moving an existing Mono mod toward IL2CPP, dual-runtime, or backend-neutral builds;
- generating typed fields, properties, enum mirrors, constructors, and simple methods from local metadata when safe;
- diagnostics for missing declarations, bad member bindings, and IL2CPP boundary issues;
- migration, rollback manifests, local path setup, and sandbox verification;
- replacing copied or local reflection helpers when the mod only needs a stable generated member binding.

S1Interop should stay boring: make direct game access portable, then get out of the way.

## Why both can exist

S1API makes domain decisions: NPC builder behavior, saveable load order, item ID validation, hidden prefab references, and similar gameplay rules. Generated code should not invent those rules.

S1Interop covers repetitive backend glue: runtime type names, member bindings, direct casts, cached reflection, and Harmony targets that differ between Mono and IL2CPP.

| Question | Prefer S1API | Prefer S1Interop |
| --- | --- | --- |
| "I want to add an item, NPC, quest, phone app, save data, or content workflow." | Yes. Use the module that owns the workflow. | Only for direct game access the module does not expose. |
| "My mod already has Mono and IL2CPP configurations." | Only for the gameplay workflows S1API owns. | Yes, when shared direct game access can move behind generated facades while both configurations remain validation targets. |
| "My mod requires S1API, but I also patch a vanilla method and read a few game properties." | Keep S1API for the content workflow. | Yes, especially when that patch currently branches between `ScheduleOne.*` and `Il2CppScheduleOne.*`. |
| "My mod copied a helper that caches `FieldInfo`, `PropertyInfo`, or Harmony `AccessTools` bindings." | S1API owns the higher-level workflow when one exists. | Yes, when the binding points at a Schedule One game type and can be generated from local metadata. |
| "I want one player-facing dependency with content helpers and loader behavior." | Yes. S1API has runtime packages and `S1APILoader`. | No. The generator package is a build-time tool, not a gameplay framework. |
| "I want a backend-neutral direct game SDK for types S1API does not wrap." | No, unless S1API adds that domain. | Yes. Declare the type and let the generator emit facades where metadata is safe. |

## How to choose

If S1API supports the content workflow, start there. If your mod later needs a direct Schedule One type that S1API does not expose, use S1Interop for that gap instead of adding hand-written Mono/IL2CPP branches.

If you already have a mod, start with `s1interop analyze .`. Keep your MelonLoader entry point, Harmony patches, logging, config, deployment scripts, and any S1API/MAPI/SteamNetworkLib dependencies. The first useful migration is usually one direct game access point, not the whole project.

For native Mono/IL2CPP mods, start with code that reads or invokes the same game member on both backends. For S1API-specific mods, start outside the S1API workflow: a Harmony target lookup, cached `FieldInfo`, local `ReflectionUtils.TryGetFieldOrProperty(...)`, or direct game-wrapper cast. For hybrid mods, move in small pieces and keep both runtime builds as proof.

## Boundary to keep clear

S1Interop works from local reference metadata. Do not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled source, prefabs, scenes, textures, or exported Unity projects.

S1API and S1Interop can both inspect local game references during development. Public artifacts should contain mod/API source, generated declarations, and compiled mod libraries only when those libraries do not include proprietary game files.
