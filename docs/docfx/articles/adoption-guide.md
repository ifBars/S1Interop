# Adoption guide

Use this page to pick the right first command. S1Interop has a few workflows, and choosing the wrong one early can make a simple setup feel harder than it is.

Most users fall into one of two groups:

- existing Schedule One mod developers who already have a Mono mod and want IL2CPP or backend-neutral support without maintaining two source trees;
- first-time modders who want to start from a project shape that already understands Mono, IL2CPP, local game paths, and generated SDK facades.

S1Interop does not ship game assemblies, generated IL2CPP wrappers, decompiled source, prefabs, scenes, textures, or exported Unity projects. It works from your local game install and writes mod source/project files.

## Where it fits in real Schedule One mods

Most Schedule One projects already fall into a few familiar shapes. S1Interop should fit into those shapes instead of making every mod look like a sample project.

| Project shape | Common today | S1Interop role |
| --- | --- | --- |
| Direct MelonLoader patch mod | A small `MelonMod`, Harmony patches, direct `ScheduleOne.*` or `Il2CppScheduleOne.*` references, and local game paths in `local.build.props`. | Keep the mod shape. Use `analyze`, `sdkgen`, and generated facades to remove duplicated Mono/IL2CPP conditionals around game types and members. |
| Native Mono/IL2CPP configured mod | One source tree with existing `Mono`, `Il2Cpp`, `Debug Il2Cpp`, or branch-specific build configurations and runtime-specific references. | Keep the honest runtime split while moving portable game access toward generated facades. Use dual-runtime verification when the project still needs separate outputs. |
| S1API content mod | Items, NPCs, shops, saveables, or UI built through S1API builders and lifecycle hooks. | Keep S1API for the gameplay workflow. Use S1Interop underneath or beside it when the mod still needs direct game-wrapper access that S1API does not cover. |
| Hybrid S1API/direct mod | S1API handles content registration, but the mod also has Harmony patches, direct game reads, cached reflection bindings, or backend-specific helper code. | Treat each layer separately: leave S1API-owned workflows alone and migrate the direct game seams that cause Mono/IL2CPP friction. |
| MAPI/building mod | Meshes, interiors, GLTF loading, or building helpers that try to avoid game assembly churn. | Use MAPI for content construction. Use S1Interop only for the direct Schedule One calls that remain. |
| SteamNetworkLib or FishNet multiplayer mod | Runtime-specific networking references, host/client authority checks, and sometimes DTO libraries. | Start with analysis and dual-runtime verification. Move small direct game seams to facades before trying to make the whole project backend-neutral. |
| Dedicated server addon | Headless safety, server/client splits, command permissions, and persistence hooks. | Treat server and client surfaces separately. S1Interop can reduce wrapper differences, but it does not replace server authority or lifecycle design. |
| Performance or UI tuning mod | Direct patches against managers, UI screens, camera/player systems, graphics settings, or bGUI/uGUI surfaces. | Use type facades for stable direct game access. Keep Harmony patches thin and still validate on the actual public IL2CPP branch. |

Use the helper API that owns your domain. If S1API, MAPI, SteamNetworkLib, or DedicatedServerMod already gives you the right workflow, keep it. S1Interop is the generated interop layer for the lower-level game-wrapper calls that still leak through. See [S1API and S1Interop](s1api-and-s1interop.md) for the deeper comparison.

## Which path should I use?

| Situation | Start with | Why |
| --- | --- | --- |
| You are new to Schedule One modding and want a blank mod project. | `s1interop new .\MyMod --apply` | Creates a MelonLoader mod scaffold, Mono/IL2CPP validation configurations, ignored local path props, and starter S1Interop declarations. |
| You have an existing Mono mod and want one backend-neutral assembly. | `s1interop analyze .`, then `s1interop init . --apply` and `s1interop sdkgen . --apply` | Keeps your project intact while adding the generator package and usage-driven SDK declarations. |
| You have an existing Mono mod and want separate Mono/IL2CPP builds. | `s1interop analyze .`, then `s1interop migrate . --dual-runtime --dry-run` | Adds runtime-specific project configuration and reports the source patterns that still need review. |
| You are exploring the game API surface. | `s1interop sdkgen . --full-sdk --apply` | Registers broad Schedule One type coverage from your local reference metadata. Add `S1InteropType` declarations for member facades you actually use. |
| You are unsure whether a migration is safe. | `s1interop verify-migration . --dual-runtime --include-source-migrations` | Applies the plan in a temporary copy so your real mod tree is not mutated. |

