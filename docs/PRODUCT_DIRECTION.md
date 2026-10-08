# Product Direction

S1Interop aims to let mod authors write ordinary game C# once and build it for Mono and IL2CPP without maintaining wrappers or runtime conditionals. The source compiler is the primary authoring workflow. The current implementation is experimental; known unsupported behavior prevents a claim of unrestricted compatibility.

## Primary developer experience

- One `s1interop` command creates projects, configures local installations, and hosts the compiler operations invoked by MSBuild.
- Plain `new` creates a compiler project with ordinary `ScheduleOne.*` source, a pinned local tool, and matching build imports.
- Authors restore that tool, configure matching game installations, and build separate Mono and IL2CPP outputs from the same source.
- Builds do not deploy or launch the game. Mod authors validate each runtime's actual behavior separately.
- Existing projects can be evaluated in controlled copies with explicit source and dependency inputs. Automatic adoption of arbitrary projects is still unfinished.

Follow [the first-mod walkthrough](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/first-mod.md) and [existing-mod compiler evaluation](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/compiler-adoption.md) for the implemented path. The alpha.2 candidate is unpublished; published alpha.1 retains the earlier generator workflow.

## Architecture and coverage

Compiler adaptation comes from authoring and target metadata plus reusable language/runtime transformations. It must not become a manually maintained catalog of gameplay APIs. Higher-level APIs can own domain abstractions independently.

The compiler must preserve observable behavior across supported boundaries: identity, aliases, mutations, exceptions, lifetimes, and serialization. A transformation that compiles while losing those properties is insufficient. Unsupported constructs need actionable diagnostics and a reproducible case that can drive broader coverage.

Use unchanged real mods and separately compiled libraries as inputs. Keep author compilation, target compilation, contract execution, live game checks, and gameplay/multiplayer validation distinct. Passing a selected corpus does not establish full-game coverage or eliminate future compiler/runtime maintenance. Stripped native implementations and AOT limitations require explicit investigation rather than claims based on available metadata alone.

