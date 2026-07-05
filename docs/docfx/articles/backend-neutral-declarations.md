# Backend-neutral declarations

Backend-neutral declarations tell the S1Interop generator which game types and members should be resolved through generated Mono/IL2CPP-aware helpers.

Most projects should let `sdkgen` write these declarations first, then keep the generated file small and reviewable as the mod touches more of the game surface.

## Declaration roles

Use each declaration for a different level of intent:

| Declaration | Use it when |
| --- | --- |
| `S1InteropNamespace` | You need broad runtime type registration for a namespace, usually without generating member facades for every type. |
| `S1InteropType` | You need a backend-neutral facade and compatible public member helpers for one game type. |
| `S1InteropMember` | You need an explicit binding for a private member, ambiguous overload, pinned Harmony target, or migration-discovered reflection access. |
| `S1InteropGenerateUnityEventBridge` | You need generated listener conversion helpers for simple `UnityEvent` add/remove calls. |
| `S1InteropGenerateDelegateEventBridge` | You need generated delegate combine/remove helpers for migrated event assignments. |

Prefer the broadest declaration that still describes the real need. Do not use `S1InteropMember` to manually enumerate ordinary public members after a type facade can discover them from metadata.

## Namespace declarations

Use `S1InteropNamespace` when a project needs broad type lookup coverage:

```csharp
[assembly: S1Interop.S1InteropNamespace(
    "ScheduleOne",
    IncludeSubnamespaces = true)]
```

Namespace declarations are type-only by default. They are a good fit for full SDK seeding because they can register many types without creating a large public member facade for every type in the game.

Set `IncludeMembers = true` only when broad member discovery is intentional and reviewable. For most mods, add `S1InteropType` declarations for the specific types where source code needs member access.

## Type declarations

Use `S1InteropType` when mod source needs a generated facade for one game type:

```csharp
[assembly: S1Interop.S1InteropType(
    "ScheduleOne.Vehicles.LandVehicle",
    Alias = "LandVehicle")]
```

When reference metadata is available, the generator can use this declaration to emit:

- runtime type-name selection for Mono and IL2CPP;
- `As`, `TryAs`, and `Is` helpers;
- a typed backend-neutral handle;
- compatible public field and property accessors;
- invokers for unambiguous public methods.

Specify `Il2CppTypeName` only when the default `ScheduleOne.*` to `Il2CppScheduleOne.*` mapping is wrong or too ambiguous for the local reference surface.

## Member overrides

Use `S1InteropMember` for cases that cannot safely be inferred from the type facade:

```csharp
[assembly: S1Interop.S1InteropType(
    "ScheduleOne.PlayerScripts.PlayerCamera",
    Alias = "PlayerCamera")]

[assembly: S1Interop.S1InteropMember(
    "PlayerCamera",
    "Awake",
    Alias = "PlayerCameraAwake",
    Kind = S1Interop.S1InteropMemberKind.Method,
    ParameterTypeNames = new string[] { })]
```

`OwnerAlias` points at a declared type alias, not directly at a runtime type name. `Alias` controls the generated registry property or helper name, so choose a stable name that describes the owner and member well enough for generated code review.

For overloaded methods, set `ParameterTypeNames` with backend-neutral Mono type names where possible. The generator maps declared type aliases to the active runtime when the build has enough reference metadata to validate the binding.

## Bridge declarations

Migration can add bridge declarations when source contains simple delegate patterns that need different runtime delegate shapes:

```csharp
[assembly: S1Interop.S1InteropGenerateUnityEventBridge]
[assembly: S1Interop.S1InteropGenerateDelegateEventBridge]
```

These declarations generate helpers under `S1Interop.Generated`. They are implementation support for migrated code, not a replacement for higher-level S1API helpers when those helpers already cover the event or UI surface.

## Generated file placement

Keep declarations in generated or S1Interop-owned source such as `S1Interop.Generated/S1Interop.BackendNeutral.cs`. That keeps the migration boundary obvious and makes rollback or regeneration easier to review.

The declarations and `S1Interop.Generators` package are a compile-time unit. If a project contains S1Interop declarations, it must also reference the generator package so attributes, registries, diagnostics, and facade helpers are emitted during the build.

## Related pages

- [Backend-neutral SDK](backend-neutral-sdk.md)
- [SDK generation](sdk-generation.md)
- [Migrate to backend-neutral](migrate-to-backend-neutral.md)
- [Diagnostics](diagnostics.md)
