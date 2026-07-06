# Core concepts

This page defines the terms you will see throughout the S1Interop docs. Read it once before the workflow pages so the vocabulary is settled.

For the high-level "what is S1Interop" pitch, see [Introduction](introduction.md). For the full generated-symbol reference, see [Generated output](generator-package.md).

## Runtimes and backends

Schedule One ships both a Mono build and an IL2CPP build, and players may run either one. Without S1Interop, you would need to maintain two separate mod assemblies — one referencing `Assembly-CSharp.dll` and one referencing `Il2CppAssembly-CSharp.dll`. S1Interop lets you target both from a single codebase by abstracting the difference behind generated facades.

### Mono vs IL2CPP at a glance

| Aspect | Mono | IL2CPP |
| --- | --- | --- |
| **Game assembly** | `Assembly-CSharp.dll` | `Il2CppAssembly-CSharp.dll` |
| **Game type namespace** | `ScheduleOne.*` | `Il2CppScheduleOne.*` |
| **Interop layer** | Ordinary managed .NET | Il2CppInterop-generated wrapper types |
| **Harmony patching** | Standard `MethodBase` | Proxy-aware; transpilers restricted |
| **Delegate events** | Standard `+=` / `-=` | Requires `DelegateSupport.ConvertDelegate` |
| **Object casts** | Standard `as` / `is` | Must route through `TryCast<T>` on proxy |

> [!NOTE]
> S1Interop diagnostic codes `S1I004`-`S1I007` catch known IL2CPP boundary failures — Harmony transpiler misuse, managed collection callbacks, managed byte buffers at native boundaries, and plain object casts — at compile time. They only fire when the compilation targets IL2CPP.

### How S1Interop resolves the backend

S1Interop needs to know the active backend so it can route type resolution and member access to the right runtime surface. It resolves the backend in priority order:

1. **`S1InteropTargetRuntime` MSBuild property** — set this in your project file or `local.build.props` to pin the backend at compile time (values: `Mono`, `Il2Cpp`).
2. **Preprocessor symbols** — define `MONO` or `IL2CPP` in your `<DefineConstants>` to tell the generator explicitly.
3. **Runtime probes** — when no compile-time signal is present, the generated `S1InteropRuntime` type probes well-known type and assembly names at startup to detect which backend is loaded.

> [!TIP]
> Pinning the backend at compile time via `S1InteropTargetRuntime` lets the generator emit `Backend` as a `const`, which in turn lets the compiler eliminate the dead branch entirely in release builds.

### Reading the backend at runtime

The generator emits `S1InteropRuntime` and `S1InteropRuntimeBackend` into every project that references `S1Interop.Generators`. You can read the active backend from your mod code at any time:

```csharp
using S1Interop.Generated;

// Enum value: S1InteropRuntimeBackend.Mono, .Il2Cpp, or .Unknown
S1InteropRuntimeBackend backend = S1InteropRuntime.Backend;

// Convenience booleans
bool onMono   = S1InteropRuntime.IsMono;
bool onIl2Cpp = S1InteropRuntime.IsIl2Cpp;
```

When the build target is known at compile time — through the MSBuild property or preprocessor symbols — `Backend` is emitted as a `const` field rather than a runtime-resolved property. This means the compiler can eliminate the unused code path entirely in an optimised build.

### Term reference

| Term | Meaning |
| --- | --- |
| **Mono** | The managed runtime Schedule One ships with in its default build. Mod assemblies reference `Assembly-CSharp.dll` and use ordinary managed types under `ScheduleOne.*`. |
| **IL2CPP** | The alternate runtime Schedule One ships for IL2CPP builds. Mod assemblies reference `Il2CppAssembly-CSharp.dll` and use Il2CppInterop-generated wrapper types under `Il2CppScheduleOne.*`. |
| **Backend** | The active runtime for a given compilation or process: `Mono`, `Il2Cpp`, or `Unknown` (when the generator cannot tell). S1Interop resolves the backend at compile time when preprocessor symbols or the `S1InteropTargetRuntime` MSBuild property are set, and falls back to runtime probes otherwise. |
| **Backend-neutral** | Source that compiles and runs against either backend from one assembly, using generated `S1Interop.ScheduleOne.*` facades instead of direct `ScheduleOne.*` or `Il2CppScheduleOne.*` references. A backend-neutral project may still keep Mono and IL2CPP build configurations as validation targets for the same source. |

## The two packages

S1Interop is two NuGet packages. Mixing them up is the most common source of confusion.

