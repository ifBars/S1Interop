# Introduction

S1Interop gives Schedule One mods one generated surface for direct game access on Mono and IL2CPP.

Use it for project analysis, SDK facade generation, rollbackable migrations, sandbox verification, and backend-neutral Harmony targets.

Keep S1API, MAPI, SteamNetworkLib, and dedicated server APIs where they own the gameplay workflow. Use S1Interop for direct `ScheduleOne.*` / `Il2CppScheduleOne.*` access, type lookup, member binding, casts, delegates, and validation.

If you are already using S1API, start with [S1API and S1Interop](s1api-and-s1interop.md).

## Two packages, one workflow

S1Interop ships as two packages.

| Package | Id | What it is | When it runs |
| --- | --- | --- | --- |
| CLI tool | `S1Interop` | A .NET global tool that provides the `s1interop` command. It analyzes projects, plans and applies migrations, generates SDK declaration files, and verifies results in sandboxes. | On demand from a terminal. |
| Generator | `S1Interop.Generators` | A Roslyn source generator and analyzer package. It reads the declarations written by the CLI (or by hand) and emits generated source, diagnostics, and runtime helpers during compilation. | During every build and IDE design-time compilation of a mod project that references it. |

Most projects use both. The CLI writes declarations and migration edits; the generator turns those declarations into compiled helpers and diagnostics. If you only need the generated SDK surface, reference `S1Interop.Generators` and write declarations by hand.

## What the CLI does

The CLI handles project-level work outside the compiler:

- `analyze`: inspect projects, references, configurations, and source risks.
- `new`: scaffold a backend-neutral project.
- `init`: add backend-neutral declarations and generator support to an existing project.
- `sdkgen`: generate or update backend-neutral SDK declarations from source usage or local game references.
- `migrate`: plan and apply dual-runtime or backend-neutral migrations.
- `verify-migration`: run migrations in a disposable sandbox, optionally with builds.
- `lint` and `build-hook`: report and validate without writing source.

It writes files and records rollback manifests under `s1interop-runs/<run-id>/`. See [Commands](commands.md) for the full command surface.

## What the generator package does

The generator runs during compilation. Once a project references `S1Interop.Generators`, each build:

- reads `S1InteropType`, `S1InteropNamespace`, `S1InteropMember`, and bridge attributes from assembly-level declarations;
- emits the `S1Interop.Generated` runtime registry, type handles, member accessors, and bridge helpers;
- emits `S1Interop.ScheduleOne.*` type facades with `Handle`, `As`, `TryAs`, `Is`, `Create`, and member accessors;
- reports `S1I001`-`S1I003` declaration diagnostics, `S1I004`-`S1I007` IL2CPP boundary diagnostics, and `S1I008` patch-target review warnings.

Generated symbols appear after a build or IDE design-time build. See [Generated output](generator-package.md) for the full surface and build timing.

For the vocabulary behind these terms (`S1InteropObject<TTag>`, facade, `Handle`, Tag, registry), see [Core concepts](core-concepts.md).

## What S1Interop is not

S1Interop does not convert every mod with one command, reverse IL2CPP into Mono, or guess through every runtime difference. It also never redistributes Schedule One assemblies, IL2CPP wrappers, decompiled source, or game assets.

Backend-neutral does not mean "no per-runtime validation." Keep Mono and IL2CPP build configurations as validation targets for the same source.

## Working shape

Declare a game type once, then work through a generated facade that mirrors the original namespace under `S1Interop`:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);
string? name = vehicle.VehicleName?.ToString();
```

`S1InteropMember` is the override path for private members, ambiguous overloads, pinned Harmony targets, and reflection seams that the type facade cannot safely infer yet. See [Declarations](backend-neutral-declarations.md) for the full attribute reference.
