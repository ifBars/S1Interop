# Introduction

S1Interop is for Schedule One mod projects that need to survive the Mono to IL2CPP split without making every developer manually learn every wrapper difference.

The toolchain has three jobs:

- inspect a mod project and point out the runtime assumptions it is making;
- generate a backend-neutral SDK surface from local game reference metadata;
- migrate and verify safe changes without touching the real project until the developer asks for `--apply`.

The alpha is already useful, but it is intentionally conservative. If S1Interop cannot prove a rewrite is safe, it leaves a report instead of guessing.

## What S1Interop is

S1Interop is a CLI plus a Roslyn generator package.

The CLI handles project analysis, SDK source generation, migration planning, rollback manifests, and sandbox verification. The Roslyn package runs inside a mod project and emits compile-time helpers, diagnostics, type registries, and runtime bridge code from the generated declarations.

## What S1Interop is not

S1Interop does not reverse IL2CPP back into Mono. It also does not hide every runtime difference behind broad reflection guesses.

The goal is narrower and more practical: make the common migration path boring, catch known IL2CPP runtime failures at compile time, and give mod authors a generated SDK that feels close to ordinary Schedule One mod code.

## Current product direction

The preferred authoring model is type-first.

Developers should be able to declare or generate a game type once:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

Then work through the generated facade:

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);
string? name = vehicle.VehicleName?.ToString();
```

`S1InteropMember` still exists, but it is an override path for private members, ambiguous overloads, pinned Harmony targets, and migration cases that cannot safely come from the generated type facade yet.

