# Migration overview

Use migration when an existing mod still carries direct game-wrapper code. S1Interop can move that access toward one of two shapes:

- backend-neutral single assembly: one mod assembly uses generated `S1Interop.*` facades and resolves Mono or IL2CPP at runtime;
- dual-runtime: the project builds separate Mono and IL2CPP assemblies from runtime-specific configurations.

Pick the shape first. The commands and output are different enough to keep separate.

| Goal | Start here |
| --- | --- |
| One assembly that can run on either backend | [Migrate to backend-neutral](migrate-to-backend-neutral.md) |
| Two assemblies/configurations, one for Mono and one for IL2CPP | [Migrate to dual-runtime](migrate-to-dual-runtime.md) |

## Common first step

Start with analysis:

```powershell
s1interop analyze .
```

Analysis reports the current runtime target, visible game paths or references, and source patterns likely to fail on IL2CPP.

Most mods already have MelonLoader lifecycle code, Harmony patches, deployment scripts, helper libraries, and maybe S1API or MAPI references. `analyze` separates normal mod structure from direct game calls that need interop work.

## Safety model

Commands that write files have a dry-run mode. Review that plan before applying. Depending on the path you choose, S1Interop may:

- create or repair ignored local path props;
- install the generator package reference;
- generate SDK facade declarations;
- add IL2CPP build configurations;
- update a sibling `.sln`;
- rewrite safe source patterns;
- write a source-risk report for cases that still need review.

## Rollback

Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`.

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Verification

Use sandbox verification before touching a real mod tree when possible. `verify-migration` applies the migration plan in a temporary copy:

```powershell
s1interop verify-migration . --dual-runtime --include-source-migrations
```

When local game paths are available, build-gated verification can compile both runtime configurations:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "<your Mono Schedule I install>" `
  --il2cpp-game-path "<your IL2CPP Schedule I install>"
```

For backend-neutral projects, verify with a normal build and the generator diagnostics described in [Diagnostics](diagnostics.md). The generated surface is documented in [Generated output](generator-package.md).

## What to migrate first

Do not move an entire mature mod in one pass. Start with direct game-wrapper code that creates build friction:

- `using ScheduleOne.*` paired with `using Il2CppScheduleOne.*` under conditionals;
- casts between `object`, Unity objects, and generated IL2CPP wrappers;
- public fields or properties that are read from both backends;
- cached `FieldInfo`, `PropertyInfo`, `MethodInfo`, or property accessor bindings, including simple `typeof(...).GetField(...)`, `typeof(...).GetProperty(...)`, `typeof(...).GetMethod(...)`, `AccessTools.Field(typeof(...), "...")`, `AccessTools.Property(typeof(...), "...")`, `AccessTools.PropertyGetter(typeof(...), "...")`, `AccessTools.PropertySetter(typeof(...), "...")`, and `AccessTools.Method(typeof(...), "...")` calls;
- enum names used in Harmony patches or configuration;
- constructor calls where Mono and IL2CPP wrappers differ;
- string-held type names used for Harmony targets or reflection.

Leave higher-level mod code alone at first. Native build configurations, S1API item builders, MAPI model construction, SteamNetworkLib DTOs, bGUI menus, logging, config files, and packaging scripts should only change when they directly depend on a runtime-specific game type.
