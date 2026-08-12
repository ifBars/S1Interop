---
title: Start here
description: Choose the shortest S1Interop route for a first mod, an existing mod, or a tooling integration.
uid: s1interop.start
---

# Start here

Choose the section that matches your experience. You do not need to read the documentation in order, and you do not need to migrate an entire mod to use S1Interop.

> [!TIP]
> If you are unsure, keep Mono and IL2CPP as explicit build targets. This is the recommended starting point for new and existing mods. The one-assembly backend-neutral path is an experimental opt-in.

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
| Keep the current architecture and add compiler guardrails | Run `s1interop lint .`, then read [Diagnostics](diagnostics.md). |
| Produce separate Mono and IL2CPP assemblies | Preview [dual-runtime migration](migrate-to-dual-runtime.md). |
| Verify a migration without touching the original project | Use the sandbox flow in [Migration overview](migrating-mono-mods.md). |
| Share one direct game seam across runtimes | Evaluate a narrow [backend-neutral migration](migrate-to-backend-neutral.md). |

Start at the direct `ScheduleOne.*` or `Il2CppScheduleOne.*` seam causing the compatibility problem. Keep content registration, saves, networking, deployment, and packaging in their existing libraries and workflows.

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
