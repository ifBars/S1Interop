# Generated output

Check this page when generated symbols are missing or you need to know what a declaration emits.

For the attribute reference that drives generation, see [Declarations](backend-neutral-declarations.md). For the conceptual facade model, see [Backend-neutral SDK](backend-neutral-sdk.md).

## When the generator runs

`S1Interop.Generators` is a Roslyn incremental source generator. It runs as part of C# compilation in two places:

- **Design-time builds**: triggered by Visual Studio, Rider, and OmniSharp when files or project state change. This is what makes generated symbols show up in IntelliSense, Go-to-Definition, and completion.
- **Full builds**: triggered by `dotnet build`, `dotnet rebuild`, MSBuild, or IDE build commands. This is what produces the generated source that ends up in the output assembly.

After adding, editing, or removing a declaration, rebuild the project or let the IDE regenerate before calling the new symbols. If a generated type or member is missing from IntelliSense, run `dotnet build` once.

The generator does not run at runtime. It only emits C# source that the compiler turns into IL alongside the rest of the project.

## Two surfaces, one package

The generator is compile-time only. It reads declarations already in the compilation and emits source plus diagnostics. The CLI handles commands, project edits, and source migration.

| Responsibility | Owner |
| --- | --- |
| Write declaration files (`S1InteropType`, `S1InteropNamespace`, `S1InteropMember`) | CLI (`sdkgen`, `init`, `migrate`) or you, if you author declarations by hand. |
| Rewrite call sites to generated facades | CLI (`migrate`) before compilation. |
| Emit `S1Interop.Generated` registry, facades, and bridge helpers | Generator package, during compilation. |
| Report `S1I001`-`S1I008` diagnostics | Generator package, during compilation. |
| Resolve Mono/IL2CPP type names at runtime | Generated `S1Interop.Generated.S1InteropRuntime` and `S1InteropTypeRegistry`. |

If your mod references only the generator package, you can author declarations by hand. The CLI is helpful, but not required at runtime.

## Generated source files

The generator emits stable file names under "Dependencies" / "Analyzers" / "Source files":

| File | Emitted when | Contains |
| --- | --- | --- |
| `S1InteropTypeAttribute.g.cs` | Always, on first generation. | The `S1Interop` attribute classes (`S1InteropTypeAttribute`, `S1InteropNamespaceAttribute`, `S1InteropMemberAttribute`, `S1InteropMemberKind`, `S1InteropPatchAttribute`, patch handler attributes, `S1InteropGenerateUnityEventBridgeAttribute`, `S1InteropGenerateDelegateEventBridgeAttribute`). You never define these yourself. |
| `S1Interop.TypeRegistry.g.cs` | Always, when the generator is referenced. | The `S1Interop.Generated` runtime backend detector, type registry, member registry, object handle, object-cast helper, delegate bridge, per-type `Tag`/registry entries, and every `S1Interop.ScheduleOne.*` type facade. |
| `S1Interop.HarmonyPatcher.g.cs` | Only when at least one `[S1InteropPatch]` class is present. | Internal Harmony patch registrar with a module initializer. It resolves patch targets through `S1InteropMemberRegistry` and applies generated patch declarations once. |
| `S1Interop.UnityEventBridge.g.cs` | Only when `[assembly: S1InteropGenerateUnityEventBridge]` is present. | `S1Interop.Generated.S1InteropUnityEventBridge` with `Add`/`Remove` overloads for parameterless and one-argument UnityEvents. |
| `S1Interop.DelegateEventBridge.g.cs` | Only when `[assembly: S1InteropGenerateDelegateEventBridge]` is present. | `S1Interop.Generated.S1InteropDelegateEventBridge` with `Combine`/`Remove` helpers for delegate event fields. |

Type facades under `S1Interop.ScheduleOne.*` are emitted as part of `S1Interop.TypeRegistry.g.cs` (one file) rather than one file per facade, so the generated file count stays small even for broad SDK generation.

## Generated namespaces and symbols

### `S1Interop.Generated`

Internal infrastructure shared by every facade and by migration-rewritten call sites. Prefer the typed facades over calling these directly from mod code.

