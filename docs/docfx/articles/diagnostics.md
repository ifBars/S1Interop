# Diagnostics

S1Interop turns common IL2CPP runtime failures into compile-time feedback.

All diagnostics on this page are reported by the `S1Interop.Generators` Roslyn package during compilation. They do not require the CLI to run. The generator reports them alongside your normal build errors and warnings, so you see them in your IDE error list and in `dotnet build` output the same way you would see any other compiler diagnostic.

See [Generated output](generator-package.md) for build timing and the conditions under which each diagnostic group stays quiet.

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
- `ParameterTypeNames` specifies a signature that does not match any overload.

**Fix:** Check the member name and signature against the game reference assemblies. Set `Kind = S1InteropMemberKind.Method` and supply the correct `ParameterTypeNames` when resolving an overloaded method.

## IL2CPP boundary diagnostics (S1I004-S1I007)

IL2CPP boundary diagnostics fire only when the compilation targets IL2CPP, detected by the `IL2CPP` preprocessor symbol or IL2CPP reference assemblies. They **never fire on Mono-only builds**.

These diagnostics catch source shapes that compile successfully but fail at runtime inside an IL2CPP game. They are intentionally narrow: normal managed collections and arrays inside ordinary mod logic are fine. The checks only trigger at the specific boundary crossing patterns described below.

> [!TIP]
> To trigger S1I004-S1I007 locally, build under the IL2CPP configuration (for example, `Debug Il2Cpp` or `Release Il2Cpp`). Mono-only builds will never report these diagnostics.

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

**Fix:** Use `Il2CppStructArray<byte>` at the IL2CPP boundary and copy the data into a managed `byte[]` after the call if you need managed access to the result.

### S1I007 - Plain C# cast across IL2CPP object boundary

A plain C# cast (`as` expression or `is` pattern) from an `object` or `Il2CppObjectBase` value to a Unity object type (`UnityEngine.Object`, `Component`, `MonoBehaviour`, `UniversalRenderPipelineAsset`) does not reliably unwrap IL2CPP proxy wrappers. The cast may silently return `null` or throw at runtime under IL2CPP.

```csharp
// S1I007: direct cast from object may not unwrap the IL2CPP proxy correctly.
object rawValue = GetSomeValue();
var component = rawValue as MyMonoBehaviour;

// Fix: route through S1InteropObjectCast for backend-neutral unwrapping.
var component = S1Interop.Generated.S1InteropObjectCast.As<MyMonoBehaviour>(rawValue);
```

**Fix:** Replace the plain cast with `S1Interop.Generated.S1InteropObjectCast.As<T>(value)`. This helper applies the correct unwrapping strategy for the active backend, including IL2CPP proxy handling.

> [!NOTE]
> `S1I007` is a **warning**, not an error. The cast may work in many cases; the diagnostic flags it because silent failure under IL2CPP is common enough to warrant review at every occurrence.

## Related pages

- [Troubleshooting](troubleshooting.md)
- [Declarations](backend-neutral-declarations.md)
- [Generated output](generator-package.md)
