---
title: Compatibility
description: What S1Interop handles today, where it still falls short, and what to test before sharing a mod.
uid: s1interop.compatibility
---

# What works and what's missing

S1Interop is experimental. You can write ordinary game code and build it for both runtimes, but some operations still need compiler support. A successful build doesn't guarantee that your mod works in-game.

## What works today

Tested cases include:

- **Game API access:** game types, nested types, constructors, methods, and supported nonpublic members. Coverage comes from local game metadata rather than a hand-written wrapper catalog.
- **Common runtime differences:** casts, interfaces, object identity, callbacks, event removal, and coroutines.
- **Shared collections:** supported lists, dictionaries, and arrays preserve access to game storage and mutations instead of silently copying values.
- **Mod components and libraries:** supported `MonoBehaviour` subclasses and compiler-built dependencies, including shared runtime support across mods.

These are tested categories, not a promise that every use of them works. The compiler needs matching Mono and IL2CPP game references. [How the compiler works](core-concepts.md) explains the build and runtime dependencies.

## What still needs work

- **Reflection:** computed types or names, escaped reflection descriptors, and stored or chained Harmony Traverse calls have gaps.
- **Collections and callbacks:** some generic, array, and collection flows across game or library boundaries remain unsupported.
- **Unity components and assets:** arbitrary serialized fields, prefab scripts, and AssetBundle integration are not fully covered.
- **Existing libraries:** prebuilt dependencies don't become compatible just because your mod uses the compiler.
- **Missing game code:** publicizing references cannot restore stripped native code or APIs absent from the target game version.

[Polyfill](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill) by DooDesch ([Nexus Mods](https://www.nexusmods.com/schedule1/mods/2452)) tackles a related gap: repairing missing or changed API names in MelonLoader's generated assemblies so older mods can run. Its documented limits include code removed without a successor. We haven't verified it alongside S1Interop.

Unsupported cases may produce a compiler diagnostic, but some only fail at runtime. The compiler itself still needs maintenance as these cases and game updates surface.

## Before sharing your mod

Build and test each runtime you intend to support. Exercise the actual feature, including save/load or multiplayer behavior when relevant. Our runtime probes use matching 0.4.7f9 installations; they don't establish compatibility for every mod or game version.

Follow [Test and distribute](distributing-mods.md). If something fails, check [Troubleshooting](troubleshooting.md) and report the smallest source example that reproduces it.

For implementation details and individual test results, see the [detailed compiler evidence](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md).