| Term | Package id | What it is | When it runs |
| --- | --- | --- | --- |
| **CLI tool** | `S1Interop` | A .NET global tool exposing the `s1interop` command. Owns project analysis, migration planning/application/rollback, SDK declaration generation, and sandbox verification. | On demand from a terminal. Never runs during compilation. |
| **Generator package** | `S1Interop.Generators` | A Roslyn incremental source generator and analyzer. Reads declarations and emits source plus diagnostics. | During every build (and IDE design-time build) of a project that references it. |

A mod project that only wants the generated SDK surface can reference just the generator package and author declarations by hand. The CLI is the recommended way to produce those declarations, but it is not a runtime dependency of the generator.

S1Interop's package split is also its product boundary. The generator and CLI own interop mechanics: type registration, facade emission, source-risk diagnostics, migration, rollback, and sandbox verification. Higher-level gameplay APIs can sit above that layer and keep their own domain abstractions.

## Declarations

**Declarations** are assembly-level attributes that tell the generator which game types and members to resolve through generated helpers. They live in one S1Interop-owned source file, usually `S1Interop.Generated/S1Interop.BackendNeutral.cs`.

| Attribute | Declares |
| --- | --- |
| `S1InteropNamespace` | Broad runtime type registration for a namespace. Type-only by default; `IncludeMembers = true` opts into member discovery. |
| `S1InteropType` | A single game type opted into a full backend-neutral facade with discovered public members. |
| `S1InteropMember` | An explicit member binding (private, ambiguous overload, pinned Harmony target, reflection seam). |
| `S1InteropGenerateUnityEventBridge` | Request the `S1InteropUnityEventBridge` helper for migrated UnityEvent add/remove calls. |
| `S1InteropGenerateDelegateEventBridge` | Request the `S1InteropDelegateEventBridge` helper for migrated delegate event assignments. |

The attributes themselves are generated by `S1Interop.Generators` on first generation. You never define them by hand. See [Declarations](backend-neutral-declarations.md) for the full property reference.

## Generated symbols

These are the symbols the generator emits into a project that references `S1Interop.Generators`. They are the building blocks the rest of the docs refer to by name.

### `S1InteropObject<TTag>`

The generic backend-neutral handle. A `readonly struct` that wraps a raw `object?` instance and exposes `Instance`, `HasValue`, and `ToString()`. The `TTag` parameter is a per-type marker struct (see **Tag** below) that ties a handle to a declared game type without carrying the runtime type at the type-system level.

Every type facade's `Handle` is a named wrapper around `S1InteropObject<AliasTag>`. Mod code rarely uses `S1InteropObject<TTag>` directly; it uses the facade's `Handle` instead.

### Tag

A per-type empty `readonly struct` (for example `LandVehicleTag`) generated for each declared type. It exists only to bind a `S1InteropObject<TTag>` to a specific game type at the type level. Tags are emitted as nested types of `S1Interop.Generated.S1InteropTypeRegistry`.

### Type facade

A `static internal class` emitted under `S1Interop.ScheduleOne.*` for each declared type (whether from `S1InteropNamespace` or `S1InteropType`). Preserves the original runtime namespace root under `S1Interop`: `ScheduleOne.Vehicles.LandVehicle` becomes `S1Interop.ScheduleOne.Vehicles.LandVehicle`. The generator never emits shortened duplicate namespaces.

A facade always exposes `Handle`, `Type`, `TypeName`, `Create(...)`, `CreateHandle(...)`, `TryCreate(...)`, `Is(...)`, `TryAs(...)`, `As(...)`, `Get(...)`, `TrySet(...)`, `Invoke(...)`. Whether it also exposes named member accessors depends on how the type was declared:

- `S1InteropNamespace` with default `IncludeMembers = false`: facade and `Handle` exist, but no named member accessors. Only generic and reflection-style members.
- `S1InteropType` (or `S1InteropNamespace` with `IncludeMembers = true`): the generator discovers compatible public fields, properties, and unambiguous public methods from referenced Mono and IL2CPP metadata and emits them as named accessors on the facade and `Handle`.

Prefer named facade members when they exist; the string-based helpers are fallback and migration support.

### Handle

A `readonly struct` nested on each type facade. Wraps `S1InteropObject<AliasTag>` and is the primary value type mod code holds for a backend-neutral game object. Obtained from `Facade.As(rawInstance)` or `Facade.TryAs(rawInstance, out var handle)`.

The members available on a `Handle` depend on how the type was declared:

- **Namespace-only** (via `S1InteropNamespace` with default `IncludeMembers = false`): the `Handle` exposes only generic members - `HasValue`, `Instance` (the raw `object?`), `Value` (the underlying `S1InteropObject<...>`), and `ToString`. You can still use `As`/`TryAs`/`Is` on the facade and call reflection-style `Get`/`TrySet`/`Invoke` on the registry, but there are no named accessors on the `Handle` itself.
- **Type-declared** (via `S1InteropType`, or `S1InteropNamespace` with `IncludeMembers = true`): the `Handle` gains named accessors for compatible public fields, properties, and unambiguous public methods discovered from referenced Mono and IL2CPP metadata. Backend-neutral scalar, `string`, `object`, `void` method shapes, declared enum mirrors, and game-object values with declared facades get concrete signatures, such as `vehicle.VehicleName`, `vehicle.CurrentThrottle`, or `vehicle.AssignedDriver`; read-only discovered values get named getters without named setters; unsafe shapes stay on object/generic fallback helpers.

The transition from generic-only to named accessors happens after the next build completes. There is a short delay between saving a declaration file and the IDE regenerating the source - the Roslyn generator runs as part of compilation, which is not instantaneous. See [Build timing](backend-neutral-declarations.md#build-timing) for details.

### `S1InteropTypeRegistry`

The runtime type-name and resolution cache, emitted under `S1Interop.Generated`. Holds per-alias `*Name`, `*MonoName`, `*Il2CppName` constants, plus `Resolve`, `Create`, `IsInstance`, `As*`, `TryAs*`, `Is*`, `Get*`, `TrySet*`, `Invoke*`, and `Invoke*<T>` members. Prefer the typed facades over calling the registry directly.

### `S1InteropMemberRegistry`

Reflection-based get/set/invoke helpers used by the registry and by facades. Handles value conversion, overload selection by parameter type names, and instance vs static dispatch. Rarely called directly from mod code; the facades route through it.

### `S1InteropRuntime` and `S1InteropRuntimeBackend`

`S1InteropRuntimeBackend` is an enum with `Unknown`, `Mono`, `Il2Cpp`. `S1InteropRuntime` exposes `Backend`, `IsMono`, `IsIl2Cpp`. When the build target is known at compile time (from the `S1InteropTargetRuntime` MSBuild property or `MONO`/`IL2CPP` preprocessor symbols), `Backend` is emitted as a `const`. Otherwise it is resolved at runtime through type and assembly probes against the referenced game surface.

### `S1InteropObjectCast`

Helper for `is`/`as` casts that cross the IL2CPP proxy boundary. Plain C# casts from `object` or `Il2CppObjectBase` to Unity object types fail on IL2CPP; `S1InteropObjectCast.As<T>(value)` routes through the proxy's `TryCast<T>` method when needed. `S1I007` warns on plain casts that should use this helper instead.

### `S1InteropDelegateBridge`

Helper for converting managed delegates into the IL2CPP delegate shape required by `DelegateSupport.ConvertDelegate`. Used internally by the UnityEvent bridge; rarely referenced directly by mod code.

### `S1InteropUnityEventBridge` and `S1InteropDelegateEventBridge`

Bridge helpers emitted only when the matching `S1InteropGenerateUnityEventBridge` / `S1InteropGenerateDelegateEventBridge` attribute is present. `S1InteropUnityEventBridge` wraps `Add`/`Remove` for parameterless and one-argument UnityEvents with a per-event delegate cache so the same managed listener can be removed later. `S1InteropDelegateEventBridge` exposes `Combine<T>`/`Remove<T>` for delegate event fields.

### `S1Interop.Generated`

The namespace that holds the internal infrastructure above. Prefer the typed `S1Interop.ScheduleOne.*` facades over calling `S1Interop.Generated.*` types directly from mod code.

## Facade namespace rule

One canonical namespace per game type. The original runtime namespace root is preserved under `S1Interop`:

```text
ScheduleOne.Vehicles.LandVehicle -> S1Interop.ScheduleOne.Vehicles.LandVehicle
FishNet.Runtime.*                -> S1Interop.FishNet.Runtime.*
```

The generator does not emit shortened duplicates like `S1Interop.Vehicles.LandVehicle`. One canonical namespace is easier to learn, search, and document.

## Migration and verification

| Term | Meaning |
| --- | --- |
| **Migration** | A CLI operation that converts analysis findings into file changes: project edits, source rewrites, generated declarations, solution updates. Always reversible through a manifest and backups. |
| **Migration plan** | The dry-run list of operations the CLI would apply. Review it before `--apply`. |
| **Migration applier** | The Core component that writes file changes, records backups, and writes a rollback manifest. |
| **Sandbox verification** | Running a migration against a temporary copy of the project, optionally building it, without touching the original tree. Owned by `MigrationVerifier`. |
| **Rollback manifest** | A JSON manifest written under `s1interop-runs/<run-id>/` for every applied migration. Consumed by `s1interop migrate rollback`. |
| **Source-risk report** | A generated report of cases the migration could not safely rewrite. These remain as diagnostics or manual review items instead of being guessed. |

The pipeline is `analyze` -> `plan` -> `apply` -> `verify`. See [Migration overview](migrating-mono-mods.md) for the workflow paths.

## SDK generation

**`sdkgen`** is the CLI command that writes declaration files from source usage or local game references. It does not emit the facades itself; the generator package does that at build time. Two modes:

- **Usage-driven** (default): inspects source references, aliases, namespace imports, and string-held game type names, then emits only the declarations the project appears to need.
- **Full SDK** (`--full-sdk`): seeds broad type coverage from local reference metadata as one compact `S1InteropNamespace` declaration. Used for blank or exploratory projects.

See [SDK generation](sdk-generation.md) and [Generated output](generator-package.md) for the compile-time side.

## Diagnostics

Compile-time diagnostics reported by the generator, grouped into:

- **Declaration diagnostics** `S1I001`-`S1I003`: validate `S1InteropType` and `S1InteropMember` against referenced Mono/IL2CPP assemblies. Quiet when no game reference surface is available.
- **IL2CPP source-boundary diagnostics** `S1I004`-`S1I007`: catch known IL2CPP runtime failures (Harmony transpilers, managed collection callback signatures, managed byte buffers at native boundaries, plain object/proxy casts). Fire only when the compilation targets IL2CPP.

See [Diagnostics](diagnostics.md) for the full table.

## Local-state files

| File | Purpose | Committed? |
| --- | --- | --- |
| `local.build.props` | Machine-specific Mono and IL2CPP game paths, plus optional `S1InteropGeneratorPackageSource` for unpublished local packages. | No (gitignored). |
| `local.build.props.example` | Template committed by `s1interop new` and migration. Copy to `local.build.props` and fill in. | Yes. |
| `s1interop-runs/<run-id>/` | Backups and rollback manifest for each applied migration. | No (gitignored). |
| `s1interop-cache/` | CLI cache for analysis and generation. | No (gitignored). |
| `artifacts/packages/` | Local alpha NuGet packages produced by `dotnet pack`. | No (gitignored). |
| `S1Interop.Generated/S1Interop.BackendNeutral.cs` | The declaration file produced by `sdkgen`/`init`/`migrate`. | Yes, this is the reviewable SDK surface. |

See [Local game paths](local-paths.md) for the property reference.

## Which tool for which job

| You want to... | Use this |
| --- | --- |
| Start a new backend-neutral mod | `s1interop new` then `s1interop sdkgen --full-sdk --apply`. See [New projects](new-projects.md). |
| Inspect a mod's runtime assumptions and risks | `s1interop analyze`. See [Commands](commands.md). |
| Generate SDK declarations from existing source | `s1interop sdkgen --apply`. See [SDK generation](sdk-generation.md). |
| Move a Mono mod toward one backend-neutral assembly | `s1interop init` then `s1interop sdkgen --apply`. See [Migrate to backend-neutral](migrate-to-backend-neutral.md). |
| Build separate Mono and IL2CPP assemblies | `s1interop migrate --dual-runtime --apply`. See [Migrate to dual-runtime](migrate-to-dual-runtime.md). |
| Verify a migration without touching the real tree | `s1interop verify-migration`. See [Migration overview](migrating-mono-mods.md). |
| Roll back an applied migration | `s1interop migrate rollback <manifest>`. See [Rollback](migrating-mono-mods.md). |
| Add a private member binding the facade cannot infer | `[assembly: S1InteropMember(...)]` by hand. See [Declarations](backend-neutral-declarations.md). |
| Catch IL2CPP boundary failures at compile time | Reference `S1Interop.Generators` and build for IL2CPP. See [Diagnostics](diagnostics.md). |
| Embed analysis or migration in another tool | `S1Interop.Core` directly. See [API reference](api-reference.md). |

## Next steps

- [Installation](getting-started.md) to build and pack the local alpha packages.
- [Local game paths](local-paths.md) to configure Mono and IL2CPP references.
- [Commands](commands.md) for the CLI reference.
- [Generated output](generator-package.md) for the full generated-symbol reference.