## First-time modder path

1. Install the .NET SDK 8.0 or later.
2. Build and install the local alpha CLI from [Installation](getting-started.md).
3. Create a mod scaffold:

```powershell
s1interop new .\MyFirstScheduleOneMod --apply
```

4. Copy `local.build.props.example` to `local.build.props`.
5. Set `MonoGamePath` to an `alternate` or `alternate-beta` branch install and `Il2CppGamePath` to a public `none` or `beta` branch install.
6. Build both validation targets:

```powershell
dotnet build .\MyFirstScheduleOneMod.sln -c Debug
dotnet build .\MyFirstScheduleOneMod.sln -c "Debug Il2Cpp"
```

7. Add game API coverage as you need it:

```powershell
s1interop sdkgen . --apply
```

For a blank exploratory project, use `--full-sdk` once to seed broad type registration. After that, keep declarations narrow. Add member facades for the types your mod actually touches.

The scaffold is still a normal MelonLoader mod. You get `ModCore.cs`, `[MelonInfo]`, `[MelonGame]`, Harmony references, a `.sln`, and local path props. The difference is that the generated `S1Interop.ScheduleOne.*` surface is available from the start, so your first direct game call does not have to become a pair of `#if MONO` / `#if IL2CPP` branches.

## Existing mod developer path

Start with a read-only report:

```powershell
s1interop analyze .
```

Then choose the smallest next step that matches the project:

- `s1interop init . --apply`: installs the generator package and starter declarations without rewriting gameplay code.
- `s1interop sdkgen . --apply`: generates declarations from the game types your source already references.
- `s1interop migrate . --dual-runtime --dry-run`: previews project/configuration edits for separate Mono and IL2CPP builds.
- `s1interop verify-migration . --dual-runtime --include-source-migrations`: tests the migration in a temporary copy.

When testing unpublished S1Interop changes against a real mod, use a temporary copy of the mod. Pack the current `S1Interop.Generators` package into a temporary local feed and point the copy at that feed. Otherwise the mod may restore an older generator package and reproduce bugs that are already fixed in source.

Existing mods usually have a mix of concerns: MelonLoader lifecycle wiring, Harmony patches, helper APIs, local deployment scripts, and direct game references. Start by moving only the direct game references that cause backend friction. Leave content builders, packaging, logging, and deployment scripts alone unless the analyzer reports a concrete problem.

Do not assume the project has only one identity. A mod can already have native Mono/IL2CPP configs, require S1API for content workflows, and still contain its own direct Schedule One patch code. S1Interop should follow those boundaries instead of flattening the project into one migration story.

## What to avoid at first

- Do not commit `local.build.props`; it contains machine-local game paths.
- Do not copy game assemblies or generated IL2CPP wrappers into the repository.
- Do not start with explicit `S1InteropMember` declarations for every public member. Add `S1InteropType` coverage first and let the generator discover safe public fields, properties, constructors, enum mirrors, and simple methods.
- Do not treat Mono build success as proof of IL2CPP runtime safety. Build both validation targets when local references are available.

## A healthy first pass

The first pass is in good shape when:

- `s1interop analyze .` runs without unexpected project discovery failures;
- `local.build.props` exists locally and is ignored by git;
- the project restores `S1Interop.Generators` from the intended package source;
- Mono and IL2CPP validation builds either pass or produce actionable diagnostics;
- generated code under `S1Interop.ScheduleOne.*` can replace at least one direct `ScheduleOne.*` / `Il2CppScheduleOne.*` conditional branch.

After this, move to [New projects](new-projects.md), [Migrate to backend-neutral](migrate-to-backend-neutral.md), or [Migrate to dual-runtime](migrate-to-dual-runtime.md) for the detailed workflow.
