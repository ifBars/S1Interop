# S1API and S1Interop

If you already know S1API, the obvious question is why S1Interop exists at all.

Short answer: keep S1API when it owns the modding workflow your mod needs. Use S1Interop for direct `ScheduleOne.*` / `Il2CppScheduleOne.*` access that you do not want to hand-maintain across Mono and IL2CPP. That direct access might live in a standalone patch mod, an S1API content mod, or a hybrid project with both.

They solve different problems.

## What S1API owns in your mod

S1API is a curated modding API. It has hand-written modules, runtime packaging, loader support, docs, examples, and public API decisions. Its job is to give modders stable gameplay workflows.

Use S1API for the workflows it already covers:

- items, products, stations, shops, money, and inventory workflows;
- custom NPCs, dealers, customers, schedules, dialogue, relationships, and appearance defaults;
- quests, phone apps, TV apps, UI helpers, law, cartel state, buildings, delivery locations, parking, and vehicles where the S1API wrapper is enough;
- save/load through `Saveable` and `SaveableField`;
- lifecycle events such as `GameLifecycle.OnPreLoad`, `OnLoadComplete`, `OnSaveStart`, and `OnSaveComplete`;
- packaged player installs where `S1APILoader` chooses the matching Mono or IL2CPP framework build at startup.

S1API is more than wrappers. It encodes real modding decisions: when game systems are ready, how content should be registered, how data should survive save/load, how IL2CPP delegate quirks are hidden, and which game concepts deserve friendly builders.

## What S1Interop owns in a mod

S1Interop is a generated interop surface. It is not trying to design item builders, NPC schedules, phone apps, or save systems.

Use S1Interop for:

- generated facades for direct `ScheduleOne.*` and `Il2CppScheduleOne.*` types;
- reducing duplicated `#if MONO` / `#if IL2CPP` code in direct patch mods, S1API-specific mods, and mixed projects;
- moving an existing Mono mod toward IL2CPP, dual-runtime, or backend-neutral builds;
- generating typed fields, properties, enum mirrors, constructors, and simple methods from local metadata when safe;
- compile-time diagnostics for missing declarations, bad member bindings, and known IL2CPP boundary issues;
- project migration, rollback manifests, local path setup, and sandbox verification;
- replacing copied or local reflection helpers when the mod only needs a stable generated member binding.

S1Interop should be boring infrastructure. The best version of it makes direct game access feel less special, then gets out of the way.

## Why both can exist

S1API has to decide what a good modding API should look like. That takes human judgment. A generated tool cannot know that an NPC builder should precreate schedule actions for FishNet stability, that saveables need load-order semantics, or that item registration should validate IDs and keep hidden prefab references stable.

S1Interop can cover a different kind of work: repetitive backend glue inside a mod project. Native dual-config mods have it when they branch between Mono and IL2CPP references. S1API-specific mods have it when they step outside S1API for a patch or a missing game surface. Hybrid mods have both. Those local seams are the S1Interop target.

That is the overlap: both care about Mono and IL2CPP. The difference is where the abstraction lives.

| Question | Prefer S1API | Prefer S1Interop |
| --- | --- | --- |
| "I want to add an item, NPC, quest, phone app, save data, or content workflow." | Yes. Use the module that owns the workflow. | Only for direct game access the module does not expose. |
| "My mod already has Mono and IL2CPP configurations." | Only for the gameplay workflows S1API owns. | Yes, when shared direct game access can move behind generated facades while both configurations remain validation targets. |
| "My mod requires S1API, but I also patch a vanilla method and read a few game properties." | Keep S1API for the content workflow. | Yes, especially when that patch currently branches between `ScheduleOne.*` and `Il2CppScheduleOne.*`. |
| "My mod copied a helper that caches `FieldInfo`, `PropertyInfo`, or Harmony `AccessTools` bindings." | S1API may still own the higher-level workflow. | Yes, when the binding points at a Schedule One game type and can be generated from local metadata. |
| "I want one player-facing dependency with content helpers and loader behavior." | Yes. S1API has runtime packages and `S1APILoader`. | No. The generator package is a build-time tool, not a gameplay framework. |
| "I want a backend-neutral direct game SDK for types S1API does not wrap." | No, unless S1API adds that domain. | Yes. Declare the type and let the generator emit facades where metadata is safe. |

## How this should feel for modders

New modders should not have to pick a side.

If they are building content that S1API supports, point them to S1API first. It is friendlier and has the domain concepts they need. If they hit a direct game type that S1API does not expose, S1Interop can cover that narrow gap without forcing the whole mod into hand-written backend branches.

Existing mod authors should start with `s1interop analyze .`. They can keep their MelonLoader entry point, Harmony patches, logging, config, deployment scripts, and any S1API/MAPI/SteamNetworkLib dependencies. The first useful migration is usually one direct game seam, not the whole project.

For native Mono/IL2CPP mods, a good first target is a branch that only exists to read or invoke the same game member on both backends. For S1API-specific mods, a good first target is the code outside the S1API workflow: a Harmony target lookup, a cached `FieldInfo`, a local `ReflectionUtils.TryGetFieldOrProperty(...)`, or a direct cast to a game wrapper. For hybrid mods, do both in small pieces and keep using both runtime builds as the proof.

## Boundary to keep clear

S1Interop works from local reference metadata. It should not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled source, prefabs, scenes, textures, or exported Unity projects.

S1API and S1Interop can both inspect local game references during development. Public artifacts should contain mod/API source, generated declarations, and compiled mod libraries only when those libraries do not include proprietary game files.
