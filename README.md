# S1Interop

S1Interop helps Schedule One mod developers move Mono mods toward IL2CPP and dual-runtime builds without hand-editing every project file, reference path, and obvious source compatibility case.

It is an alpha tool. It can already inspect real mod projects, scaffold dual-runtime build configurations, generate compatibility helpers, and verify migrations in a temporary sandbox. It is not a promise that every Mono mod can become IL2CPP-compatible with one command.

## What works today

- Discover Schedule One mod `.csproj` files from a project folder or workspace.
- Infer Mono, IL2CPP, and CrossCompat configurations from project metadata, references, packages, and source defines.
- Report common IL2CPP migration blockers, including target framework drift, stale publicized assemblies, wrong reference surfaces, missing runtime defines, injected type issues, and source patterns that need review.
- Add IL2CPP configurations for Mono-only projects when `migrate --dual-runtime` can infer the source Mono configuration.
- Update a sibling `.sln` so generated configurations appear in Visual Studio.
- Move machine-specific game paths into ignored `local.build.props` scaffolding.
- Rewrite simple ScheduleOne using directives, generated type facades, UnityEvent listener calls, Harmony overload bindings, and other handled source patterns.
- Generate opt-in Roslyn compile-time helpers for backend-specific type-name/reflection caches.
- Generate a source-risk report for cases that still need deliberate review, such as Harmony transpilers or unsafe delegate surfaces.
- Run the migration in a throwaway sandbox with `verify-migration` before touching the real project.

## Safety model

S1Interop works on your mod source and project files. It should not commit, package, or redistribute Schedule One assemblies, generated IL2CPP wrappers, decompiled dumps, prefabs, scenes, textures, or AssetRipper exports.

Applied migrations write a manifest and backups under `s1interop-runs/<run-id>/`, so you can roll back a generated change set:

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

Machine-local and internal files are ignored by default:

```text
local.build.props
Directory.Build.user.props
s1interop-runs/
s1interop-cache/
.internal/
docs/internal/
*.internal.md
*.local.md
```

Keep local validation notes, private workspace findings, demo recordings, and machine-specific paths in those ignored locations. Public docs should describe repeatable user workflows, not one developer's local setup.

## Build from source

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln
```

## Repository layout

```text
src/S1Interop.Cli/
  CommandLine/   argument parsing and supported command metadata
  Commands/      command execution paths
  Reporting/     text output and help

src/S1Interop.Core/
  Analysis/      project discovery, MSBuild inspection, and source-risk analysis
  Generators/    migration-time generated source and report writers
  Migration/     migration planning, application, rollback, and sandbox verification
  Rewriting/     source rewrite helpers
  Utilities/     shared low-level helpers

src/S1Interop.Generators/
  Roslyn source generators for opt-in compile-time compatibility helpers

tests/S1Interop.Tests/
  Portable and local integration coverage
```

Additional project docs:

- [`AGENTS.md`](AGENTS.md) - repository instructions for coding agents and automation.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) - module boundaries and migration flow.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) - local development and contribution workflow.
- [`TESTING.md`](TESTING.md) - test modes, CI-equivalent commands, and fixture rules.

## Compile-time generators

S1Interop has two generator layers:

- `S1Interop.Core.Generators` is used by the CLI. It writes migration-time files such as SDK facades, bridge helpers, member-target declarations, and source-risk reports.
- `S1Interop.Generators` is the Roslyn analyzer/source-generator package installed into migrated mod projects. It emits compile-time helpers from attributes.

The SDK facade belongs to the CLI layer because it is produced during migration from the current project source. The Roslyn package stays focused on compile-time additive code that a mod project can reference privately.

`S1Interop.Generators` is for cases where a mod wants compile-time generated helpers instead of hand-maintained backend strings and reflection caches.

Example:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
[assembly: S1Interop.S1InteropType("ScheduleOne.NPCs.Behaviour.MoveItemBehaviour", Alias = "MoveItemBehaviour")]
[assembly: S1Interop.S1InteropType("ScheduleOne.Management.TransitRoute", Alias = "TransitRoute")]
[assembly: S1Interop.S1InteropType("ScheduleOne.ItemFramework.ItemInstance", Alias = "ItemInstance")]
[assembly: S1Interop.S1InteropMember("PlayerCamera", "container", Alias = "NoticeContainer")]
[assembly: S1Interop.S1InteropMember("PlayerCamera", "Instance", Alias = "PlayerCameraInstance", IsStatic = true)]
[assembly: S1Interop.S1InteropMember("MoveItemBehaviour", "IsDestinationValid", Alias = "IsDestinationValid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "TransitRoute", "ItemInstance", "string&" })]
```

The generator emits `S1Interop.Generated.S1InteropTypeRegistry.PlayerCameraName` and a cached `PlayerCamera` resolver. In a Mono build the name resolves to `ScheduleOne.PlayerScripts.PlayerCamera`; in an IL2CPP build it resolves to `Il2CppScheduleOne.PlayerScripts.PlayerCamera`.

Member declarations emit helpers such as `S1Interop.Generated.S1InteropMemberRegistry.GetNoticeContainer(...)`, `TrySetNoticeContainer(...)`, and static helpers such as `GetPlayerCameraInstance()`. These helpers intentionally check both properties and fields, which covers a common Schedule One migration case where a value is a field on Mono but a property on IL2CPP.

Method declarations can also include `ParameterTypeNames` for overload-specific binding. Use registered type aliases for game types and `&` for by-ref parameters. The generator emits both an invoker and a `MethodInfo` property, so Harmony patch targets can use the same generated overload binding instead of repeating `AccessTools.Method(...)` parameter arrays.