See [the source compiler guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for current support and limits. The delivery bar includes isolated package installation, project setup, both builds, and matching live evidence for runtime changes.

## Existing workflows

`analyze`, `lint`, migration previews, and reversible migration remain available for existing projects. They do not enable the source compiler automatically. `new --legacy-generator` creates the earlier helper-based project, and `new --backend-neutral` creates the opt-in facade experiment. Neither is the default compiler authoring model.

The facade design below documents that separate experiment. It does not define the compiler's public surface or require compiler users to adopt handles, declarations, or conditional code.

## Earlier facade design

### Target Experience

Today, backend-neutral code can use generated type handles:

```csharp
S1Interop.ScheduleOne.Vehicles.LandVehicle.Handle vehicle =
    S1Interop.ScheduleOne.Vehicles.LandVehicle.As(rawVehicle);

string? name = vehicle.VehicleName;
float? throttle = vehicle.CurrentThrottle;
S1Interop.ScheduleOne.PlayerScripts.Player.Handle driver = vehicle.AssignedDriver;
```

The generated SDK should preserve the original runtime namespace root under `S1Interop`:

```csharp
using S1Interop.ScheduleOne.Vehicles;
using S1Interop.ScheduleOne.PlayerScripts;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);
LandVehicle.Handle created = LandVehicle.CreateHandle();

string? name = vehicle.VehicleName;
float? throttle = vehicle.CurrentThrottle;
Player.Handle driver = vehicle.AssignedDriver;
```

or, when a static facade is the safer shape:

```csharp
using S1Interop.ScheduleOne.Vehicles;
using S1Interop.ScheduleOne.PlayerScripts;

var vehicle = LandVehicle.As(rawVehicle);
LandVehicle.Handle created = LandVehicle.CreateHandle();

string? name = LandVehicle.GetVehicleName(vehicle);
float? throttle = LandVehicle.GetCurrentThrottle(vehicle);
Player.Handle driver = LandVehicle.GetAssignedDriver(vehicle);
```

`S1InteropMemberRegistry` can remain the low-level generated layer, but it should not be the normal mod-authoring API. Do not emit both shortened and root-preserving namespaces for the same game type. Schedule One facades belong under `S1Interop.ScheduleOne.*`; future supported surfaces should preserve their own roots, such as `S1Interop.FishNet.Runtime.*`.

### Type-First SDK Generation

`S1InteropNamespace` should cover broad type registration without forcing developers to emit thousands of per-type attributes:

```csharp
[assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]
```

Namespace imports are type-only by default. Use `IncludeMembers = true` only for narrow namespaces where the extra generated member surface is intentional.

`S1InteropType` should mean "generate the backend-neutral facade for this game type." It should not require developers to manually declare every ordinary public member.

For example, this declaration:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

now starts generating:

- Mono and IL2CPP runtime type resolution.
- A typed backend-neutral handle or wrapper.
- `As`, `TryAs`, and `Is` helpers for object/proxy conversion.
- Accessors for compatible public fields and properties, including inherited members when both runtime hierarchies agree.
- Invokers for unambiguous compatible public methods.

It should continue toward:

- Broader method and constructor coverage where overload and conversion rules are explicit enough.
- Backend-specific conversions for common wrapper differences such as arrays, `Il2CppSystem.Guid`, and IL2CPP collection types.
- Diagnostics for missing, ambiguous, or incompatible members across Mono and IL2CPP.
- Extending the same root-preserving facade rule to additional supported surfaces when needed, such as FishNet, Unity, or other common modding dependencies.
- Treating lower-level registry names as generated implementation details in more migration rewrites and examples.

The generated member surface should come from local reference metadata. Do not commit game assemblies, generated IL2CPP wrappers, decompiled source, or a static hand-maintained catalog of Schedule One APIs.

### Member Declarations Are Overrides

`S1InteropMember` should become the exception path, not the main workflow.

Use explicit member declarations when:

- The member is private, internal, renamed, or otherwise outside the default public SDK surface.
- A better alias is needed for readability.
- An overload needs explicit parameter names or by-ref markers.
- Mono and IL2CPP surfaces disagree and the developer wants to pin a specific binding.
- Migration inferred a reflection pattern that cannot be represented by the automatic type facade yet.

Normal public fields, properties, and unambiguous public methods should come from the generated type facade after a type is included. That includes inherited public members when the Mono and IL2CPP hierarchies expose the same compatible shape. Explicit declarations remain the safer alpha path for aliases, private members, pinned bindings, and ambiguous overloads, but they should still be enriched from metadata whenever one compatible member can be identified. The escape hatch should not permanently downgrade a mod back to object-only helpers.

### Experimental SDK Generation Modes

The opt-in SDK experiment supports two generation entry points:

- `sdkgen --apply`: infer the narrow SDK a mod needs from source usage, aliases, string-held type names, and local reference metadata.
- `sdkgen --full-sdk --apply`: seed a blank or exploratory project with all discoverable Schedule One type facades from local reference metadata.

`new --backend-neutral` creates the experimental single-assembly project shape. Plain `new` creates the compiler starter described above. All facade-generation paths should produce the same style of facade, but developers must be able to return to the explicit dual-runtime shape when metadata or runtime behavior diverges.

### CLI Shape

For new mods:

```batch
s1interop new .\MyMod --legacy-generator --apply
cd .\MyMod
s1interop setup . --apply
dotnet run -c "Debug Il2Cpp"
dotnet run -c "Debug Mono"
```

`dotnet run` builds, deploys the DLL into that install's `Mods` folder, and starts the game.

For existing mods:

```batch
s1interop analyze .
s1interop sdkgen . --apply
s1interop migrate . --dual-runtime --dry-run
```

For an explicit backend-neutral experiment:

```batch
s1interop new .\MyExperiment --backend-neutral --apply
s1interop sdkgen .\MyExperiment --full-sdk --apply
```

`sdkgen --full-sdk` is the blank-project seeding path. It should generate facades for all discoverable Schedule One types from local reference metadata. Usage-driven `sdkgen` should generate only the types and members a project appears to use.

`migrate` should first produce reviewable plans and safe transformations. Generated SDK convergence is an optional migration outcome. When S1Interop cannot safely rewrite a runtime-specific call, it should leave a focused report or explicit conditional fallback instead of guessing.

### Non-Goals

- Do not hide every runtime difference behind fragile reflection guesses.
- Do not generate broad aliases that make the developer forget whether a value is backend-neutral or native.
- Do not make `S1InteropMemberRegistry` the public-facing API shape.
- Do not require manual `S1InteropMember` declarations for common public type members once the type facade generator can discover them.
- Do not redistribute proprietary game artifacts.
