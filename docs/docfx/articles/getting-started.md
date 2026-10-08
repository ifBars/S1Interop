---
title: Install S1Interop
description: Build and install the compiler candidate for the first-mod walkthrough.
uid: s1interop.install
---

# Install S1Interop

These instructions install **0.1.0-alpha.2**, the unpublished compiler candidate. Published alpha.1 does not contain the compiler used in this walkthrough.

## Install the .NET 8 SDK

Install the Windows x64 SDK from the [.NET 8 download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), then open PowerShell:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

Check for an `8.0` SDK and `Microsoft.NETCore.App 8.0`. Install the SDK even if you already have the runtime. Keep .NET 8 installed alongside any newer SDKs; this checkout selects it through `global.json`.

## Build and install the candidate from source

Clone or download the [S1Interop repository](https://github.com/ifBars/S1Interop). Open PowerShell in its root, beside `S1Interop.sln`.

Run each command separately. Stop if a command fails.

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

Expect `S1Interop 0.1.0-alpha.2`. Keep this PowerShell window open: the next page uses its tool path and `$candidateFeed`.

Continue to [Build your first mod](first-mod.md). Create your mod outside the S1Interop checkout and game directories.

If you rebuild the candidate with the same version, use fresh cache and tool directories, such as `candidate-cache-2` and `.tools-2`. This prevents NuGet from reusing an earlier build. See [Troubleshooting](troubleshooting.md) if restore selects the wrong package.