Generated method and type facades also include typed convenience helpers such as `InvokeIsDestinationValid<bool>(...)` and `InvokePlayerCamera<int>(...)`. These still use the same cached backend-neutral reflection path, but they keep simple mod code from scattering object casts and primitive conversions around call sites.

When `migrate --apply` finds a simple `AccessTools.Method(...)` overload binding that it can parse safely, it can generate these member declarations and rewrite the local method variable to `S1Interop.Generated.S1InteropMemberRegistry.<Alias>Method`. Ambiguous or unsupported reflection shapes stay in the source-risk report instead of being rewritten.

This does not reverse IL2CPP or remove every runtime difference. It gives S1Interop a compile-time surface for backend-specific adapters, with the goal of replacing repeated string-based reflection and manual conditionals over time.

Roslyn source generators are additive: they can emit new C# and diagnostics, but they cannot rewrite existing source calls or modify IL. A single-DLL compatibility path is still possible, but it needs one of these shapes:

- source migration rewrites known call sites to generated backend-routing helpers before build;
- mod code calls generated facades directly;
- a future IL-weaving step rewrites compiled call sites after Roslyn compilation.

The current project deliberately uses the first two paths. IL weaving is a separate future layer because it changes the compiled assembly and needs stronger verification than source generation.

Until `S1Interop.Generators` is published, projects that opt into generator attributes need a local package source:

```powershell
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
dotnet restore .\YourMod.csproj --source .\artifacts\packages --source https://api.nuget.org/v3/index.json
```

## Run from source

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- analyze .
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- new .\MyBackendNeutralMod --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- init . --apply
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- lint .
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- migrate . --dual-runtime --dry-run
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- verify-migration . --dual-runtime
```

Use `new` to create a backend-neutral mod scaffold. Use `init` when you already have a project and want to opt into backend-neutral attributes and generated helpers. Use `sdkgen --apply` to generate facade declarations from the game types your source already uses; when those declarations need `S1InteropType` attributes, `sdkgen --apply` also installs the `S1Interop.Generators` package reference required to compile them. Manual `S1InteropType` and `S1InteropMember` declarations are still available for dynamic or reflection-heavy cases the generator cannot infer yet.

When the current build references the Mono or IL2CPP game assemblies, `S1Interop.Generators` validates declared type/member strings during compilation. Missing type names report `S1I001`; member declarations with an unknown owner alias report `S1I002`; member names that are absent from the referenced owner type report `S1I003`. The checks stay quiet when no game reference surface is available, so package restore or docs-only builds do not fail just because local game paths are not configured.

Use `--apply` only after reviewing the dry-run output.

## Backend-neutral member access

`S1InteropMember` helpers still use reflection under the hood, but generated type handles reduce the chance of passing the wrong object into a member getter, setter, or invoker:

```csharp
S1Interop.Vehicles.LandVehicle.Handle vehicle = S1Interop.Vehicles.LandVehicle.As(rawVehicle);

if (vehicle.HasValue)
{
    float? throttle = S1Interop.Vehicles.LandVehicle.GetCurrentThrottleValue<float>(vehicle);
}
```

Prefer type-scoped facades such as `S1Interop.Vehicles.LandVehicle` over calling `S1InteropMemberRegistry` directly. The registry remains available as the generated low-level layer, and raw `object` overloads remain available for dynamic cases, but the tagged handle path keeps backend-neutral code closer to native Mono/IL2CPP usage and catches wrong receiver types earlier.

The goal is a generated SDK surface, not a hand-maintained wrapper for every Schedule One type. S1Interop infers declarations from source usage and local reference metadata, then emits the facade code needed for the types the mod actually touches.

## Install as a local tool

Until packages are published, pack and install from a local source:

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
.\.tools\s1interop --help
```

Then run:

```powershell
.\.tools\s1interop analyze .
.\.tools\s1interop new .\MyBackendNeutralMod --apply
.\.tools\s1interop init . --apply
.\.tools\s1interop migrate . --dual-runtime --dry-run
.\.tools\s1interop verify-migration . --dual-runtime
```

## Local game paths

Every developer has different Schedule One install paths. Pass paths explicitly when build verification needs them:

```powershell
.\.tools\s1interop verify-migration . --dual-runtime --build --il2cpp-game-path "<your IL2CPP Schedule I install>" --mono-game-path "<your Mono Schedule I install>"
```

If migration creates `local.build.props`, fill in the generated `MonoGamePath` and `Il2CppGamePath` values for your machine. Do not commit that file.

## Commands

```text
s1interop analyze [path=.] [--configuration name] [--format text|json]
s1interop new <path> [--dry-run|--apply] [--format text|json]
s1interop init [path=.] [--dry-run|--apply] [--format text|json]
s1interop lint [path=.] [--configuration name] [--format text|json]
s1interop sdkgen [path=.] [--dry-run|--apply] [--format text|json]
s1interop build-hook [path=.] [--dry-run|--apply] [--format text|json]
s1interop migrate [path=.] [--dry-run|--apply] [--dual-runtime] [--format text|json]
s1interop verify-migration [path=.] [--dual-runtime] [--include-source-migrations] [--build] [--il2cpp-game-path path] [--mono-game-path path] [--build-timeout-seconds n] [--format text|json]
s1interop migrate rollback <manifest.json> [--format text|json]
```

## Tests

Quick tests skip MSBuild/package/build-gate fixtures so analyzer, rewriter, migration-planning, and generator changes can be checked faster:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --quick
```

Portable tests run without private local fixtures:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --portable
```

Maintainers can run integration tests when the expected local mod workspace and game installs are available:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -- --integration
```

Integration tests may use local open-source mod checkouts and local game paths. Keep those assumptions out of committed project files and public docs.
