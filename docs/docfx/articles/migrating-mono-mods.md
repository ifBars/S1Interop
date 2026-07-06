# Migration overview

Use migration when a project already builds against Mono, already has Mono/IL2CPP configurations, or uses helper APIs while still carrying direct game-wrapper code. S1Interop can move that project toward one of two shapes:

- backend-neutral single assembly: one mod assembly uses generated `S1Interop.*` facades and resolves Mono or IL2CPP at runtime;
- dual-runtime: the project builds separate Mono and IL2CPP assemblies from runtime-specific configurations.

Pick the shape first. The commands and output are different enough that the docs split them into separate pages.

| Goal | Start here |
| --- | --- |
| One assembly that can run on either backend | [Migrate to backend-neutral](migrate-to-backend-neutral.md) |
| Two assemblies/configurations, one for Mono and one for IL2CPP | [Migrate to dual-runtime](migrate-to-dual-runtime.md) |

## Common first step

Start with analysis:

```powershell
s1interop analyze .
```

Analysis tells you which runtime the project currently targets, which game paths or references it can see, and which source patterns are likely to fail on IL2CPP.

For real mods, this is usually more useful than starting with a migration command. A typical project may already have native Mono/IL2CPP configurations, a MelonLoader entry point, Harmony patches, local deployment events, helper libraries, and maybe S1API or MAPI references. `analyze` lets you separate normal mod structure from the direct game calls that actually need interop work.

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

Use sandbox verification before touching a real mod tree when possible. This is most useful for dual-runtime migrations because `verify-migration` can apply the migration plan in a temporary copy:

```powershell
s1interop verify-migration . --dual-runtime --include-source-migrations
```

Build-gated verification can also compile both runtime configurations when local game paths are available:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "<your Mono Schedule I install>" `
  --il2cpp-game-path "<your IL2CPP Schedule I install>"
```

For backend-neutral projects, the usual verification path is a normal build plus the generator diagnostics described in [Diagnostics](diagnostics.md). The generator surface itself is documented in [Generated output](generator-package.md).

## What to migrate first

Do not try to move an entire mature mod in one pass. Start with the direct game-wrapper code that creates build friction:

- `using ScheduleOne.*` paired with `using Il2CppScheduleOne.*` under conditionals;
- casts between `object`, Unity objects, and generated IL2CPP wrappers;
- public fields or properties that are read from both backends;
- cached `FieldInfo`, `PropertyInfo`, or property accessor bindings, including simple `typeof(...).GetField(...)`, `typeof(...).GetProperty(...)`, `AccessTools.Field(typeof(...), "...")`, `AccessTools.Property(typeof(...), "...")`, `AccessTools.PropertyGetter(typeof(...), "...")`, and `AccessTools.PropertySetter(typeof(...), "...")` calls;
- enum names used in Harmony patches or configuration;
- constructor calls where Mono and IL2CPP wrappers differ;
- string-held type names used for Harmony targets or reflection.

Leave higher-level mod code alone at first. Native build configurations, S1API item builders, MAPI model construction, SteamNetworkLib DTOs, bGUI menus, logging, config files, and packaging scripts should only change when they directly depend on a runtime-specific game type.
