---
title: Install S1Interop
description: Install the S1Interop command-line tool and start writing a mod.
uid: s1interop.install
---

# Install S1Interop

## Install .NET

Install the Windows x64 **.NET 8 SDK** from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0). The SDK includes the runtime needed by S1Interop.

Open Command Prompt or Windows Terminal. These commands work in both Command Prompt and PowerShell:

```console
dotnet --list-sdks
```

Check that the list includes an `8.0` SDK.

## Install S1Interop

```console
dotnet tool install --global S1Interop --version 0.1.0-alpha.2
s1interop --version
```

You do not need to clone or build S1Interop. If the terminal cannot find `s1interop` after installation, close it and open a new one.

Continue to [Build your first mod](first-mod.md). If you already have an older S1Interop tool installed, see [Update the tool](common-tasks.md#update-the-tool).
