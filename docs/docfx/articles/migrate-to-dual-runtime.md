# Migrate to dual-runtime

Use this path when an existing mod should build separate Mono and IL2CPP assemblies.

Dual-runtime keeps the familiar two-configuration model. Use it when a mod still needs runtime-specific code or when backend-neutral facades are too large a first step.

This path matches many existing Schedule One mods: one source tree, runtime-specific build configurations, local game paths, and release DLLs for the target Steam branch.

Keep S1API, MAPI, SteamNetworkLib, dedicated-server helpers, and other domain APIs where they own the workflow. Use S1Interop for direct Schedule One seams: runtime-specific `ScheduleOne.*` / `Il2CppScheduleOne.*` references, cached reflection bindings, Harmony method targets, and small field/property reads around patches.

Dual-runtime is also the safer first stop for Harmony transpilers, server/client splits, Steam networking, injected IL2CPP components, or dependencies that already ship separate Mono and IL2CPP builds.

## 1. Analyze the mod

```powershell
s1interop analyze .
```

Make sure the Mono project builds before migrating. Outdated mod dependencies should be fixed separately unless they block migration planning itself.

## 2. Review the migration plan

```powershell
s1interop migrate . --dual-runtime --dry-run
```

A dual-runtime migration may:

- add IL2CPP configurations such as `Debug Il2Cpp` and `Release Il2Cpp`;
- add stable `MonoGamePath` and `Il2CppGamePath` slots;
- update a sibling `.sln` so Visual Studio or Rider can see the new configurations;
- add runtime-specific references;
- add safe conditional source rewrites;
- install the generator package when generated helpers are needed.

## 3. Apply the migration

```powershell
s1interop migrate . --dual-runtime --apply
```

After applying, open or reload the solution. If your IDE still only shows old configurations, close and reopen the `.sln` after checking that it was updated.

## 4. Fill in local game paths

Do not commit machine-specific game paths. Put them in `local.build.props`:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>D:\SteamLibrary\steamapps\common\Schedule I_alternate</MonoGamePath>
    <Il2CppGamePath>D:\SteamLibrary\steamapps\common\Schedule I_public</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

Use your own install paths. The paths above are only examples.

## 5. Verify in a sandbox

Before trusting the migrated tree, run verification in a temporary copy:

```powershell
s1interop verify-migration . --dual-runtime --include-source-migrations
```

When game paths are available, add build verification:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "<your Mono Schedule I install>" `
  --il2cpp-game-path "<your IL2CPP Schedule I install>"
```

## 6. Roll back if needed

Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`.

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Current limits

Dual-runtime migration can automate project shape, references, solution configuration, safe source patterns, and some generated helper declarations. Runtime-specific behavior, missing third-party dependencies, and unsupported IL2CPP wrapper differences may still need manual work.

Keep the first migration boring: get project references, solution configurations, and path props correct, then build both runtimes. Move direct game access to generated facades after the two runtime builds are honest about what still fails.
