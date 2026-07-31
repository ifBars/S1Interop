---
title: Build your first mod
description: Create, configure, build, install, and check a small Schedule I mod with explicit Mono and IL2CPP targets.
uid: s1interop.first-mod
---

# Build your first mod

This walkthrough starts with an empty folder and ends with a mod that prints the active Schedule I runtime to the MelonLoader console.

The default project keeps Mono and IL2CPP builds explicit. That is the safe beginner and production fallback while backend-neutral facades remain experimental.

## Before you start

You need:

- Windows and PowerShell;
- .NET SDK 8 or newer;
- the installed `s1interop` alpha tool;
- at least one Schedule I install with MelonLoader;
- the local folder containing `S1Interop.Generators.*.nupkg`.

Check the tools:

```powershell
dotnet --version
s1interop --version
```

Follow [Install S1Interop](getting-started.md) if the command is not available.

## 1. Preview the project

```powershell
s1interop new ..\MyFirstMod
```

The output lists every planned file and identifies the recommended dual-runtime mode. Nothing is written.

## 2. Create the project

```powershell
s1interop new ..\MyFirstMod --apply
Set-Location ..\MyFirstMod
```

S1Interop refuses to write into a non-empty target directory.

The generated `ModCore.cs` already reports the selected runtime:

```csharp
LoggerInstance.Msg($"{ModName} loaded on {S1Interop.Generated.S1InteropRuntime.Backend}.");
```

You do not need to edit code before the first build.

## 3. Diagnose local setup

Try automatic detection:

```powershell
s1interop doctor .
```

`doctor` checks:

- exactly one project exists in the target directory;
- the Mono install has the managed game and MelonLoader references;
- the optional IL2CPP install has generated wrapper assemblies and MelonLoader references;
- `local.build.props` is covered by `.gitignore`.

It is always read-only.

If a path is not detected, pass it explicitly:

```powershell
s1interop doctor . `
  --mono-game-path "D:\Games\Schedule I_alternate" `
  --il2cpp-game-path "D:\Games\Schedule I_public"
```

Only Mono is required for the first Mono build. IL2CPP is optional until you want to build and test the public branch.

## 4. Preview and write local configuration

Use the same explicit flags when automatic detection needs help:

```powershell
s1interop setup .
```

When every required check is ready:

```powershell
s1interop setup . --apply
```

`setup` writes only `local.build.props`. It does not install software, edit the project, or overwrite an existing file. It refuses to write unless the target is covered by a recognized `.gitignore` rule.

## 5. Build one runtime

Build the branch you have installed:

```powershell
dotnet build .\MyFirstMod.sln -c "Debug Mono"
```

or:

```powershell
dotnet build .\MyFirstMod.sln -c "Debug Il2Cpp"
```

The DLL is written to:

```text
bin\Mono\Debug Mono\netstandard2.1\MyFirstMod.dll
```

or:

```text
bin\Il2Cpp\Debug Il2Cpp\net6.0\MyFirstMod.dll
```

## 6. Run it in Schedule I

Copy the DLL matching the active branch into that install's `Mods` folder, then launch the game.

Expected Mono log marker:

```text
MyFirstMod loaded on Mono.
```

Expected IL2CPP log marker:

```text
MyFirstMod loaded on Il2Cpp.
```

`Unknown` means the runtime probes did not find the expected assemblies. Keep the log and use [Troubleshooting](troubleshooting.md).

## What you have now

You have a normal MelonLoader mod with:

- explicit Mono and IL2CPP build targets;
- compile-time S1Interop diagnostics;
- read-only diagnosis and analysis commands;
- ignored machine-local paths;
- a deterministic runtime success marker.

Continue with [Common tasks](common-tasks.md) to analyze code, add a safe migration plan, or validate both builds.

The backend-neutral one-DLL scaffold is an experimental opt-in:

```powershell
s1interop new ..\MyExperiment --backend-neutral --apply
```

Do not use it as the production default. Keep explicit runtime builds until the exact mod has sustained real-world validation on both branches.
