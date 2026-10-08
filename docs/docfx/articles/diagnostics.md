# Diagnostics

S1Interop turns common IL2CPP runtime failures into compile-time feedback.

Compiler projects report `S1IC` diagnostics through the pinned tool invoked by MSBuild. Start with [compiler troubleshooting](troubleshooting.md#ordinary-game-source-fails-in-the-native-build) and the [source compiler guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for those diagnostics. Installing the generator package is not a fix for unsupported compiler adaptation.

## Event repair startup failures

`S1IC040` means the generated event repair cannot validate its runtime prerequisites. A wrapper hash or module-ID mismatch requires rebuilding the mod and its compiler-built dependencies against the installed, matching game versions, then deploying their matching `S1Interop.Runtime.dll`. An unavailable IL2CPP domain or unattached loader thread means the assembly initialized outside its supported game-loader context. These are startup failures; the runtime does not silently apply a repair to an unverified image.

## Earlier generator diagnostics

The `S1I` diagnostics below are reported by the `S1Interop.Generators` Roslyn package during compilation. They do not require the CLI to run. The generator reports them alongside your normal build errors and warnings, so you see them in your IDE error list and in `dotnet build` output the same way you would see any other compiler diagnostic.

You can use these diagnostics even if you keep manual Mono and IL2CPP code. Generated facades are optional; the diagnostics are still useful as build-time guardrails.

See [Generated output](generator-package.md) for build timing and the conditions under which each diagnostic group stays quiet.

Generated helpers also keep runtime reports for cases the compiler cannot prove. If a facade method returns `null` or a `TrySet...` helper returns `false`, inspect `S1Interop.Generated.S1InteropMemberRegistry.Reports`. Those reports cover active-backend failures such as missing IL2CPP wrapper members, unresolved overload parameter types, ambiguous overloads, and argument conversion failures.

## Declaration diagnostics (S1I001-S1I003)

Declaration diagnostics fire when the build has access to Mono or IL2CPP game reference assemblies. The generator validates every `S1InteropType` and `S1InteropMember` declaration against those assemblies and reports an error when a requested type or member cannot be found.

These diagnostics **stay quiet** when no game reference surface is available in the compilation. Package-restore builds, docs-only builds, and CI environments without local game paths configured will not fail because of missing game assemblies.

| Diagnostic | Severity | Meaning |
| --- | --- | --- |
| `S1I001` | Error | A requested game type could not be found in the referenced assemblies. |
| `S1I002` | Error | A member override references an unknown owner alias; `ownerAlias` does not match any declared `S1InteropType`. |
| `S1I003` | Error | A member name or method overload signature was not found on the resolved owner type. |

### S1I001 - Type not found

You get `S1I001` when the `monoTypeName` (or the computed `Il2CppTypeName`) in an `S1InteropType` declaration does not exist in the referenced game assemblies. Common causes:

- A typo in the fully-qualified type name.
- The type was renamed or moved in a game update.
- The `Il2CppTypeName` override does not match the actual IL2CPP wrapper name.

```csharp
// This declaration will produce S1I001 if "ScheduleOne.Vehicles.Hovercraft"
// does not exist in the referenced Mono assemblies.
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.Hovercraft")]
```

**Fix:** Verify the type name against the game reference assemblies using `s1interop analyze` or a decompiler. Update the `monoTypeName` (and `Il2CppTypeName` if overridden) to match.

### S1I002 - Member owner not declared

You get `S1I002` when an `S1InteropMember` declaration's `ownerAlias` does not match the `Alias` of any `S1InteropType` in the same compilation.

```csharp
// S1I002: "Hovercraft" is not the alias of any S1InteropType.
[assembly: S1Interop.S1InteropMember("Hovercraft", "Speed")]
```

**Fix:** Make sure the `ownerAlias` exactly matches the `Alias` property (not the runtime type name) of a declared `S1InteropType`.

### S1I003 - Member not found

You get `S1I003` when the `memberName` in an `S1InteropMember` declaration, combined with any `ParameterTypeNames`, does not resolve to a member on the owner type in the referenced assemblies. Common causes:

- A typo in the member name.
- The wrong `Kind` is set (for example, `FieldOrProperty` when the target is a method).
- `IsStatic` does not match the target's static or instance shape.
- `ParameterTypeNames` specifies a signature that does not match any overload.

**Fix:** Check the member name, kind, static shape, and signature against the game reference assemblies. Set `Kind = S1InteropMemberKind.Method` and supply the correct `ParameterTypeNames` when resolving an overloaded method.

## IL2CPP boundary diagnostics (S1I004-S1I007)

IL2CPP boundary diagnostics fire only when the compilation targets IL2CPP, detected by the `IL2CPP` preprocessor symbol or IL2CPP reference assemblies. They **never fire on Mono-only builds**.

These diagnostics catch source shapes that compile successfully but fail at runtime inside an IL2CPP game. They are intentionally narrow: normal managed collections and arrays inside ordinary mod logic are fine. The checks only trigger at the specific boundary crossing patterns described below.

> [!TIP]
> To trigger S1I004-S1I007 locally, build the explicit `"Debug Il2Cpp"` configuration. In the experimental single-assembly scaffold, use `dotnet build -p:S1InteropReferenceRuntime=Il2Cpp -p:S1InteropTargetRuntime=Il2Cpp`; its normal Mono-reference build will not report these diagnostics.

| Diagnostic | Severity | Meaning |
| --- | --- | --- |
| `S1I004` | Error | Harmony transpiler usage in IL2CPP-facing code. |
| `S1I005` | Error | Managed collection parameters in IL2CPP-facing callback signatures. |
| `S1I006` | Error | Managed `byte[]` buffer passed to a native/game fill API. |
| `S1I007` | Warning | Plain C# cast from an `object` or IL2CPP proxy value to a Unity object type. |

### S1I004 - Harmony transpiler not IL2CPP portable

Harmony transpilers (`IEnumerable<CodeInstruction>` methods with `[HarmonyTranspiler]` or names containing `Transpiler`) cannot be used in IL2CPP builds. IL2CPP does not preserve the IL instruction stream that transpilers manipulate.

```csharp
// S1I004: transpilers are not supported under IL2CPP.
[HarmonyTranspiler]
static IEnumerable<CodeInstruction> MyTranspiler(IEnumerable<CodeInstruction> instructions)
{
    // ...
}
```

**Fix:** Replace the transpiler with prefix, postfix, or finalizer patches, or use runtime-specific patch registration to skip the transpiler on IL2CPP builds.

### S1I005 - Managed collection in IL2CPP-facing signature

Managed collection types (`List<T>`, `Dictionary<K,V>`, `HashSet<T>`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IDictionary<K,V>`) cannot cross the IL2CPP interop boundary in callback signatures. The types that trigger this diagnostic are those in Harmony patch methods, methods on types decorated with `[RegisterTypeInIl2Cpp]`, and methods with Harmony `__`-prefixed parameters.

**Fix:** Use the corresponding `Il2CppSystem.Collections.Generic.*` wrapper type at the boundary, or use a generated S1Interop facade boundary that handles the conversion.

### S1I006 - Managed byte buffer at IL2CPP boundary

Passing a managed `byte[]` directly to an IL2CPP or native buffer-fill API (such as `SteamNetworking.ReadP2PPacket`) does not work under IL2CPP. The native side expects an `Il2CppStructArray<byte>`.

```csharp
// S1I006: managed byte[] cannot be passed to an IL2CPP buffer-fill API.
byte[] buffer = new byte[1024];
SteamNetworking.ReadP2PPacket(buffer, ...);
```

**Fix:** Use `S1Interop.Generated.S1InteropSteamNetworking` for Steam P2P reads and sends, or use `Il2CppStructArray<byte>` at the IL2CPP boundary and copy the data into a managed `byte[]` after the call if you need managed access to the result.

### S1I007 - Plain C# cast across IL2CPP object boundary

A plain C# cast (`as` expression or `is` pattern) from an `object` or `Il2CppObjectBase` value to a Unity object type (`UnityEngine.Object`, `Component`, `MonoBehaviour`, `UniversalRenderPipelineAsset`) does not reliably unwrap IL2CPP proxy wrappers. The cast may silently return `null` or throw at runtime under IL2CPP.

```csharp
// S1I007: direct cast from object may not unwrap the IL2CPP proxy correctly.
object rawValue = GetSomeValue();
var component = rawValue as MyMonoBehaviour;

// Fix in a dual-runtime project (with `using S1Interop;`): same call on Mono and IL2CPP.
var component = rawValue.TryCast<MyMonoBehaviour>();

// Fix in a backend-neutral single-assembly project.
var component = S1Interop.Generated.S1InteropObjectCast.As<MyMonoBehaviour>(rawValue);
```

**Fix:** In a dual-runtime project, replace the plain cast with `value.TryCast<T>()`, `value.Cast<T>()`, or `value.Is(out T result)` from the [runtime helpers](dual-runtime-code.md#cast-game-objects). In a backend-neutral project, use `S1Interop.Generated.S1InteropObjectCast.As<T>(value)`, which picks the unwrapping strategy at runtime.

> [!NOTE]
> `S1I007` is a **warning**, not an error. The cast may work in many cases; the diagnostic flags it because silent failure under IL2CPP is common enough to warrant review at every occurrence.

## Patch target diagnostics (S1I008)

Patch target diagnostics fire when a backend-neutral `S1InteropPatch` target resolves to a method shape that needs IL2CPP review.

| Diagnostic | Severity | Meaning |
| --- | --- | --- |
| `S1I008` | Warning | A backend-neutral patch target is overloaded without `ParameterTypeNames`, accessor-like, operator-like, or marked for aggressive inlining/optimization. |

### S1I008 - Patch target needs IL2CPP review

You get `S1I008` when S1Interop can resolve the patch target but the declaration is likely to be brittle on IL2CPP. Common causes:

- the target method has overloads and the patch declaration omitted `ParameterTypeNames`;
- the target is a property accessor such as `get_Health` or `set_Health`;
- the target is an event accessor or operator method;
- the target method is marked with `MethodImplOptions.AggressiveInlining` or `AggressiveOptimization`.

```csharp
// S1I008: overloaded patch target should declare ParameterTypeNames.
[S1InteropPatch(
    "ScheduleOne.NPCs.Behaviour.MoveItemBehaviour",
    "StartMoving")]
internal static class MoveItemPatch
{
    [S1InteropPrefix]
    private static void Prefix()
    {
    }
}
```

**Fix:** Add `ParameterTypeNames` for overloaded targets. For accessor-like or aggressively inlined targets, prefer a higher-level method that still runs on IL2CPP. If you intentionally keep the target, validate that the handler fires on the actual IL2CPP branch you support.

## Generated code diagnostics (S1I009-S1I010)

| Diagnostic | Severity | Meaning |
| --- | --- | --- |
| `S1I009` | Warning | An IL2CPP build has a `[RegisterTypeInIl2Cpp]` class without an `IntPtr` constructor that S1Interop cannot generate. |
| `S1I010` | Warning | The project compiles below C# 9, so S1Interop reports diagnostics but generates no code. |

### S1I009 - Injected type needs an IntPtr constructor

Il2CppInterop creates a managed wrapper for each native instance of an injected class through its `IntPtr` constructor. Without one, registration or wrapping fails when the game runs. S1Interop generates the constructor for IL2CPP builds when it can extend the class:

```csharp
// S1I009 on IL2CPP builds: the class is not partial.
[RegisterTypeInIl2Cpp]
public class Spinner : MonoBehaviour { }

// Fix: S1Interop generates `Spinner(IntPtr)` for IL2CPP builds only.
[RegisterTypeInIl2Cpp]
public partial class Spinner : MonoBehaviour { }
```

**Fix:** Mark the class and every class that contains it `partial`, or write the constructor yourself inside `#if IL2CPP`. S1Interop also reports this when the base class has no accessible `IntPtr` constructor.

### S1I010 - Generated code needs C# 9

A `netstandard2.1` project defaults to C# 8. S1Interop's generated helpers, registries, and facades need C# 9, so the generator skips them and keeps reporting the other diagnostics.

**Fix:** Add `<LangVersion>latest</LangVersion>` (or `10.0` or later, which `S1InteropUsing` needs) to the project. Ignore the warning if you only want diagnostics.

## Build errors (S1I101-S1I107)

The `S1Interop.Generators` build targets report these before compilation when [build integration](dual-runtime-code.md#enable-in-an-existing-project) is enabled.

| Code | Severity | Meaning |
| --- | --- | --- |
| `S1I101` | Error | `S1InteropGameReferences` is on, but the configuration does not set `S1InteropTargetRuntime`. |
| `S1I102` | Error | No game install is configured for the runtime. Run `s1interop setup . --apply`. |
| `S1I103` | Error | The configured install has no MelonLoader. |
| `S1I104` | Error | The Mono install has no `Schedule I_Data\Managed\Assembly-CSharp.dll`. |
| `S1I105` | Error | The IL2CPP install has no generated `MelonLoader\Il2CppAssemblies`. Launch it once with MelonLoader. |
| `S1I106` | Warning | `S1InteropDeployToGame` is on, but no install is configured. |
| `S1I107` | Warning | The built DLL could not be copied to `Mods`, usually because the game is running. The build output is still valid. |

[Local game paths](local-paths.md) explains which folder each property needs.

## Related pages

- [Legacy troubleshooting](legacy-troubleshooting.md)
- [Declarations](backend-neutral-declarations.md)
- [Generated output](generator-package.md)
