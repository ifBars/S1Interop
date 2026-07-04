# Backend-neutral SDK

The backend-neutral SDK is the main S1Interop product direction.

Instead of writing code against `ScheduleOne.*` in Mono and `Il2CppScheduleOne.*` in IL2CPP, a mod can use generated facades under `S1Interop.ScheduleOne.*`.

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);

if (vehicle.HasValue)
{
    string? name = vehicle.VehicleName?.ToString();
    float? throttle = vehicle.GetCurrentThrottleValue<float>();
}
```

Facade namespaces preserve the original runtime namespace root:

```text
ScheduleOne.Vehicles.LandVehicle -> S1Interop.ScheduleOne.Vehicles.LandVehicle
FishNet.Runtime.*                -> S1Interop.FishNet.Runtime.*
```

S1Interop does not emit shortened duplicate namespaces such as `S1Interop.Vehicles.*`. One canonical namespace is easier to learn, search, and document.

## Type-first coverage

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

