---
title: Install S1Interop
description: Install and check the published tool or an unreleased candidate.
uid: s1interop.install
---

# Install S1Interop

The `S1Interop` package provides the single terminal command and source compiler. New compiler projects pin it in a local tool manifest. `S1Interop.Generators` belongs to the earlier helper/facade workflow and is not required by the compiler starter. Players install the built mod and, for IL2CPP compiler builds, its matching `S1Interop.Runtime.dll`.

> [!IMPORTANT]
> These docs describe the **0.1.0-alpha.2 candidate**, which is not yet published. The published version is **0.1.0-alpha.1**. Use the source-build route below for the compiler and compiler-first project creation. See [Release readiness](../contributors/release-readiness.md) for publication gates.

## 1. Check the .NET SDK

Install the Windows x64 **SDK** from the [.NET 8 download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), then open a new PowerShell window. The SDK compiles C# into a DLL; a runtime alone cannot build a mod.

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

For the candidate, expect an SDK at `8.0` or newer and a `Microsoft.NETCore.App 8.0` runtime. A newer major SDK does not necessarily install the .NET 8 runtime needed by the command; install the .NET 8 SDK alongside it if missing.

**Published alpha.1 requires a .NET 9 SDK (9.0.200 or newer) for its generator.** Install it from the [.NET 9 download page](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). The candidate fixes this by targeting Roslyn 4.8, the compiler API available from .NET 8.

## 2. Install the command

For the currently published version:

```powershell
dotnet tool install --global S1Interop --version 0.1.0-alpha.1
```

If already installed, use `dotnet tool update` with the same arguments. Published packages restore from NuGet.org without a custom source.

## 3. Check the installation

```powershell
s1interop --version
s1interop --help
```

The output identifies the installed version and lists commands including `doctor`, `setup`, `new`, `analyze`, and `verify-migration`. If PowerShell cannot find the command, reopen it and check `dotnet tool list --global`.

For published alpha.1, continue to the [earlier generator walkthrough](legacy-generator-first-mod.md). For the compiler-first [Build your first mod](first-mod.md) walkthrough, install the candidate below.

## Build and install the candidate from source

Download or clone the [S1Interop repository](https://github.com/ifBars/S1Interop), open PowerShell in its root (the folder containing `S1Interop.sln`), and run each command separately:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Release --no-restore
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release --no-build -o .\artifacts\packages
$env:NUGET_PACKAGES = "$PWD\artifacts\candidate-cache"
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.2
$env:PATH = "$PWD\.tools;$env:PATH"
$candidateFeed = (Resolve-Path .\artifacts\packages).Path
s1interop --version
```

Stop if a command fails. Expect `S1Interop 0.1.0-alpha.2` before continuing. Use this same PowerShell window for the walkthrough: the path and candidate feed apply only to this terminal and its child processes. No feed or package-cache path belongs in `local.build.props`.

When rebuilding an unpublished candidate with the same version, use a new cache directory name (for example `candidate-cache-2`) so an older compiler or build asset is not reused. Install the CLI into a fresh tool directory too. The public global tool can remain installed.

Contributors should run `./tests/Test-CompilerPackage.ps1 -PackageDirectory ./artifacts/packages`, which validates the compiler package, generated project, and local tool restore in an isolated .NET 8 environment. Supply both `-MonoGamePath` and `-Il2CppGamePath` to also verify setup, doctor, and both real-reference builds. `Test-Packages.ps1` covers the separate generator package workflow.
