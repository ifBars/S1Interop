# Migrate to dual-runtime

Use this path when an existing mod should build separate Mono and IL2CPP assemblies.

Dual-runtime keeps the familiar two-configuration model. Use it when a mod still needs runtime-specific code or when backend-neutral facades are too large a first step.

This path matches many existing Schedule One mods: one source tree, runtime-specific build configurations, local game paths, and release DLLs for the target Steam branch.

Dual-runtime can be the final shape. S1Interop does not require you to collapse to one DLL later. If you prefer manual runtime differences, keep them and use S1Interop for analysis, linting, build hooks, or a few generated helpers where they save work.

Keep S1API, MAPI, SteamNetworkLib, dedicated-server helpers, and other domain APIs where they own the workflow. Use S1Interop for direct Schedule One seams: runtime-specific `ScheduleOne.*` / `Il2CppScheduleOne.*` references, cached reflection bindings, Harmony method targets, and small field/property reads around patches.

Dual-runtime is also the safer first stop for Harmony transpilers, server/client splits, Steam networking, injected IL2CPP components, or dependencies that already ship separate Mono and IL2CPP builds.

## 1. Analyze the mod

```batch
s1interop analyze .
```

Make sure the Mono project builds before migrating. Outdated mod dependencies should be fixed separately unless they block migration planning itself.

Start from a commit or a separate copy of the mod. Keep your existing configuration names and dependencies in view; the analyzer's report is the source for the names you build later.

## 2. Review the migration plan

```batch
s1interop migrate . --dual-runtime --dry-run
```

A dual-runtime migration may:

- add IL2CPP configurations such as `Debug Il2Cpp` and `Release Il2Cpp`;
- add stable `MonoGamePath` and `Il2CppGamePath` slots;
- update a sibling `.sln` so Visual Studio or Rider can see the new configurations;
- add runtime-specific references;
- add safe conditional source rewrites;
- install the generator package when generated helpers are needed.

## 3. Verify the proposal in a sandbox

Before changing the original, run the migration in a temporary copy:

```powershell
s1interop verify-migration . --dual-runtime --include-source-migrations
```

With both local game installs available, compile the sandbox too (replace the example paths):

```powershell
s1interop verify-migration . --dual-runtime --include-source-migrations --build --mono-game-path "D:\Games\Schedule I_alternate" --il2cpp-game-path "D:\Games\Schedule I_public"
```

Review residual diagnostics and each build result. A missing dependency or an unsupported rewrite needs attention before applying. This verifies a proposed migration in a copy; it does not playtest the mod or deploy anything.

## 4. Apply the migration

```batch
s1interop migrate . --dual-runtime --apply
```

After applying, open or reload the solution. If your IDE still only shows old configurations, close and reopen the `.sln` after checking that it was updated.

Review the source/project diff and save the manifest path printed by the command. Confirm changes are limited to the intended project and runtime seams.

## 5. Configure and build the resulting project

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

Build the actual migrated project using its configuration names from `s1interop analyze .`. For example, if the new configurations are `Debug Mono` and `Debug Il2Cpp`, build each with `dotnet build -c "Debug Mono"` and `dotnet build -c "Debug Il2Cpp"`. Keep both results; one successful runtime does not establish the other.

Then [test the feature in each game branch](distributing-mods.md#validate-each-archive). Keep the existing higher-level mod libraries and runtime dependencies in those tests.

## 6. Roll back if needed

Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`.

Preserve any edits you made after migration before restoring earlier files. Use the manifest path printed by your own run:

```batch
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Current limits

Dual-runtime migration can automate project shape, references, solution configuration, safe source patterns, and some generated helper declarations. Runtime-specific behavior, missing third-party dependencies, and unsupported IL2CPP wrapper differences may still need manual work.

Keep the first migration boring: get project references, solution configurations, and path props correct, then build both runtimes. Move direct game access to generated facades after the two runtime builds are honest about what still fails.
