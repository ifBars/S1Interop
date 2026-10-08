---
title: Write code for both runtimes
description: Write one source file that builds for Mono and IL2CPP without #if blocks around imports, casts, collections, events, or injected types.
uid: s1interop.dual-runtime-code
---

# Write code for both runtimes

A dual-runtime project builds the same source twice: once against Mono's `ScheduleOne.*` assemblies and once against MelonLoader's generated `Il2CppScheduleOne.*` wrappers. Most differences between the two are mechanical. S1Interop handles those at build time, so a typical file needs no `#if MONO` / `#if IL2CPP` blocks.

Projects created with `s1interop new` have everything on this page enabled. To add it to an existing project, see [Enable in an existing project](#enable-in-an-existing-project).

## Import game namespaces once

IL2CPP builds prefix every game namespace with `Il2Cpp`, so dual-runtime files usually start with a mirrored block like this:

```csharp
#if MONO
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
#else
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
#endif
```

List the namespaces in the `.csproj` instead:

```xml
<ItemGroup>
  <S1InteropUsing Include="ScheduleOne.NPCs" />
  <S1InteropUsing Include="ScheduleOne.PlayerScripts" />
</ItemGroup>
```

Every file then sees `NPCManager`, `NPC`, and `Player` on both runtimes. Mono builds import the names as written; IL2CPP builds import them with the `Il2Cpp` prefix. The same rule covers `FishNet` and other game assemblies that MelonLoader prefixes. Unity and BCL namespaces such as `UnityEngine.UI` keep their names on both runtimes, so import them with a normal `using` or `<Using Include="UnityEngine.UI" />`.

Because these are global imports, two namespaces that define the same type name make that name ambiguous everywhere. Give one of them an alias:

```xml
<!-- ScheduleOne.Console would collide with System.Console. -->
<S1InteropUsing Include="ScheduleOne.Console" Alias="GameConsole" />
```

```csharp
public sealed partial class HelloCommand : GameConsole.ConsoleCommand
```

An alias can also name a closed generic type. The prefix applies to the whole name, so `System.Collections.Generic.List<string>` becomes `Il2CppSystem.Collections.Generic.List<string>`:

```xml
<S1InteropUsing Include="System.Collections.Generic.List&lt;string&gt;" Alias="NativeStringList" />
```

```csharp
public override void Execute(NativeStringList args)
```

Only the outer name is prefixed. Type arguments must already be the same on both runtimes, such as `string`, primitives, or Unity types.

Global imports need C# 10 or later. The scaffold sets `<LangVersion>10.0</LangVersion>`.

## Cast game objects

On IL2CPP, a value is typed by the wrapper it was returned as, not by the native object's real class. `npc is Employee` and `npc as Employee` therefore fail for an employee returned as an `NPC`. Il2CppInterop's `TryCast<T>()` and `Cast<T>()` check the native class. With `using S1Interop;`, the same calls also compile on Mono:

```csharp
Employee? employee = npc.TryCast<Employee>();   // null when it is not an employee
Employee known = npc.Cast<Employee>();          // throws InvalidCastException instead

if (component.Is(out Employee? match))
{
    // ...
}
```

On Mono these are ordinary casts. On IL2CPP, `T` must be a game or Unity type, as with Il2CppInterop's own `TryCast`. The `S1I007` diagnostic flags casts that should use these calls.

## Work with game collections

IL2CPP game lists are `Il2CppSystem.Collections.Generic.List<T>`, which LINQ cannot query. Use the same calls on both runtimes:

```csharp
// Query a game list.
var names = NPCManager.NPCRegistry.AsEnumerable().Select(npc => npc.FirstName);

// Take a managed copy, for example before modifying the game list in a loop.
List<NPC> snapshot = NPCManager.NPCRegistry.ToManagedList();

// Build the list type a game method expects.
NativeStringList args = new List<string> { "world" }.ToNativeList();
```

| Call | Mono | IL2CPP |
| --- | --- | --- |
| `list.AsEnumerable()` | LINQ's `AsEnumerable` | Enumerates the IL2CPP list |
| `list.ToManagedList()` | Copies into `List<T>` | Copies into `List<T>` |
| `items.ToNativeList()` | `List<T>` | `Il2CppSystem.Collections.Generic.List<T>` |
| `dictionary.AsEnumerable()` | LINQ's `AsEnumerable` | Managed `KeyValuePair` sequence |
| `dictionary.ToManagedDictionary()` | Copies into `Dictionary<TKey, TValue>` | Copies into `Dictionary<TKey, TValue>` |

IL2CPP arrays already implement `IEnumerable<T>`, so LINQ works on them directly. IL2CPP collection interfaces are wrapper classes, so a game API that takes `Il2CppSystem.Collections.Generic.IEnumerable<T>` still needs an IL2CPP-specific call.

## Add UnityEvent listeners

IL2CPP's `UnityAction` is a wrapper class, so a lambda cannot convert to it and `button.onClick.AddListener(() => ...)` normally fails there. With `using S1Interop;` the same line compiles on both runtimes:

```csharp
button.onClick.AddListener(() => LoggerInstance.Msg("Clicked"));
slider.onValueChanged.AddListener(value => LoggerInstance.Msg($"Volume {value}"));
```

Removing a listener on IL2CPP needs the exact native delegate that was added; converting the same lambda or `Action` again creates a different one. When you need to remove a listener, keep the handle that `Subscribe` returns:

```csharp
IDisposable clickHandler = button.onClick.Subscribe(OnClicked);
// Later:
clickHandler.Dispose();
```

## Subscribe to game delegates

Game events such as `Player.onLocalPlayerSpawned` are `System.Action` on Mono and `Il2CppSystem.Action` on IL2CPP. Alias the delegate type, convert the handler once, and add and remove that value:

```xml
<S1InteropUsing Include="System.Action" Alias="GameAction" />
```

```csharp
private static readonly GameAction OnSpawned = (Action)Spawned;

public static void Enable() => Player.onLocalPlayerSpawned += OnSpawned;
public static void Disable() => Player.onLocalPlayerSpawned -= OnSpawned;

private static void Spawned() => MelonLogger.Msg($"Local player: {Player.Local?.name}");
```

## Register your own MonoBehaviours

IL2CPP builds need an `IntPtr` constructor on every class MelonLoader injects with `[RegisterTypeInIl2Cpp]`. Mono builds must not require it. Mark the class `partial`, and S1Interop generates the constructor for IL2CPP builds only:

```csharp
[RegisterTypeInIl2Cpp]
public partial class Spinner : MonoBehaviour
{
    private void Update() => transform.Rotate(0f, 90f * Time.deltaTime, 0f);
}
```

Injected classes that are not Unity components, such as console commands, also get a parameterless constructor on IL2CPP, so `new HelloCommand()` compiles on both runtimes. A non-partial injected class without an `IntPtr` constructor reports `S1I009` in IL2CPP builds.

## Keep each DLL on its runtime

A melon whose build sets `S1InteropTargetRuntime` gets a `MelonPlatformDomain` attribute for that runtime. MelonLoader then declines to load a DLL built for the other runtime instead of failing later with missing-type errors. Declare `MelonPlatformDomain` yourself, or set `S1InteropEmitPlatformDomain` to `false`, to opt out.

## When you still need #if

Use `#if MONO` / `#if IL2CPP` where the two builds really differ:

- Members that are private on Mono but public on IL2CPP, or that exist on only one runtime.
- Harmony transpilers, which cannot target IL2CPP methods (`S1I004`).
- Game APIs that take IL2CPP collection interfaces or byte buffers (`S1I005`, `S1I006`).
- Game API differences between the two Steam branches when they are on different game versions.

Building both configurations is what catches these. A Mono build proves nothing about IL2CPP.

## Enable in an existing project

These features come from the `S1Interop.Generators` package. Each one is opt-in, so adding the package for diagnostics alone does not change references, imports, or output.

| Setting | Effect |
| --- | --- |
| `<S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>` or `Il2Cpp` | Selects the runtime for one configuration. Required by the settings below. |
| `<S1InteropGameReferences>true</S1InteropGameReferences>` | References MelonLoader and the game and Unity assemblies for that runtime, and stops the build early with setup guidance when the install is incomplete. Also makes `dotnet run` start the game. |
| `<S1InteropDeployToGame>true</S1InteropDeployToGame>` | Copies the DLL and its `.pdb` into the install's `Mods` folder after each build. |
| `<S1InteropUsing Include="..." />` | Imports a game namespace with the runtime's prefix. |
| `<Using Include="S1Interop" />` | Imports the casting, collection, and UnityEvent helpers. |

The install comes from `S1InteropGamePath`, then `GamePath`, then `MonoGamePath` or `Il2CppGamePath`; `s1interop setup . --apply` writes the last two. Other properties:

| Property | Default | Purpose |
| --- | --- | --- |
| `S1InteropModsPath` | `<install>\Mods` | Deployment folder. |
| `S1InteropDeploySymbols` | `true` | Also deploy the `.pdb` so MelonLoader stack traces have line numbers. |
| `S1InteropRunArguments` | empty | Arguments `dotnet run` passes to the game. |
| `S1InteropExcludedGameReferences` | empty | Paths or wildcards to leave out of the game references. |
| `S1InteropEmitRuntimeHelpers` | `true` | Set to `false` if the helpers clash with your own extension methods. |
| `S1InteropEmitPlatformDomain` | `true` when `S1InteropTargetRuntime` is set | Set to `false` for a DLL that intentionally loads on both runtimes. |

Generated helpers need C# 9 or later; projects on C# 8 receive `S1I010` and keep diagnostics only. `S1InteropUsing` needs C# 10.
