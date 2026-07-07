# Backend-neutral SDK

For what the generator emits at build time and when those symbols appear, see [Generated output](generator-package.md). For the attribute reference that drives generation, see [Declarations](backend-neutral-declarations.md).

Instead of writing code against `ScheduleOne.*` in Mono and `Il2CppScheduleOne.*` in IL2CPP, use generated facades under `S1Interop.ScheduleOne.*`.

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);

if (vehicle.HasValue)
{
    string? name = vehicle.VehicleName;
    float? throttle = vehicle.CurrentThrottle;
    string? started = vehicle.StartEngine();
}
```

Facade namespaces preserve the original runtime namespace root:

```text
ScheduleOne.Vehicles.LandVehicle -> S1Interop.ScheduleOne.Vehicles.LandVehicle
FishNet.Runtime.*                -> S1Interop.FishNet.Runtime.*
```

S1Interop does not emit shortened duplicates such as `S1Interop.Vehicles.*`.

## Authoring model and validation targets

The goal is one source surface with generated facades, not a pile of `#if MONO` / `#if IL2CPP` branches.

Keep Mono and IL2CPP build configurations as validation targets when you have both local reference surfaces.

## Type-first coverage

Short version:

Use `S1InteropNamespace` for broad runtime type registration:

```csharp
[assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]
```

Namespace declarations are type-only by default. They keep full-SDK builds practical without generating member facades for every type.

`S1InteropType` means "generate backend-neutral coverage for this game type."

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

When metadata is available, `S1InteropType` can generate:

- runtime type resolution for Mono and IL2CPP;
- `As`, `TryAs`, and `Is` helpers;
- a typed backend-neutral handle;
- compatible public field and property accessors;
- typed handle methods and facade invokers for unambiguous public methods.

`S1InteropMember` remains available for cases the type facade cannot safely infer yet: private members, better aliases, ambiguous overloads, pinned Harmony targets, and migration-specific reflection bindings.

## Current limits

The facade layer favors correctness over pretending every game API is a normal C# wrapper. You will still see `Handle`, `As`, `TryAs`, `Get<T>`, `Get...Value<T>`, `TrySet...`, and `Invoke`.

Use named facade members first. Discovered public fields, properties, and methods use concrete signatures when metadata is safe for both backends: scalar values, `string`, `object`, `void`, declared enum mirrors, and game-object values whose types are also declared as S1Interop facades. Read-only fields/properties get getters; named `TrySet...` helpers require writable metadata.

Use `S1InteropMember` for private members, aliases, pinned bindings, or migration-generated reflection targets. If the binding is unresolved or ambiguous, it stays on object/generic fallback helpers.

Fall back to string-based `Get`, `TrySet`, or `Invoke` only when a typed facade member is not safe yet. Common blockers are overload ambiguity, generic method shape, generated backing-field metadata, incompatible Mono/IL2CPP signatures, or missing conversion rules.

Treat registry and reflection helpers as escape hatches, not the normal way to write mod code.
