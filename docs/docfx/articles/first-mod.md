---
title: Build your first mod
description: Create, build, install, and check a small Schedule I mod with S1Interop.
uid: s1interop.first-mod
---

# Build your first mod

This walkthrough starts with an empty folder and ends with a mod that prints the active Schedule I backend to the MelonLoader console. It is deliberately small. Get this working before adding game types, patches, or another modding library.

## Before you start

You need:

- Windows and PowerShell;
- the .NET 8 SDK or newer;
- the S1Interop repository;
- a Schedule I Mono install with MelonLoader. The Steam `alternate` and `alternate-beta` branches use Mono.

Run this first:

```powershell
dotnet --version
s1interop --version
```

The first command should print `8.0` or a newer major version. The second should print an S1Interop version. If `s1interop` is not found, follow [Install S1Interop](getting-started.md).

> [!NOTE]
> The normal one-DLL build uses the Mono game files as compile-time references. You only need a separate IL2CPP install when you are ready to run the optional IL2CPP reference check or test the mod in that branch.

## 1. Preview the project

From the S1Interop repository, ask the CLI what it would create:

```powershell
s1interop new ..\MyFirstMod --dry-run
```

The output should start with `S1Interop new project dry-run: MyFirstMod` and list a solution, project file, `ModCore.cs`, an S1Interop declarations file, and local setup files. Nothing is written during a dry run.

## 2. Create the project

Run the same command with `--apply`:

```powershell
s1interop new ..\MyFirstMod --apply
```

S1Interop creates `MyFirstMod` beside the repository. It refuses to write into a non-empty target directory, which helps prevent an accidental overwrite when using the CLI.

## 3. Add your local paths

Copy the example props file:

```powershell
Copy-Item ..\MyFirstMod\local.build.props.example ..\MyFirstMod\local.build.props
```

Open `local.build.props` and replace the placeholder values:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>D:\Games\Schedule I_alternate</MonoGamePath>
    <Il2CppGamePath>D:\Games\Schedule I_public</Il2CppGamePath>
    <S1InteropGeneratorPackageSource>C:\Code\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
    <RestoreAdditionalProjectSources Condition="'$(S1InteropGeneratorPackageSource)'!=''">$(S1InteropGeneratorPackageSource);$(RestoreAdditionalProjectSources)</RestoreAdditionalProjectSources>
  </PropertyGroup>
</Project>
```

Use the folder that contains `Schedule I.exe` for each game path. `S1InteropGeneratorPackageSource` must point to the `artifacts\packages` folder created when you [installed the local alpha packages](getting-started.md).

Only `MonoGamePath` and the package source are required for the first build. Leave `Il2CppGamePath` as a placeholder until you have a separate public or `beta` install with MelonLoader-generated assemblies.

`local.build.props` is ignored by the scaffold. Do not commit it, game DLLs, or generated IL2CPP assemblies.

## 4. Add the first bit of code

Replace `MyFirstMod\ModCore.cs` with this:

[!code-csharp[](../samples/first-mod/ModCore.cs)]

There are three pieces worth knowing:

- `MelonInfo` tells MelonLoader which class starts the mod.
- `MelonGame` limits the mod to Schedule I.
- `OnInitializeMelon` runs when MelonLoader initializes the mod. The generated `S1InteropRuntime` helper reports whether the current game uses Mono or IL2CPP.

You do not need to understand generated facades yet. The generator package adds `S1InteropRuntime` during the build.

## 5. Build the DLL

Move into the new project and build it:

```powershell
Set-Location ..\MyFirstMod
dotnet build .\MyFirstMod.sln -c Debug
```

A successful build ends with `Build succeeded.` The DLL is written to:

```text
bin\Single\Debug\netstandard2.1\MyFirstMod.dll
```

If the build reports a missing MelonLoader or game assembly, check that `MonoGamePath` points to the game folder, not `Schedule I_Data` or one of its subfolders. See [Common issues](troubleshooting.md) for the exact fixes.

## 6. Run it in Schedule I

Copy `MyFirstMod.dll` into the game's `Mods` folder, then launch Schedule I.

Look for this part of the message in the MelonLoader console or `MelonLoader\Latest.log`:

```text
MyFirstMod loaded on Mono.
```

The same DLL should print `MyFirstMod loaded on Il2Cpp.` when loaded by an IL2CPP install. `Unknown` means the runtime probes did not find the expected Schedule I assemblies; keep the log and check [Runtime shows Unknown](troubleshooting.md#runtime-shows-unknown).

## What you have now

You built a normal MelonLoader mod. S1Interop supplied a compile-time generator and a small runtime helper inside your DLL; it did not add a separate runtime dependency.

Next, use [Common tasks](common-tasks.md) to add one game type, run an IL2CPP reference check, or analyze an existing mod. Read [Core concepts](core-concepts.md) when a term such as facade, declaration, or backend becomes relevant.
