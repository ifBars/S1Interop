---
title: Install S1Interop
description: Install the published S1Interop alpha tool and verify the command.
uid: s1interop.install
---

# Install S1Interop

S1Interop is an alpha .NET tool. Install the command from NuGet.org; generated mod projects restore `S1Interop.Generators` from the same public feed.

## 1. Check the .NET SDK

```powershell
dotnet --version
```

Use .NET SDK 8.0 or newer. The SDK is required, not only the .NET runtime.

## 2. Install the command

```powershell
dotnet tool install --global S1Interop --version 0.1.0-alpha.1
```

If S1Interop is already installed, update it:

```powershell
dotnet tool update --global S1Interop --version 0.1.0-alpha.1
```

No custom NuGet source is required. The CLI package and `S1Interop.Generators` are published together.

## 3. Check the installation

```powershell
s1interop --version
s1interop --help
```

The version output should start with `S1Interop 0.1.0-alpha.1`. The help output should list `doctor`, `setup`, `new`, `analyze`, `sdkgen`, and `verify-migration`.

If PowerShell cannot find `s1interop`, close and reopen the terminal so the .NET global-tools path refreshes. Inspect installed tools with:

```powershell
dotnet tool list --global
```

## Repository-local tool install

To keep the command inside one working directory:

```powershell
dotnet tool install S1Interop --tool-path .\.tools --version 0.1.0-alpha.1
.\.tools\s1interop --version
```

The rest of these docs use the global `s1interop` command. Substitute the full `.tools\s1interop` path if you choose a local install.

## Building unreleased source

Contributors can still create local packages for validation:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Release
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
```

Pass that temporary folder as an explicit restore source in contributor tests. Do not put a package-feed path in a generated mod's `local.build.props`.

Continue to [Build your first mod](first-mod.md).
