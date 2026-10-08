---
title: Advanced
description: Integrate libraries and tooling, or maintain existing generator and migration projects.
uid: s1interop.advanced
---

# Advanced

New mods and existing mods adopting S1Interop use the [compiler workflow](first-mod.md). The tools below address specific needs in established projects.

## Libraries and compiler internals

- [Existing-mod adoption](compiler-adoption.md) covers dependencies, resources, and source selection.
- [S1API and S1Interop](s1api-and-s1interop.md) explains how gameplay libraries fit alongside the compiler.
- [Compiler support and evidence](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) records supported operations, diagnostics, and known gaps.
- [Command reference](commands.md) documents the CLI, including build operations normally invoked by MSBuild.
- [Architecture](architecture.md) and the [Core API](api-reference.md) cover tooling integrations.

## Inspect a project without changing its build

Run these commands from an existing mod's directory:

```powershell
s1interop analyze .
s1interop lint .
```

They inspect project structure and known source risks without editing files. These checks do not establish compiler compatibility or replace runtime testing.

To add the older generator diagnostics to normal builds, reference its package:

```xml
<PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.2" PrivateAssets="all" />
```

In the mod project, run `dotnet restore`. Keep package versions pinned. The package can emit runtime helpers when it recognizes the target runtime. It does not enable source compiler adaptation.

Read [Generator diagnostics](diagnostics.md) for requirements. Declaration checks need game reference metadata; no warnings without those references is not proof of compatibility.

## Maintain a generator project

The legacy generator provides explicit runtime helpers and declaration-based bindings. It cannot rewrite arbitrary source like the compiler.

Use [Legacy project setup](legacy-generator-first-mod.md), [Runtime helpers](dual-runtime-code.md), and [Generated output](generator-package.md) for projects that depend on it. Their fixes and configuration properties apply to that workflow.

Create this project explicitly:

```powershell
s1interop new .\GeneratorMod --legacy-generator --apply
```

[Migrations](migrating-mono-mods.md) can add legacy helpers and runtime configurations to an existing project. They preview changes, preserve backups, and support rollback. They do not adopt the compiler.

## Explore generated facades

The experimental facade workflow exposes selected game types through generated `S1Interop.*` declarations. It aims at one shipping DLL and has different limits from compiler projects.

```powershell
s1interop new .\FacadeMod --backend-neutral --apply
```

Use [Backend-neutral SDK](backend-neutral-sdk.md), [Declarations](backend-neutral-declarations.md), and [SDK generation](sdk-generation.md) when maintaining or investigating that experiment. Validate the same shipping DLL on both runtimes.

## Maintain published alpha.1

Published alpha.1 contains the earlier generator workflow. Its generator requires a .NET 9 SDK, version 9.0.200 or newer.

```powershell
dotnet tool install --global S1Interop --version 0.1.0-alpha.1
```

Use `dotnet tool update` with the same arguments if already installed. Pin generator references to `0.1.0-alpha.1` too. Alpha.1's plain `new` creates the legacy project; `--legacy-generator` selects that project in alpha.2.

[Legacy troubleshooting](legacy-troubleshooting.md) covers generator, migration, and alpha.1 failures.
