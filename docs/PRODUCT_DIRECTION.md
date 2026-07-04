# Product Direction

S1Interop should become a generated backend-neutral SDK for Schedule One mod development.

The ideal authoring experience is not a hand-written registry of every member a developer might need. A developer should be able to opt into, infer, or bulk-generate a game type once and then work through a generated facade that exposes the type's normal public surface as much as the Mono and IL2CPP reference metadata allows.

The core product promise is:

- New backend-neutral mods should start from an SDK-shaped project, not from raw reflection helpers.
- Migrated mods should move toward the same SDK shape instead of accumulating more conditional code.
- `S1InteropType` is a declaration of type coverage, not a requirement to manually list the type's public members.
- `S1InteropMember` is an override and escape hatch for surfaces that cannot be safely discovered yet.
- The generated SDK must come from local reference metadata so drift is visible and no proprietary game artifacts are committed.

## Target Experience

Today, backend-neutral code can use generated type handles:

```csharp
S1Interop.Vehicles.LandVehicle.Handle vehicle =
    S1Interop.Vehicles.LandVehicle.As(rawVehicle);

string? name = vehicle.VehicleName?.ToString();
float? throttle = vehicle.GetCurrentThrottleValue<float>();
```

The longer-term product target is closer to native mod code:

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle vehicle = LandVehicle.As(rawVehicle);

string? name = vehicle.VehicleName;
float throttle = vehicle.CurrentThrottle;
```

or, when a static facade is the safer shape:

```csharp
using S1Interop.Vehicles;

var vehicle = LandVehicle.As(rawVehicle);

string? name = LandVehicle.GetVehicleName(vehicle);
float throttle = LandVehicle.GetCurrentThrottle<float>(vehicle);
```

`S1InteropMemberRegistry` can remain the low-level generated layer, but it should not be the normal mod-authoring API.

## Type-First SDK Generation

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

- Constructor helpers and broader method coverage where overload and conversion rules are explicit enough.
- Backend-specific conversions for common wrapper differences such as arrays, `Il2CppSystem.Guid`, and IL2CPP collection types.
- Diagnostics for missing, ambiguous, or incompatible members across Mono and IL2CPP.
- A native-like namespace layout such as `S1Interop.ScheduleOne.Vehicles.LandVehicle`, with lower-level registry names treated as generated implementation details.

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