- `S1InteropRuntime` and `S1InteropRuntimeBackend`: detect or constant-bind the active backend (`Mono`, `Il2Cpp`, or `Unknown`). When the build target is known at compile time, `Backend` is emitted as a `const`; otherwise it is resolved at runtime through type and assembly probes.
- `S1InteropTypeRegistry`: the type-name and resolution cache. Exposes per-alias `*Name`, `*MonoName`, `*Il2CppName`, `Resolve`, `Create`, `IsInstance`, `As*`, `TryAs*`, `Is*`, `Get*`, `TrySet*`, `Invoke*`, and `Invoke*<T>` members.
- `S1InteropMemberRegistry`: reflection-based get/set/invoke helpers used by the registry and facades.
- `S1InteropHarmonyPatcher`: emitted only when `[S1InteropPatch]` is used. It is internal generated infrastructure, not an author-facing `PatchAll` API. It records per-patch status so missing IL2CPP targets and Harmony failures are visible.
- `S1InteropObject<TTag>`: the generic backend-neutral handle backing every facade `Handle`.
- `S1InteropObjectCast`: object/proxy unwrapping helper referenced by `S1I007` diagnostics.
- `S1InteropUnityEventBridge` and `S1InteropDelegateEventBridge`: bridge helpers emitted only when the matching bridge attribute is present.

### `S1Interop.ScheduleOne.*` type facades

One static facade class per declared game type, preserving the original runtime namespace root under `S1Interop`. For a type declared as `ScheduleOne.Vehicles.LandVehicle`, the facade lives at:

```text
S1Interop.ScheduleOne.Vehicles.LandVehicle
```

Each facade exposes:

- `Handle`: a readonly struct wrapping the runtime instance. Exposes `HasValue`, `Instance`, `Value`, and an implicit conversion to the underlying `S1InteropObject<LandVehicleTag>`.
- `Type`: the resolved `System.Type?` for the active backend.
- `TypeName`: the runtime type name string.
- `Create(...)` and `Create<T>(...)`: object-returning constructor helpers.
- `CreateHandle(...)` and `TryCreate(out Handle, ...)`: constructor helpers that immediately wrap successful creations as the facade `Handle`.
- `Is(object?)`: instance check.
- `TryAs(object?, out Handle)` and `As(object?)`: backend-neutral wrapping.
- `Get(handle, memberName)` and `Get(instance, memberName)`: reflection get.
- `TrySet(handle, memberName, value)` and `TrySet(instance, memberName, value)`: reflection set.
- `Invoke(handle, methodName, ...)`, `Invoke(instance, methodName, ...)`, and `Invoke<T>(...)` overloads: reflection invocation.
- Named member accessors discovered from reference metadata or declared via `S1InteropMember`. Metadata-backed scalar, string, object, void method shapes, declared enum mirrors, and game-object values with declared facades get concrete signatures, including explicit member declarations when the referenced Mono and IL2CPP metadata resolve one compatible target. Read-only discovered values get getters without named `TrySet...` helpers; undeclared game wrapper types, collections, by-ref parameters, generated backing fields, ambiguous overloads, and unresolved explicit declarations stay on object/generic fallback helpers.

Generated facades are `internal` by default. The generator does not shorten namespaces: `ScheduleOne.Vehicles.LandVehicle` always becomes `S1Interop.ScheduleOne.Vehicles.LandVehicle`, never `S1Interop.Vehicles.LandVehicle`.

## How declarations map to generated output

