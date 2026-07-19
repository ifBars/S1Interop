---
title: Install S1Interop
description: Build the current alpha packages from source and install the S1Interop command.
uid: s1interop.install
---

# Install S1Interop

S1Interop is still an alpha project. The current setup builds the command-line tool and source generator from this repository, then installs the command from a local NuGet feed.

## Check the .NET SDK

Run:

```powershell
dotnet --version
```

You need .NET SDK 8.0 or newer. This is the developer SDK, not just the .NET runtime.

## 1. Build S1Interop

Open PowerShell in the S1Interop repository:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Release
```

Both commands should finish without errors. The solution contains the CLI, the Core library, the source generator, and the test project.

## 2. Create the local packages

Pack the CLI and generator into the same folder:

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
```

You should now have both of these packages under `artifacts\packages`:

```text
S1Interop.0.1.0-alpha.1.nupkg
S1Interop.Generators.0.1.0-alpha.1.nupkg
```

The CLI creates and edits projects. The generator runs inside each mod build and creates the backend-neutral helpers. A scaffolded mod needs the generator package feed even after the CLI is installed.

## 3. Install the command

Install the CLI as a global .NET tool so the `s1interop` command works from a mod folder:

```powershell
dotnet tool install --global S1Interop `
  --add-source .\artifacts\packages `
  --version 0.1.0-alpha.1
```

If this version is already installed, update it instead:

```powershell
dotnet tool update --global S1Interop `
  --add-source .\artifacts\packages `
  --version 0.1.0-alpha.1
```

## 4. Check the installation

Run:

```powershell
s1interop --version
s1interop --help
```

The version output should start with `S1Interop 0.1.0-alpha.1`. A source build may add a `+` suffix containing the Git commit. The help output should list commands such as `new`, `analyze`, `sdkgen`, and `verify-migration`.

If PowerShell cannot find `s1interop`, close and reopen the terminal so the .NET global tools path is refreshed. You can inspect installed tools with:

```powershell
dotnet tool list --global
```

## Keep the package feed path

Run this from the S1Interop repository and copy the result:

```powershell
Resolve-Path .\artifacts\packages
```

You will put that absolute folder path in a mod's `local.build.props` as `S1InteropGeneratorPackageSource`. That lets `dotnet restore`, Visual Studio, and Rider find the unpublished generator package without adding a machine-specific source to the project file.

## Install without changing global tools

If you prefer a repository-local tool, use `--tool-path`:

```powershell
dotnet tool install S1Interop `
  --tool-path .\.tools `
  --add-source .\artifacts\packages `
  --version 0.1.0-alpha.1

.\.tools\s1interop --version
```

The rest of these docs use the global `s1interop` command. Substitute the full `.tools\s1interop` path if you choose the local install.

Continue to [Build your first mod](first-mod.md).
