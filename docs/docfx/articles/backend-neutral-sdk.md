# Backend-neutral SDK

The backend-neutral SDK is the main S1Interop product direction.

For what the generator emits at build time and when those symbols appear, see [Generated output](generator-package.md). For the attribute reference that drives generation, see [Declarations](backend-neutral-declarations.md).

Instead of writing code against `ScheduleOne.*` in Mono and `Il2CppScheduleOne.*` in IL2CPP, a mod can use generated facades under `S1Interop.ScheduleOne.*`.

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);

if (vehicle.HasValue)
{
    string? name = vehicle.VehicleName;
    float? throttle = vehicle.CurrentThrottle;
}
```

Facade namespaces preserve the original runtime namespace root:

```text
ScheduleOne.Vehicles.LandVehicle -> S1Interop.ScheduleOne.Vehicles.LandVehicle
FishNet.Runtime.*                -> S1Interop.FishNet.Runtime.*
```

S1Interop does not emit shortened duplicate namespaces such as `S1Interop.Vehicles.*`. One canonical namespace is easier to learn, search, and document.

## Authoring model and validation targets

Backend-neutral is about the source code a mod author writes. The goal is one source surface with generated facades, not one pile of `#if MONO` / `#if IL2CPP` branches.

Projects can still keep Mono and IL2CPP build configurations. Those configurations validate the same backend-neutral source against both local reference surfaces and give the generator enough context to report missing types, missing members, or IL2CPP-only boundary problems. Treat them as validation targets rather than separate implementation tracks.

## Type-first coverage

Use [Backend-neutral declarations](backend-neutral-declarations.md) as the detailed reference for declaration semantics. The short version is:

Use `S1InteropNamespace` when a project needs broad runtime type registration:

```csharp
[assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]
```

Namespace declarations are type-only by default. They keep full-SDK builds practical while still allowing registry lookup for the whole namespace.

`S1InteropType` means "generate backend-neutral coverage for this game type."

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

When reference metadata is available, that type declaration can generate:

- runtime type resolution for Mono and IL2CPP;
- `As`, `TryAs`, and `Is` helpers;
- a typed backend-neutral handle;
- compatible public field and property accessors;
- invokers for unambiguous public methods.

`S1InteropMember` remains available for cases the type facade cannot safely infer yet: private members, better aliases, ambiguous overloads, pinned Harmony targets, and migration-specific reflection bindings.

## Current alpha ergonomics

The current facade layer intentionally favors correctness over pretending every game API is already a normal C# wrapper. You will still see `Handle`, `As`, `TryAs`, `Get<T>`, `Get...Value<T>`, `TrySet...`, and `Invoke` patterns in generated output.

Use named facade members first when they are generated. Discovered public fields, properties, and methods use concrete signatures when the metadata is safe for both backends today: scalar values, `string`, `object`, and `void` method returns. Examples include `vehicle.VehicleName`, `vehicle.CurrentThrottle`, or `LandVehicle.GetCurrentThrottle(vehicle)`.

Fall back to string-based `Get`, `TrySet`, or `Invoke` only when the member is not yet safe to expose as a typed facade member. If a public member is missing, the usual reasons are overload ambiguity, generic method shape, generated backing-field metadata, incompatible Mono/IL2CPP signatures, or a conversion rule S1Interop does not know yet.

Future SDK work should keep moving ordinary public members toward native-feeling facade access while leaving the registry and reflection helpers as implementation details or explicit escape hatches.
