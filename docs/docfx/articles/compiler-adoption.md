---
title: Bring an existing mod
description: Bring source and dependencies into a compiler project, then validate both runtime outputs.
uid: s1interop.compiler-adoption
---

# Bring an existing mod

Use the same compiler project and build commands as the [first-mod walkthrough](first-mod.md). Keep the original mod while you check the new build.

Adoption is currently manual. You need to carry over source files, resources, dependencies, and packaging.

## Create the compiler project

Complete [installation](getting-started.md). Create an empty sibling project:

```console
s1interop new ../MyMod-Compiler --apply
```

In the new directory, restore the pinned tool and configure both game paths as described in [Create and configure](first-mod.md#create-and-configure).

## Bring over source and dependencies

1. Remove the generated `Mod.cs` starter.
2. Copy the original project's selected source files into a `Source` directory.
3. Preserve the original assembly name when source or dependencies rely on it.
4. Add the mod's third-party references and required resources.

The new project includes C# files under its directory automatically. Keep unrelated files outside it. Preserve compile exclusions, linked source, source generators, and embedded resources; copying a directory may not capture them.

Keep the original mod entry point and Melon metadata. Leave out `bin`, `obj`, generated wrappers, and the original build scripts. The compiler imports already provide game, Unity, MelonLoader, and Harmony references.

Use normal `ProjectReference` entries for libraries built through the compiler. Prebuilt libraries need compatible dependency APIs for each output. Keep `.s1interop/authoring` companions with compiler-built IL2CPP libraries distributed as binaries.

Start with unchanged author source. Both compiler outputs select existing `MONO` branches. Review `IL2CPP` and `!MONO` branches for independent features you need to bring into that author source. Framework symbols still follow the selected framework.

## Access non-public game members

The compiler prepares publicized, metadata-only game references under `obj` and generates runtime access attributes. A member being private on Mono but public in an IL2CPP wrapper does not, by itself, require conditional source. Original game assemblies remain unchanged.

This addresses the same visibility difference as [S1MelonModTemplate's Krafs.Publicizer setup](https://github.com/k073l/S1MelonModTemplate/blob/master/template/MyMod.csproj). Compiler projects need no additional publicizer package. S1Interop also preserves original hashes and field visibility for reflection adaptation and native repair validation.

Publicization cannot supply absent members or stripped native code. The compiler preserves virtual member visibility and the visibility of fields sharing event or property names to avoid changing source binding. Report unsupported access patterns with the compiler diagnostic and a small example.

## Build and test

Run these commands serially from the new project:

```console
dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

Outputs go to `bin/Release/Mono/netstandard2.1` and `bin/Release/Il2Cpp/net6.0`. The IL2CPP output also contains `S1Interop.Runtime.dll`.

Resolve missing dependencies and outdated game API calls before diagnosing compiler adaptation. Check that the build includes all intended sources and resources. Keep generated files under `obj` unchanged.

Follow [Test and distribute a mod](distributing-mods.md). Verify loading and the actual feature on each runtime, including saves or multiplayer where applicable. Compare behavior with the original mod.

After validation, adopt the compiler project as your normal build and restore the packaging steps you need. The [existing-mod evidence](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md#existing-mod-corpus) records completed compile and runtime checks.
