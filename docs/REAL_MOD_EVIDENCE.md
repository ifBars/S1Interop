# Real Mod Evidence

S1Interop is still alpha. This page tracks what the current test harness is meant to prove against real Schedule One mod projects and where the evidence stops.

The real-mod lanes run against sibling checkouts in the broader local ScheduleOne workspace. They copy projects into temporary folders before applying migrations or build-gate checks, then delete the copies. They should not mutate the original mod repositories.

## Evidence Lanes

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-backend-neutral
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-hoverboard
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-build-gates
```

- `--integration-backend-neutral` focuses on generated SDK/facade behavior from real source and reference metadata.
- `--integration-hoverboard` is a smaller real-mod loop for Hoverboard SDK generation and migration convergence.
- `--integration-build-gates` focuses on sandboxed migration plus Mono/IL2CPP build verification for selected real-mod copies.

Run the full integration lane before release-facing validation:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration
```

## Current Real-Mod Coverage

| Mod or fixture | Current evidence |
| --- | --- |
| S1FuelMod | Injected-type analysis, source migration for generated member-access facades, sandboxed migration without mutating source, mono-only copy conversion, and backend-neutral generated facade compilation against both Mono and IL2CPP reference surfaces when local game paths are available. |
| BarsGraphics | Source alias detection, string-held game type discovery, `sdkgen --apply`, generated facade compilation, and build-gated migration across `MonoDevelopment`, `MonoStable`, `Il2cppDevelopment`, and `Il2cppStable` style configurations. |
| Hoverboard | Sandboxed migration convergence, `sdkgen --apply`, namespace-scoped facade generation, and generated SDK compilation against Mono/IL2CPP reference surfaces in focused integration lanes. |
| bGUI-style dependencies | Mixed Mono/IL2CPP configuration inference and staged dependency path handling for `bGUI.dll`-style references. |
| S1-VoiceChat | Build-gated conversion of a mono-only copy when local dependencies and game paths are available. |
| BotanistFix | Build-gated conversion of a mono-only copy when local dependencies and game paths are available. |
| BigWillyMod | Sandboxed verification for property-based generated-wrapper references. |
| BetterJukebox | Missing runtime define migration and absolute hint-path cleanup. |
| GunsAlwaysAccurate | Baseline clean dual-runtime recognition and SDK namespace detection. |
| s1-employeetweaks | Package-reference runtime evidence and Harmony overload risk reporting. |
| OTC-S1-Mod | Configuration condition evaluation and Harmony transpiler risk reporting. |
| CasinoDirectDeposit | Game-root `Mods` dependency hint-path migration. |
| NPCPack | Absolute sibling DLL hint-path migration. |

## What This Proves

- The analyzer can read real project shapes, imported props/targets, custom configuration names, package references, and common local path patterns.
- `migrate` and `verify-migration` can run against temporary real-mod copies without changing the original project.
- Selected mono-only real-mod copies can be migrated far enough for build-gated verification when their unrelated dependencies are available.
- Backend-neutral SDK generation can be seeded from real source aliases, string-held game type names, and local reference metadata.
- Generated backend-neutral helper source is compiled against both Mono and IL2CPP reference surfaces in focused cases.

## What This Does Not Prove Yet

- It does not prove that every Mono mod can migrate cleanly in one command.
- It does not prove runtime behavior for every migrated mod. Runtime smoke coverage exists for backend-neutral demo mods that emit deterministic probe markers, but most real-mod evidence is compile-time and sandbox-verifier evidence.
- It does not remove the need for human review of Harmony transpilers, arbitrary reflection flows, unsafe delegate surfaces, or mod-specific dependencies.
- It does not redistribute or vendor Schedule One assemblies, IL2CPP wrappers, or local game files.

## Adding New Evidence

For a new real-mod fixture:

1. Copy the mod to `%TEMP%\S1Interop.Tests\<guid>` before applying changes.
2. Pack the current `S1Interop.Generators` package into a temporary local feed if the fixture exercises generator output.
3. Use local `MonoGamePath` and `Il2CppGamePath` properties for build-gated validation.
4. Assert that the original project file, source files, `local.build.props`, and `s1interop-runs` state were not changed.
5. Prefer a focused integration lane when the fixture is expensive enough to slow normal iteration.
