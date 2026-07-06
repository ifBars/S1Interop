# SDK generation

`sdkgen` is the CLI command that writes the generated declarations powering the backend-neutral SDK. It produces declaration files on disk; the `S1Interop.Generators` Roslyn package then consumes those declarations at build time. See [Generated output](generator-package.md) for the compile-time side.

For existing source, use the narrow mode:

```powershell
s1interop sdkgen . --apply
```

This inspects source usage, aliases, namespace imports, string-held game type names, and local reference metadata. It generates the types the project appears to need.

For a blank or exploratory project, use the full SDK mode:

```powershell
s1interop sdkgen . --full-sdk --apply
```

This seeds a compact namespace declaration for discoverable Schedule One types from the configured local reference surface. It does not generate every public member for every type.

## What gets generated

Generated SDK source can include:

- `S1InteropType` declarations;
- `S1InteropNamespace` declarations;
- root-preserving facades under `S1Interop.ScheduleOne.*`;
- runtime type registry entries;
- bridge helpers for Unity events or delegate conversion;
- compiler diagnostics for known IL2CPP boundary failures.

The generated SDK comes from local reference metadata. Do not commit game assemblies, generated IL2CPP wrappers, decompiled source, or static hand-maintained catalogs of Schedule One APIs.

## Namespace and type declarations

For a detailed declaration reference, see [Backend-neutral declarations](backend-neutral-declarations.md).

Full SDK generation usually starts with one broad namespace declaration:

```csharp
[assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]
```

That gives the runtime registry broad type coverage without generating a giant member facade for every type in the game.

Add `S1InteropType` for types where mod code needs native-feeling member access:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

The generated facade lives under the original namespace root:

```text
S1Interop.ScheduleOne.Vehicles.LandVehicle
```

## When to add manual overrides

Prefer generated type coverage first. Use namespace declarations for broad type registration, then add `S1InteropType` declarations for the specific types where the mod needs generated member facades.

Add explicit member overrides only when a binding cannot safely come from metadata:

- the member is private or internal;
- an overload needs explicit parameter names or by-ref markers;
- Mono and IL2CPP disagree in a way that needs a pinned binding;
- a migration found reflection code that needs a stable generated target, such as a cached `FieldInfo`, `PropertyInfo`, `AccessTools.Field(typeof(...), "...")`, or `AccessTools.Property(typeof(...), "...")` binding in a consumer mod.
