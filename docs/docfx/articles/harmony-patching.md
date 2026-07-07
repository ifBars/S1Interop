# Backend-neutral Harmony patching

Use S1Interop patch attributes when a mod needs to patch a Schedule One method without choosing `ScheduleOne.*` on Mono and `Il2CppScheduleOne.*` on IL2CPP in source.

The patch still lands on the real native method. S1Interop only owns target lookup: it reads the Mono type and method name from your attribute, maps that target to the active backend, resolves the `MethodInfo`, and applies the Harmony patch through generated code.

## Basic patch

Your project still needs Harmony. Normal MelonLoader mod projects already have it through `0Harmony.dll`; S1Interop only removes backend-specific target lookup from your source.

Write the target as the Mono game type name. Mark the handler methods with S1Interop's patch-role attributes.

```csharp
using S1Interop;

[S1InteropPatch(
    "ScheduleOne.NPCs.Behaviour.MoveItemBehaviour",
    "IsDestinationValid",
    ParameterTypeNames = new[]
    {
        "ScheduleOne.Management.TransitRoute",
        "ScheduleOne.ItemFramework.ItemInstance",
        "string&"
    },
    Required = true)]
internal static class MoveItemDestinationPatch
{
    [S1InteropPrefix]
    private static bool Prefix(object? __instance, ref string invalidReason)
    {
        return true;
    }

    [S1InteropPostfix]
    private static void Postfix(object? __instance)
    {
    }
}
```

The `string&` entry is the by-ref marker for `ref string` or `out string`. For overloaded methods, use fully qualified Mono parameter type names. S1Interop converts those names to the IL2CPP wrapper names when the mod runs on IL2CPP.

Use `Required = true` for patches your mod cannot run without. Required patches throw during generated startup if the target cannot be resolved, no handler can be found, or Harmony rejects the patch.

## Do not call PatchAll

Do not call `PatchAll` for S1Interop patch attributes.

When a project contains `[S1InteropPatch]`, the generator emits an internal patch registrar and a module initializer. That initializer applies S1Interop patches once when the mod assembly loads.

Calling `PatchAll` yourself can double-apply handlers. With S1Interop, the API is the attributes. The registrar is implementation detail.

If your mod also has ordinary `[HarmonyPatch]` or MelonLoader patch attributes, keep treating those as ordinary Harmony/MelonLoader patches. S1Interop patch attributes are only for backend-neutral Schedule One target resolution.

## Runtime status

The generated registrar records one status report per S1Interop patch in `S1Interop.Generated.S1InteropHarmonyPatcher.Reports`:

| Status | Meaning |
| --- | --- |
| `Applied` | The target resolved and Harmony accepted the patch. |
| `SkippedMissingTarget` | The target method could not be resolved for the active backend. Check the method name, overload parameters, IL2CPP wrapper name, and local references. |
| `SkippedMissingHandler` | The patch class did not resolve a marked prefix, postfix, or finalizer handler. |
| `PatchFailed` | The target resolved, but Harmony or the active backend rejected the patch. |

Missing and failed optional patches are logged. Required patches throw after recording the report.

`Applied` only means the method body was patched. It does not prove the game still calls that method on IL2CPP. If the method is tiny, accessor-like, or only used from hot paths, IL2CPP may inline callers so your handler never runs. Patch a higher-level method when possible and validate on the actual IL2CPP branch you support.

## Attribute reference

`S1InteropPatch` goes on the patch class.

| Property | Meaning |
| --- | --- |
| `monoTypeName` | Required positional Mono runtime type name, such as `ScheduleOne.PlayerScripts.Player`. |
| `methodName` | Required positional method name to patch. |
| `ParameterTypeNames` | Optional Mono parameter type names for overload resolution. Add `&` for by-ref parameters. |
| `Il2CppTypeName` | Optional override when the default `ScheduleOne.*` to `Il2CppScheduleOne.*` mapping is wrong. |
| `OwnerAlias` | Optional generated owner alias. Use only when you need predictable registry names. |
| `MethodAlias` | Optional generated method alias. Use only when you need predictable registry names. |
| `IsStatic` | Optional hint for generated invocation helpers. Target method lookup does not require it. |
| `Required` | Throw during generated startup when the patch cannot be applied. Use for patches the mod cannot safely run without. |

Patch handler attributes go on methods inside the patch class:

| Attribute | Harmony role |
| --- | --- |
| `S1InteropPrefix` | Prefix handler. |
| `S1InteropPostfix` | Postfix handler. |
| `S1InteropFinalizer` | Finalizer handler. |

S1Interop does not expose a backend-neutral transpiler attribute. Transpilers are tied to Mono-only IL assumptions too easily. Keep them runtime-specific until S1Interop has a safer abstraction.

## What gets generated

A patch declaration also registers the target owner and method as generated interop metadata. When Mono or IL2CPP references are available during compilation, missing target types or methods report `S1I001` or `S1I003`.

At runtime, the generated patcher resolves:

```csharp
S1Interop.Generated.S1InteropMemberRegistry.MoveItemBehaviourIsDestinationValidMethod
```

or the equivalent alias for your patch target, then calls Harmony with generated `HarmonyMethod` objects for the marked handlers.

The same overload-resolution path is used by:

- migrated cached `MethodInfo` bindings;
- explicit `S1InteropMember` method declarations;
- backend-neutral S1Interop patch attributes.

There is no second patch-only resolver.

## When to use this

Use S1Interop patch attributes when the patch target is the portability problem:

- a direct patch mod has separate Mono and IL2CPP `typeof(...)` targets;
- an S1API mod uses S1API for content but still patches a vanilla Schedule One method directly;
- a hybrid mod has cached `AccessTools.Method(...)` calls next to normal gameplay code;
- a method overload is stable, but its declaring type lives under different Mono and IL2CPP wrapper namespaces.

Keep using S1API for the gameplay workflow it owns. This feature is for patch targets S1API does not need to abstract.
