# Introduction

S1Interop is a toolchain for Schedule One mod developers who need to move Mono mods toward IL2CPP, dual-runtime, or backend-neutral single-assembly shapes without hand-editing every wrapper difference.

The alpha is already useful for real project analysis, SDK facade generation, rollbackable migrations, and sandbox verification. It is intentionally conservative: if S1Interop cannot prove a rewrite is safe, it leaves a report instead of guessing.

## Two packages, one workflow

S1Interop ships as two separate NuGet packages. Understanding which one does what is the key to using the toolchain well.

| Package | Id | What it is | When it runs |
| --- | --- | --- | --- |
| CLI tool | `S1Interop` | A .NET global tool that provides the `s1interop` command. It analyzes projects, plans and applies migrations, generates SDK declaration files, and verifies results in sandboxes. | On demand from a terminal. |
| Generator | `S1Interop.Generators` | A Roslyn source generator and analyzer package. It reads the declarations written by the CLI (or by hand) and emits generated source, diagnostics, and runtime helpers during compilation. | During every build and IDE design-time compilation of a mod project that references it. |

You usually use both: the CLI writes declarations and migrates source; the generator turns those declarations into compiled helpers and compile-time diagnostics. A mod project that only wants the generated SDK surface can reference just the generator package and author declarations by hand.

## What the CLI does

The CLI owns project-level work that happens outside the compiler:

- `analyze`: inspect projects, references, configurations, and source risks.
- `new`: scaffold a backend-neutral project.
- `init`: add backend-neutral declarations and generator support to an existing project.
- `sdkgen`: generate or update backend-neutral SDK declarations from source usage or local game references.
- `migrate`: plan and apply dual-runtime or backend-neutral migrations.
- `verify-migration`: run migrations in a disposable sandbox, optionally with builds.
- `lint` and `build-hook`: report and validate without writing source.

The CLI never runs inside the compiler. It writes files (declarations, project edits, source rewrites) and records rollback manifests under `s1interop-runs/<run-id>/`. See [Commands](commands.md) for the full command surface.

## What the generator package does

The generator package owns compile-time work. Once a project references `S1Interop.Generators`, every build of that project:

- reads `S1InteropType`, `S1InteropNamespace`, `S1InteropMember`, and bridge attributes from assembly-level declarations;
- emits the `S1Interop.Generated` runtime registry, type handles, member accessors, and bridge helpers;
- emits `S1Interop.ScheduleOne.*` type facades with `Handle`, `As`, `TryAs`, `Is`, `Create`, and member accessors;
- reports `S1I001`-`S1I003` declaration diagnostics and `S1I004`-`S1I007` IL2CPP boundary diagnostics.

Because the generator runs during compilation, generated symbols are only visible after a build (or after the IDE's design-time build regenerates them). See [Generated output](generator-package.md) for the full generated surface and build timing.

For the vocabulary behind these terms (`S1InteropObject<TTag>`, facade, `Handle`, Tag, registry), see [Core concepts](core-concepts.md).

## What S1Interop is not

S1Interop does not reverse IL2CPP back into Mono, hide every runtime difference behind reflection guesses, or convert every mod with one command. It also never redistributes Schedule One assemblies, IL2CPP wrappers, decompiled source, or game assets.

## Current product direction

The preferred authoring model is type-first. Declare a game type once, then work through a generated facade that mirrors the original namespace under `S1Interop`:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);
string? name = vehicle.VehicleName?.ToString();
```

`S1InteropMember` is the override path for private members, ambiguous overloads, pinned Harmony targets, and reflection seams that the type facade cannot safely infer yet. See [Declarations](backend-neutral-declarations.md) for the full attribute reference.
