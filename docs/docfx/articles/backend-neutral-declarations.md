# Declarations

Declarations tell the `S1Interop.Generators` package which game types and members should be resolved through generated backend-neutral helpers. They are assembly-level attributes written to a single S1Interop-owned source file and consumed by the Roslyn generator at compile time.

Most projects should let `sdkgen` write these declarations first, then keep the generated file small and reviewable as the mod touches more of the game surface. See [SDK generation](sdk-generation.md) for the CLI side.

For what the generator emits from these declarations and when, see [Generated output](generator-package.md).

## Declaration roles

| Declaration | Use it when |
| --- | --- |
| `S1InteropNamespace` | You need broad runtime type registration for a namespace, usually without generating member facades for every type. |
| `S1InteropType` | You need a backend-neutral facade and compatible public member helpers for one game type. |
| `S1InteropMember` | You need an explicit binding for a private member, ambiguous overload, pinned Harmony target, or migration-discovered reflection access. |
| `S1InteropGenerateUnityEventBridge` | You need generated listener conversion helpers for simple `UnityEvent` add/remove calls. |
| `S1InteropGenerateDelegateEventBridge` | You need generated delegate combine/remove helpers for migrated event assignments. |

Prefer the broadest declaration that still describes the real need. Do not use `S1InteropMember` to manually enumerate ordinary public members after a type facade can discover them from metadata.

## `S1InteropNamespace`

Registers every public type in a namespace for runtime type resolution without requiring per-type attributes.

```csharp
[assembly: S1Interop.S1InteropNamespace(
    "ScheduleOne",
    IncludeSubnamespaces = true)]
```

### Properties

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `namespaceName` (positional) | `string` | Required | The Mono runtime namespace to include, such as `ScheduleOne`. `Il2Cpp`-prefixed names are normalized back to the Mono root. |
| `IncludeSubnamespaces` | `bool` | `false` | Include child namespaces. |
| `IncludeMembers` | `bool` | `false` | Discover compatible public members for matching types. Off by default so full-SDK builds stay reviewable. |

### What this generates

- One registry entry per matching public, non-generic, top-level type in referenced Mono/IL2CPP assemblies.
- Per-type `*Name`, `*MonoName`, `*Il2CppName`, `Resolve`, `Create`, `As*`, `TryAs*`, `Is*`, `Get*`, `TrySet*`, and `Invoke*` members on `S1Interop.Generated.S1InteropTypeRegistry`.
- A `S1Interop.ScheduleOne.*` facade and `Handle` struct for every matching type. When `IncludeMembers` is `false` (the default), the `Handle` only exposes generic members: `HasValue`, `Instance`, `Value`, and `ToString`. No named member accessors are generated. This is useful for runtime type checks, `As`/`TryAs`/`Is` wrapping, and reflection-style `Get`/`TrySet`/`Invoke` calls, but it does not give you named properties or methods on the `Handle`.

To get named member accessors (fields, properties, and methods on the `Handle`), either set `IncludeMembers = true` on the namespace declaration or add a separate `S1InteropType` declaration for the specific types where you want member facades. Concrete signatures are emitted only where discovered metadata is backend-neutral. `S1InteropType` is preferred for individual types because it keeps member discovery scoped and reviewable.

Namespace declarations are the practical way to seed broad SDK coverage from `sdkgen --full-sdk` without emitting thousands of per-type attributes.

