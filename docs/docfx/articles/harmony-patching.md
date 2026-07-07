# Backend-neutral Harmony patching

S1Interop patch attributes are for mod code that wants to patch a Schedule One method without choosing `ScheduleOne.*` on Mono and `Il2CppScheduleOne.*` on IL2CPP in source.

The patch still lands on the real native method. S1Interop only owns the target lookup: it reads the Mono type and method name from your attribute, maps that target to the active backend, resolves the `MethodInfo`, and applies the Harmony patch through generated code.

## Basic patch

Your project still needs a Harmony reference. Normal MelonLoader mod projects already have one through `0Harmony.dll`; S1Interop only removes the backend-specific target lookup from your source.

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
    })]
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

## Do not call PatchAll

Do not call `PatchAll` for S1Interop patch attributes.

When a project contains `[S1InteropPatch]`, the generator emits an internal `S1Interop.Generated.S1InteropHarmonyPatcher` and a module initializer. That generated initializer applies S1Interop patches once when the mod assembly loads. It also has an internal guard so an accidental second internal apply call does not patch the same handlers twice.

This avoids a common MelonLoader/Harmony mistake: writing patch attributes, then also calling `PatchAll` during startup and ending up with every patch running twice. With S1Interop, your API is the attributes. The generated registrar is implementation detail.

If your mod also has ordinary `[HarmonyPatch]` or MelonLoader patch attributes, keep treating those as ordinary Harmony/MelonLoader patches. S1Interop patch attributes are only for backend-neutral Schedule One target resolution.

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

Patch handler attributes go on methods inside the patch class:

| Attribute | Harmony role |
| --- | --- |
| `S1InteropPrefix` | Prefix handler. |
| `S1InteropPostfix` | Postfix handler. |
| `S1InteropFinalizer` | Finalizer handler. |

S1Interop does not expose a backend-neutral transpiler attribute. Harmony transpilers are powerful, but they are also one of the easiest places to accidentally write Mono-only IL assumptions into an IL2CPP mod. Keep transpilers as explicit runtime-specific work until the project has a safer abstraction for them.

## What gets generated

A patch declaration also registers the target owner and method as generated interop metadata. The generated patcher resolves:

```csharp
S1Interop.Generated.S1InteropMemberRegistry.MoveItemBehaviourIsDestinationValidMethod
```

or the equivalent alias for your patch target, then calls Harmony with generated `HarmonyMethod` objects for the marked handlers.

That means the same overload-resolution path is used by:

- migrated cached `MethodInfo` bindings;
- explicit `S1InteropMember` method declarations;
- backend-neutral S1Interop patch attributes.

There is no second patch-only resolver to keep in sync.

## When to use this

Use S1Interop patch attributes when the patch target itself is the portability problem:

- a direct patch mod has separate Mono and IL2CPP `typeof(...)` targets;
- an S1API mod uses S1API for content but still patches a vanilla Schedule One method directly;
- a hybrid mod has cached `AccessTools.Method(...)` calls next to normal gameplay code;
- a method overload is stable, but its declaring type lives under different Mono and IL2CPP wrapper namespaces.

Keep using S1API for the gameplay workflow it owns. This feature is for the lower-level patch target seam that S1API does not need to abstract.
