# Legacy migration and rollback

These commands maintain projects using generator helpers or experimental facades. They edit project files and supported source patterns; they do not enable the source compiler. For compiler adoption, use [Bring an existing mod](compiler-adoption.md).

The migration procedures are [dual-runtime configuration migration](migrate-to-dual-runtime.md) and [experimental facade migration](migrate-to-backend-neutral.md). This page covers their shared review, rollback, and verification behavior.

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

```batch
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Verification

Use sandbox verification before touching a real mod tree when possible. `verify-migration` applies the migration plan in a temporary copy:

```batch
s1interop verify-migration . --dual-runtime --include-source-migrations
```

When local game paths are available, build-gated verification can compile both runtime configurations:

```batch
s1interop verify-migration . --dual-runtime --build ^
  --mono-game-path "<your Mono Schedule I install>" ^
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
