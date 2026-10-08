---
title: Test a source build
description: Build and install unpublished packages for compiler development.
---

# Test a source build

This workflow is for contributors testing unpublished changes. Mod authors use [Install S1Interop](../articles/getting-started.md).

Use PowerShell and the .NET 8 SDK. Candidate caches and feeds stay local; do not add them to a mod's shared build configuration.

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

Expect `S1Interop 0.1.0-alpha.2`. Keep this PowerShell window open while testing the local package.

Use the installed tool to create a temporary mod outside the checkout. In that mod directory, restore with `dotnet tool restore --add-source $candidateFeed`, then follow [Build your first mod](../articles/first-mod.md) from game setup onward. A new terminal must set `$candidateFeed` to the absolute package directory again.

If you rebuild the candidate with the same version, use fresh cache and tool directories, such as `candidate-cache-2` and `.tools-2`. This prevents NuGet from reusing an earlier build. See [Troubleshooting](../articles/troubleshooting.md) if restore selects the wrong package.
