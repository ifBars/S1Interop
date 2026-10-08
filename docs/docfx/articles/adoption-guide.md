---
title: Start here
description: Write a Schedule I mod in C# and build it for Mono and IL2CPP.
uid: s1interop.start
---

# Start here

S1Interop compiles ordinary `ScheduleOne.*` C# into separate Mono and IL2CPP mods. Write against the game's Mono API; the compiler adapts supported runtime differences during the build. You do not need a Unity Editor project, wrapper declarations, or conditional imports.

## Build your first mod

1. [Install S1Interop](getting-started.md).
2. [Create, build, and load your first mod](first-mod.md).
3. [Write game code](writing-code.md).
4. [Test and distribute it](distributing-mods.md).

The compiler is experimental. You need matching Mono and IL2CPP game installations to build both outputs.

## Bring an existing mod

Use the same compiler project and build commands. [Adopt the compiler in an existing mod](compiler-adoption.md) explains how to bring over source, resources, and dependencies.

## After your first build

[Everyday development](common-tasks.md) covers editing, rebuilding, and updating game references. Use [Troubleshooting](troubleshooting.md) when a step fails.

[How the compiler works](core-concepts.md) explains reference preparation and output files. [Advanced](advanced.md) contains tooling integrations and older workflows for developers maintaining those projects.
