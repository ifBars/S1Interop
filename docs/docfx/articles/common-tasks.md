---
title: Everyday development
description: Edit your mod, rebuild it, and keep its local references current.
uid: s1interop.common-tasks
---

# Everyday development

Run these commands from the compiler project created in [Build your first mod](first-mod.md).

## Edit and rebuild

Write C# in your mod's source files using ordinary game and Unity namespaces. Keep compiler-generated files under `obj` unchanged.

```powershell
dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

Run the builds one at a time. Close the game before replacing its mod DLL, then launch it and exercise the changed feature. [Test and distribute a mod](distributing-mods.md) lists the files to deploy.

## Restore a fresh checkout

Keep the project's `.config/dotnet-tools.json` and `.s1interop` build files in source control. Restore the pinned tool using the feed from [installation](getting-started.md):

```powershell
dotnet tool restore --add-source $candidateFeed
```

Each developer configures their own ignored `local.build.props`. Follow [Local game paths](local-paths.md) when setting up another machine.

## Update game references

Update both installations to the same game patch version. Launch the IL2CPP installation with MelonLoader to regenerate its interop assemblies, then close it.

```powershell
dotnet tool run s1interop -- doctor .
```

Build both outputs again. The build verifies game versions and native reference provenance. If an installation moved, update its path in `local.build.props` before running doctor.

## Diagnose a failed build

Start with the first error. Missing dependencies, changed game APIs, and unsupported compiler operations need different fixes. [Troubleshooting](troubleshooting.md) explains how to identify them.

Keep a small source example when reporting a compiler gap. Include the tool, game, and MelonLoader versions, plus the failing runtime.