| Declaration | What the generator emits |
| --- | --- |
| `[assembly: S1InteropNamespace(...)]` | Registry entries and a basic facade with `Handle` for every matching public type. The `Handle` only has generic members (`HasValue`, `Instance`, `Value`). No named member accessors unless `IncludeMembers = true` opts matching types into member facade discovery. |
| `[assembly: S1InteropType(...)]` | A per-type facade under `S1Interop.ScheduleOne.*` with `Handle`, `As`/`TryAs`/`Is`, constructor helpers, and discovered public member accessors. The `Handle` gains named accessors from referenced Mono and IL2CPP metadata, with concrete signatures where the metadata is backend-neutral. Enum declarations instead emit S1Interop-owned enum mirrors when backend values agree. Also emits the matching registry `Tag` and resolution entries. |
| `[assembly: S1InteropMember(...)]` | A named accessor on the owner facade and, for instance fields/properties, on `Handle` with the chosen alias. Used for private members, ambiguous overloads, pinned bindings, and migration-inferred reflection access. It uses concrete signatures when local metadata proves the member shape; otherwise it keeps object/generic fallback helpers. |
| `[S1InteropPatch(...)]` | A generated target type/member binding plus internal Harmony registrar. The target validates like an explicit method declaration when references are available, resolves through the same member registry at runtime, and is applied once by generated startup code. |
| `[assembly: S1InteropGenerateUnityEventBridge]` | `S1InteropUnityEventBridge` in `S1Interop.Generated` with `Add`/`Remove` overloads. |
| `[assembly: S1InteropGenerateDelegateEventBridge]` | `S1InteropDelegateEventBridge` in `S1Interop.Generated` with `Combine`/`Remove` helpers. |

> [!IMPORTANT]
> `S1InteropNamespace` and `S1InteropType` produce different `Handle` surfaces. A namespace-only `Handle` gives you `HasValue`, `Instance`, `Value`, and reflection-style `Get`/`TrySet`/`Invoke`. Adding `S1InteropType` for the same type triggers member discovery and adds named accessors like `vehicle.VehicleName`; scalar/string/enum members use concrete signatures when metadata is safe. The transition happens after the next build completes - see below.

## Build and IDE timing

Watch these details:

- **Attributes are generated too.** You never need to define `S1InteropTypeAttribute` or its siblings. The generator emits them on first generation via `RegisterPostInitializationOutput`. If you see "type or namespace 'S1InteropType' could not be found", the project does not reference `S1Interop.Generators`.
- **There is a delay after saving.** The IDE has to run a design-time build before IntelliSense sees new generated symbols. If symbols are missing immediately after editing, wait a moment or run `dotnet build`.
- **Namespace-only vs type-declared handles.** If you have an `S1InteropNamespace` declaration for `ScheduleOne`, every matching type gets a `Handle` with generic members only. Adding an `S1InteropType` for a specific type (like `ScheduleOne.PlayerScripts.Player`) triggers member discovery for that type. After the next build completes, the `Handle` gains named accessors, with concrete signatures where member metadata is backend-neutral. Before the build, the `Handle` still only has generic members.
- **Declarations are assembly-level.** Put them in a single generated or S1Interop-owned file such as `S1Interop.Generated/S1Interop.BackendNeutral.cs`. Keeping them in one place makes rollback and regeneration reviewable.
- **Diagnostics are quiet without game references.** `S1I001`-`S1I003` only fire when the relevant Mono or IL2CPP reference surface is available in the compilation. Docs-only or package-restore builds do not fail because local game paths are missing.
- **Source-boundary diagnostics are IL2CPP-only.** `S1I004`-`S1I007` only fire when the compilation targets IL2CPP (preprocessor symbol or runtime surface). They never fire on Mono-only builds.
- **Patch-target review warnings need references.** `S1I008` fires when referenced metadata shows an overloaded, accessor-like, operator-like, or aggressively inlined S1Interop patch target. Treat it as a review warning, not proof that the patch cannot work.

## What the generator does not do

- It cannot rewrite existing source. Roslyn source generators are additive only. Call-site transformation happens through CLI migration before compilation.
- It cannot ship game assemblies or a static catalog of Schedule One APIs. Generated member coverage comes from the Mono and IL2CPP reference assemblies already referenced by the project.
- It does not generate shortened duplicate namespaces. One canonical namespace per game type.
- It does not expose a public patching startup API. S1Interop patch declarations are applied by generated internal code so you do not accidentally double-apply patches.

For the full attribute reference, continue to [Declarations](backend-neutral-declarations.md). For patch authoring, see [Backend-neutral Harmony patching](harmony-patching.md).
