---
title: Start here
description: Choose the shortest S1Interop route for a first mod, an existing mod, or a tooling integration.
uid: s1interop.start
---

# Start here

Start with the source compiler when evaluating S1Interop's current direction. It adapts ordinary `ScheduleOne.*` C# into separate Mono and IL2CPP outputs using game metadata. Follow the [compiler setup and sample](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md#try-the-source-checkout), including its requirements for matching game installations and its known compatibility limits.

> [!TIP]
> Use the locally built candidate for the compiler workflow. Its `s1interop new` command creates a compiler-enabled project by default. The published alpha.1 package has the earlier behavior; `--legacy-generator` preserves that scaffold in the candidate. Follow the first-mod guide for the compiler route.

## New to Schedule I modding

Complete one small result before learning the migration and generator features:

1. [Install S1Interop](getting-started.md).
2. [Build your first mod](first-mod.md).
3. Stop when the mod loads in-game and reports the expected runtime.

The walkthrough explains each prerequisite, command, output path, and success log. After it works, use [Common tasks](common-tasks.md) to build the other runtime or inspect a real project.

If your mod is mainly about items, NPCs, shops, UI, saves, or other gameplay systems, read [S1API and S1Interop](s1api-and-s1interop.md) first. S1API may own most of the feature; S1Interop can remain limited to direct game calls that need runtime compatibility.

## Already maintaining a mod

Install the tool, open a terminal in the mod directory, and start with one read-only command:

```batch
s1interop analyze .
```

`analyze` reports the project shape, runtime evidence, and known IL2CPP risks without changing files. Choose the next step from the result you want:

| Outcome | Next step |
| --- | --- |
| Compile ordinary game source for both backends | Follow [Adopt the compiler in an existing mod](compiler-adoption.md). |
| Keep the current architecture and inspect known risks | Run `s1interop lint .`, then read [Diagnostics](diagnostics.md). |
| Add diagnostics to normal compiler builds | Add the private generator reference below. |
| Retain handwritten runtime branches and add the older helper workflow | Preview [legacy dual-runtime migration](migrate-to-dual-runtime.md). |
| Verify a migration without touching the original project | Use the sandbox flow in [Migration overview](migrating-mono-mods.md). |
| Share one direct game seam across runtimes | Evaluate a narrow [backend-neutral migration](migrate-to-backend-neutral.md). |

Start at the direct `ScheduleOne.*` or `Il2CppScheduleOne.*` seam causing the compatibility problem. Keep content registration, saves, networking, deployment, and packaging in their existing libraries and workflows.

### Add compiler diagnostics without migrating

The CLI's `lint` command runs only when invoked. To get generator diagnostics during compilation, add this inside an existing `<ItemGroup>` in your `.csproj`:

```xml
<PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.2" PrivateAssets="all" />
```

This is the candidate version; follow the [candidate installation](getting-started.md#build-and-install-the-candidate-from-source) for its local feed. If staying on the published alpha.1, use `0.1.0-alpha.1` and its documented compiler requirements instead. Keep the version pinned for repeatable builds.

Build each existing runtime configuration, inspect warnings, and keep your source and dependencies in their current shape. No facade declaration or `sdkgen` run is needed for diagnostics-only adoption. The package's build integration stays off until you opt in, so references and output do not change. A project on C# 8, the `netstandard2.1` default, also gets one `S1I010` warning; set `<LangVersion>latest</LangVersion>` when you want the [cross-runtime helpers](dual-runtime-code.md). Declaration diagnostics require matching game references; silence without those references is not a compatibility result.

Remove this package reference to undo diagnostics-only adoption. If you later call generated helpers, remove or replace those usages before removing the package. It is a build dependency, so players do not install it.

## Already know what you need

| I need to... | Go to... |
| --- | --- |
| Look up a CLI option | [Command reference](commands.md) |
| Configure local game installs | [Local game paths](local-paths.md) |
| Generate declarations from source or metadata | [SDK generation](sdk-generation.md) |
| Understand generated symbols and diagnostics | [Generated output](generator-package.md) |
| Call S1Interop from another tool | [Core API reference](api-reference.md) |
| Compare small, mixed adoption patterns | [Ways to use S1Interop](use-cases.md) |
| Resolve a failure | [Troubleshooting](troubleshooting.md) |

## Safety rules that apply to every route

- Commands that can write files preview their plan until you add `--apply`.
- Applied migrations create backups and a rollback manifest under `s1interop-runs/<run-id>/`.
- `verify-migration` works in a temporary copy instead of the source project.
- A Mono build is evidence for Mono only; build and test IL2CPP separately.
- Keep `local.build.props`, game assemblies, generated wrappers, decompiled output, and game assets out of source control.
