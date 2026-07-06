# Product Direction

S1Interop should become a generated backend-neutral SDK for Schedule One mod development.

The ideal authoring experience is not a hand-written registry of every member a developer might need. A developer should be able to opt into, infer, or bulk-generate a game type once and then work through a generated facade that exposes the type's normal public surface as much as the Mono and IL2CPP reference metadata allows.

The core product promise is:

- New backend-neutral mods should start from an SDK-shaped project, not from raw reflection helpers.
- Migrated mods should move toward the same SDK shape instead of accumulating more conditional code.
- `S1InteropNamespace` is the broad type-registration path, not an instruction to generate every public member for every type.
- `S1InteropType` is a declaration of type coverage, not a requirement to manually list the type's public members.
- `S1InteropMember` is an override and escape hatch for surfaces that cannot be safely discovered yet.
- The generated SDK must come from local reference metadata so drift is visible and no proprietary game artifacts are committed.

## Positioning

S1Interop is the low-level interop layer for direct Schedule One game-wrapper work. It should make IL2CPP and backend-neutral development approachable without becoming a hand-written replacement for every higher-level modding API.

That means:

- S1Interop should hide repetitive Mono/IL2CPP wrapper differences, type lookup, member binding, casts, delegate conversion, and build-validation mechanics.
- S1Interop should not grow into a manually maintained S1API-style catalog of gameplay concepts, item builders, NPC builders, shops, saveables, or UI workflows.
- Higher-level APIs can build on top of S1Interop when they need backend-neutral internals, while still owning their own domain abstractions.
- Generated metadata-backed coverage is preferred over a committed static wrapper catalog because Schedule One and MelonLoader wrapper output can drift.

## Backend-neutral authoring vs validation

Backend-neutral is an authoring model first: mod code should move toward one source surface under `S1Interop.ScheduleOne.*`. The project may still expose Mono and IL2CPP build configurations so developers can validate the same source against both local reference surfaces.

Those configurations should be presented as validation targets, not as a requirement to maintain two conditional codepaths. A new developer should be able to start with the generated facade model, fill in local paths, and build both reference surfaces without needing to understand every wrapper naming rule up front.

## Current alpha bar

The current generated facade is intentionally conservative. `Handle`, `As`, `TryAs`, `Is`, `Create`, named member accessors, and low-level `Get`/`TrySet`/`Invoke` helpers are useful now, but the public authoring goal is still more native-feeling than the current object/reflection-shaped fallback layer.

Near-term SDK quality should prioritize:

- typed property and method facades where Mono and IL2CPP metadata agree, including backend-neutral scalar, string, object, void method shapes, and declared facade handles for game-object members;
- clear diagnostics or generated reports for members skipped because they are overloaded, generic, ambiguous, missing, or incompatible across backends;
- broader constructor and conversion rules for common wrapper differences such as arrays, `Il2CppSystem.Guid`, `Il2CppSystem.Collections.Generic.List<T>`, and Unity object/proxy casts;
- keeping `S1Interop.Generated.S1InteropMemberRegistry` and other registry types as implementation details in docs, examples, and migration rewrites whenever a type facade can express the same operation.

## Target Experience

Today, backend-neutral code can use generated type handles:

```csharp
S1Interop.ScheduleOne.Vehicles.LandVehicle.Handle vehicle =
    S1Interop.ScheduleOne.Vehicles.LandVehicle.As(rawVehicle);

string? name = vehicle.VehicleName;
float? throttle = vehicle.CurrentThrottle;
S1Interop.ScheduleOne.PlayerScripts.Player.Handle driver = vehicle.AssignedDriver;
```

The generated SDK should preserve the original runtime namespace root under `S1Interop`:

```csharp
using S1Interop.ScheduleOne.Vehicles;
using S1Interop.ScheduleOne.PlayerScripts;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);
LandVehicle.Handle created = LandVehicle.CreateHandle();

string? name = vehicle.VehicleName;
float? throttle = vehicle.CurrentThrottle;
Player.Handle driver = vehicle.AssignedDriver;
```

or, when a static facade is the safer shape:

```csharp
using S1Interop.ScheduleOne.Vehicles;
using S1Interop.ScheduleOne.PlayerScripts;

var vehicle = LandVehicle.As(rawVehicle);
LandVehicle.Handle created = LandVehicle.CreateHandle();

string? name = LandVehicle.GetVehicleName(vehicle);
float? throttle = LandVehicle.GetCurrentThrottle(vehicle);
Player.Handle driver = LandVehicle.GetAssignedDriver(vehicle);
```

`S1InteropMemberRegistry` can remain the low-level generated layer, but it should not be the normal mod-authoring API. Do not emit both shortened and root-preserving namespaces for the same game type. Schedule One facades belong under `S1Interop.ScheduleOne.*`; future supported surfaces should preserve their own roots, such as `S1Interop.FishNet.Runtime.*`.

## Type-First SDK Generation

`S1InteropNamespace` should cover broad type registration without forcing developers to emit thousands of per-type attributes:

```csharp
[assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]
```

Namespace imports are type-only by default. Use `IncludeMembers = true` only for narrow namespaces where the extra generated member surface is intentional.

`S1InteropType` should mean "generate the backend-neutral facade for this game type." It should not require developers to manually declare every ordinary public member.

For example, this declaration:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

now starts generating:

- Mono and IL2CPP runtime type resolution.
- A typed backend-neutral handle or wrapper.
- `As`, `TryAs`, and `Is` helpers for object/proxy conversion.
- Accessors for compatible public fields and properties.
- Invokers for unambiguous compatible public methods.

It should continue toward:

- Broader method and constructor coverage where overload and conversion rules are explicit enough.
- Backend-specific conversions for common wrapper differences such as arrays, `Il2CppSystem.Guid`, and IL2CPP collection types.
- Diagnostics for missing, ambiguous, or incompatible members across Mono and IL2CPP.
- Extending the same root-preserving facade rule to additional supported surfaces when needed, such as FishNet, Unity, or other common modding dependencies.
- Treating lower-level registry names as generated implementation details in more migration rewrites and examples.

The generated member surface should come from local reference metadata. Do not commit game assemblies, generated IL2CPP wrappers, decompiled source, or a static hand-maintained catalog of Schedule One APIs.

## Member Declarations Are Overrides

`S1InteropMember` should become the exception path, not the main workflow.

Use explicit member declarations when:

- The member is private, internal, renamed, or otherwise outside the default public SDK surface.
- A better alias is needed for readability.
- An overload needs explicit parameter names or by-ref markers.
- Mono and IL2CPP surfaces disagree and the developer wants to pin a specific binding.
- Migration inferred a reflection pattern that cannot be represented by the automatic type facade yet.

Normal public fields, properties, and unambiguous public methods should come from the generated type facade after a type is included. Overloaded methods and constructors are still moving in that direction, but explicit declarations remain the safer alpha path until overload and conversion rules are strong enough.

## SDK Generation Modes

The SDK should support three entry points:

- `new`: create a backend-neutral project that already references the generator package, local path props, and SDK generation workflow.
- `sdkgen --apply`: infer the narrow SDK a mod needs from source usage, aliases, string-held type names, and local reference metadata.
- `sdkgen --full-sdk --apply`: seed a blank or exploratory project with all discoverable Schedule One type facades from local reference metadata.

All three paths should produce the same style of facade. Starting backend-neutral should not require a developer to first write a Mono-only mod and then migrate it.

## CLI Shape

For new backend-neutral mods:

```powershell
s1interop new .\MyMod --apply
s1interop sdkgen . --full-sdk --apply
```

For existing mods:

```powershell
s1interop analyze .
s1interop sdkgen . --apply
s1interop migrate . --dual-runtime --dry-run
```

`sdkgen --full-sdk` is the blank-project seeding path. It should generate facades for all discoverable Schedule One types from local reference metadata. Usage-driven `sdkgen` should generate only the types and members a project appears to use.

`migrate` should converge existing mods toward the same generated SDK surface. When it cannot safely rewrite a runtime-specific call, it should leave a focused report or explicit override declaration instead of guessing.

## Non-Goals

- Do not hide every runtime difference behind fragile reflection guesses.
- Do not generate broad aliases that make the developer forget whether a value is backend-neutral or native.
- Do not make `S1InteropMemberRegistry` the public-facing API shape.
- Do not require manual `S1InteropMember` declarations for common public type members once the type facade generator can discover them.
- Do not redistribute proprietary game artifacts.