> [!IMPORTANT]
> Adding or changing declarations triggers a design-time build, but the update is not instantaneous. After saving the declaration file, there is a short delay before the IDE regenerates the source and IntelliSense reflects the new symbols. See [Build timing](#build-timing) below.

## `S1InteropType`

Opts a single game type into a backend-neutral facade with discovered public member accessors.

```csharp
[assembly: S1Interop.S1InteropType(
    "ScheduleOne.Vehicles.LandVehicle",
    Alias = "LandVehicle")]
```

### Properties

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `monoTypeName` (positional) | `string` | Required | The full Mono runtime type name, such as `ScheduleOne.Vehicles.LandVehicle`. |
| `Il2CppTypeName` | `string?` | Computed | Override the IL2CPP wrapper type name when the default `ScheduleOne.*` to `Il2CppScheduleOne.*` mapping is wrong or ambiguous. |
| `Alias` | `string?` | Simple name | The generated registry and facade name. Defaults to the type's simple name, sanitized to a valid identifier. |

### What this generates

A `S1Interop.ScheduleOne.Vehicles.LandVehicle` facade with:

- a `Handle` readonly struct wrapping the runtime instance;
- `Type`, `TypeName`, `Create(...)`, `Create<T>(...)`, `CreateHandle(...)`, `TryCreate(out Handle, ...)`;
- `Is(object?)`, `TryAs(object?, out Handle)`, `As(object?)`;
- `Get`/`TrySet`/`Invoke`/`Invoke<T>` reflection helpers for both `Handle` and raw `object?` receivers;
- accessors for compatible public fields, properties, and unambiguous public methods, discovered from the referenced Mono and IL2CPP metadata;
- the underlying registry `Tag` and resolution entries in `S1Interop.Generated.S1InteropTypeRegistry`.

This is the key difference from `S1InteropNamespace` alone: `S1InteropType` opts the type into member discovery, so the `Handle` gains named accessors in addition to the generic `HasValue`/`Instance`/`Value` members. When the discovered member type is backend-neutral today, the accessor uses a concrete signature, for example `vehicle.VehicleName` as `string?`, `vehicle.CurrentThrottle` as `float?`, `vehicle.AssignedDriver` as `S1Interop.ScheduleOne.PlayerScripts.Player.Handle` when `Player` is also declared as a facade, or an S1Interop-owned enum mirror when the enum type is declared and both backend value sets agree. Read-only discovered fields/properties remain readable but do not get named `TrySet...` helpers unless both available backend surfaces expose a writable member. Members whose types are undeclared game wrappers, collections, by-ref values, generic methods, overloaded methods, or otherwise unsafe stay on the object/generic fallback helpers. Generated backing fields are skipped; use the real public field or property instead. If you only have an `S1InteropNamespace` declaration for `ScheduleOne`, the `Player` type's `Handle` will only have generic members - you will not see `player.Money` or similar named accessors until you add an `S1InteropType` declaration for that specific type.

If the referenced assemblies do not contain the requested type, the generator reports `S1I001`. Declaration diagnostics are quiet when no game reference surface is available, so package-restore and docs-only builds do not fail.

## `S1InteropMember`

Explicit member binding for cases the type facade cannot safely infer.

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

### Properties

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `ownerAlias` (positional) | `string` | Required | The alias of a declared `S1InteropType`. Must match an existing type alias, not a runtime type name. |
| `memberName` (positional) | `string` | Required | The runtime member name to resolve. |
| `Alias` | `string?` | `memberName` | The generated accessor name on the owner facade and `Handle`. |
| `Kind` | `S1InteropMemberKind` | `FieldOrProperty` | `FieldOrProperty`, `Method`, `Field`, or `Property`. |
| `IsStatic` | `bool` | `false` | Whether the target member is static. |
| `ParameterTypeNames` | `string[]` | Empty | Mono type names used to disambiguate overloaded methods. |

### What this generates

Named accessors with the chosen `Alias` on the owner type's facade and, for instance fields/properties, on `Handle`. Explicit declarations resolve private members, better aliases, ambiguous overloads, pinned bindings, and migration-inferred reflection patterns. When the referenced Mono and IL2CPP metadata identify one compatible member, the generator enriches the declaration with return, value, parameter type, and parameter-name metadata. That means explicit field/property and method bindings can still get concrete scalar, `string`, enum, and declared-facade `Handle` signatures.

If metadata is missing, ambiguous, incompatible, generic, or uses a conversion S1Interop does not understand yet, the explicit binding stays on the object/generic `Get<T>`, `Get...Value<T>`, `TrySet`, and `Invoke<T>` fallback helpers.

If the owner alias is unknown, the generator reports `S1I002`. If the member is not found on the resolved owner type, it reports `S1I003`.

Use `S1InteropMember` for:

- private or internal members;
- better aliases for readability;
- overloaded methods that need explicit parameter type names or by-ref markers;
- Mono/IL2CPP disagreements that need a pinned binding;
- migration-inferred reflection patterns that cannot yet be represented by the automatic type facade, including simple cached `FieldInfo`/`PropertyInfo`/`MethodInfo` lookups from `typeof(...).GetField(...)`, `typeof(...).GetProperty(...)`, `typeof(...).GetMethod(...)`, `AccessTools.Field(typeof(...), "...")`, `AccessTools.Property(typeof(...), "...")`, `AccessTools.PropertyGetter(typeof(...), "...")`, `AccessTools.PropertySetter(typeof(...), "...")`, or `AccessTools.Method(typeof(...), "...")`. Migration-inferred declarations skip backing-field names such as `HealthBackingField` or `<Health>k__BackingField`; use the real field or property instead.

## Bridge declarations

Migration can add bridge declarations when source contains simple delegate patterns that need different runtime delegate shapes:

```csharp
[assembly: S1Interop.S1InteropGenerateUnityEventBridge]
[assembly: S1Interop.S1InteropGenerateDelegateEventBridge]
```

### What these generate

- `S1InteropGenerateUnityEventBridge` emits `S1Interop.Generated.S1InteropUnityEventBridge` with `Add`/`Remove` overloads for parameterless and one-argument UnityEvents, including a per-event delegate wrapper cache so the same managed listener can be removed later.
- `S1InteropGenerateDelegateEventBridge` emits `S1Interop.Generated.S1InteropDelegateEventBridge` with `Combine<T>`/`Remove<T>` helpers for delegate event fields.

These are implementation support for migrated code, not a replacement for higher-level S1API helpers when those helpers already cover the event or UI surface.

## Where to keep declarations

Keep declarations in generated or S1Interop-owned source such as `S1Interop.Generated/S1Interop.BackendNeutral.cs`. That keeps the migration boundary obvious and makes rollback or regeneration easier to review.

The declarations and the `S1Interop.Generators` package are a compile-time unit. If a project contains S1Interop declarations, it must also reference the generator package so the attributes, registries, diagnostics, and facade helpers are emitted during the build.

## Build timing

Declarations are read by the generator during compilation. After you edit the declaration file:

1. Save the file.
2. The IDE triggers a design-time build in the background, or you run `dotnet build` manually.
3. The new or changed generated symbols appear in IntelliSense and are compiled into the assembly.

There is a short delay between saving and the generated symbols updating. The Roslyn generator runs as part of the compilation, which is not instantaneous - the IDE needs to re-parse, re-bind, and re-emit before IntelliSense reflects the new state. The delay depends on project size, available game reference assemblies, and IDE load. If a generated symbol is missing immediately after editing a declaration, wait a moment or trigger a full build (`dotnet build`).

The most common cause of "generated type not found" is that no design-time build has completed since the declaration was added. If the symbol still does not appear after a full build, check that the project references `S1Interop.Generators` and that the declaration file is included in the compilation.

> [!IMPORTANT]
> When you start with an `S1InteropNamespace` declaration and later add an `S1InteropType` for a specific type, the `Handle` for that type gains named member accessors after the next build completes. Concrete signatures appear where the discovered member metadata is backend-neutral. Before the build runs, the `Handle` still only exposes generic members (`HasValue`, `Instance`, `Value`). This is expected - the generator has not yet re-discovered members for the newly declared type.

Generated symbols are emitted into the same compilation as the rest of the project. They are not separate assemblies and are not referenced from a package at runtime. The `S1Interop.Generators` package only ships the generator DLL under `analyzers/dotnet/cs`; it does not add a runtime DLL reference.

## Related pages

- [Generated output](generator-package.md)
- [Backend-neutral SDK](backend-neutral-sdk.md)
- [SDK generation](sdk-generation.md)
- [Migrate to backend-neutral](migrate-to-backend-neutral.md)
- [Diagnostics](diagnostics.md)
