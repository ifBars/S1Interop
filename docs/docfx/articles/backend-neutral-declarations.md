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
- A `S1Interop.ScheduleOne.*` facade per matching type when `IncludeMembers = true` (member discovery) or when the same type is also declared via `S1InteropType`.

Namespace declarations are the practical way to seed broad SDK coverage from `sdkgen --full-sdk` without emitting thousands of per-type attributes.

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
- `Type`, `TypeName`, `Create(...)`, `Create<T>(...)`;
- `Is(object?)`, `TryAs(object?, out Handle)`, `As(object?)`;
- `Get`/`TrySet`/`Invoke`/`Invoke<T>` reflection helpers for both `Handle` and raw `object?` receivers;
- accessors for compatible public fields, properties, and unambiguous public methods, discovered from the referenced Mono and IL2CPP metadata;
- the underlying registry `Tag` and resolution entries in `S1Interop.Generated.S1InteropTypeRegistry`.

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

A typed accessor with the chosen `Alias` on the owner type's facade and `Handle`. For methods, the accessor accepts the declared parameter shapes and returns the result. For fields and properties, it exposes get/set on `Handle` and raw receivers.

If the owner alias is unknown, the generator reports `S1I002`. If the member is not found on the resolved owner type, it reports `S1I003`.

Use `S1InteropMember` for:

- private or internal members;
- better aliases for readability;
- overloaded methods that need explicit parameter type names or by-ref markers;
- Mono/IL2CPP disagreements that need a pinned binding;
- migration-inferred reflection patterns that cannot yet be represented by the automatic type facade.

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
2. Build the project (or let the IDE run a design-time build).
3. The new or changed generated symbols appear in IntelliSense and are compiled into the assembly.

If a generated symbol is missing immediately after editing a declaration, build once. The most common cause of "generated type not found" is that no design-time build has run since the declaration was added.

Generated symbols are emitted into the same compilation as the rest of the project. They are not separate assemblies and are not referenced from a package at runtime. The `S1Interop.Generators` package only ships the generator DLL under `analyzers/dotnet/cs`; it does not add a runtime DLL reference.

## Related pages

- [Generated output](generator-package.md)
- [Backend-neutral SDK](backend-neutral-sdk.md)
- [SDK generation](sdk-generation.md)
- [Migrate to backend-neutral](migrate-to-backend-neutral.md)
- [Diagnostics](diagnostics.md)
