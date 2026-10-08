---
title: Test and distribute a mod
description: Validate each runtime, package the right DLL, and tell players what to install.
---

# Test and distribute a mod

Build success checks references and C# code. A release also needs evidence that the DLL loads and its feature works in the game branch you advertise.

## Build the outputs you support

For a project created by the default `s1interop new` command:

Restore its pinned local tool first, using the candidate feed described in [installation](getting-started.md). Run the builds below serially because the project shares NuGet restore state.

```powershell
dotnet build -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

Use the same commands for an existing mod after adopting the compiler project.

| Build | Shipping DLL |
| --- | --- |
| Mono | `bin\Release\Mono\netstandard2.1\MyMod.dll` |
| IL2CPP | `bin\Release\Il2Cpp\net6.0\MyMod.dll` |

Keep the two files in separate archives, such as `MyMod-0.1.0-Mono.zip` and `MyMod-0.1.0-Il2Cpp.zip`. Both contain `Mods/MyMod.dll` and a short README; the IL2CPP archive additionally contains `UserLibs/S1Interop.Runtime.dll`. Players install one runtime variant. Each DLL declares its intended MelonLoader platform domain; verify loader behavior with the versions you support.

Compiler builds do not deploy to your local game, in either Debug or Release. The IL2CPP archive must also include the matching `S1Interop.Runtime.dll` under `UserLibs`. Include other mod-library dependencies according to their distribution requirements.

## Validate each archive

1. Close the matching game install and deploy the DLL from the archive you intend to upload.
2. Launch with the dependencies your README lists. Confirm the loader recognizes your mod and there are no dependency-load errors.
3. Exercise the actual feature. Check its expected output, repeat it, and check `MelonLoader\Latest.log` for failures. For a Harmony patch, confirm the handler fires; for a save feature, reload the save and check the result.
4. Repeat on the other runtime if you advertise support for it. Record game version/branch, MelonLoader version, mod version, dependencies, and observed result separately for Mono and IL2CPP.

A synthetic fixture, sandbox build, or Mono-only playtest cannot establish IL2CPP gameplay compatibility. Test each compiler-produced shipping DLL on its own runtime.

## Include what players need

Your release description should name supported branches, tested game/loader versions, required mod libraries, installation steps, removal steps, and known limitations. Keep S1API and other runtime dependencies in the installation instructions appropriate to those libraries.

Compiler-built IL2CPP mods require compatible generations of `S1Interop.Runtime.dll`; the Mono output does not need it. Do not ship the `S1Interop` command or `S1Interop.Generators.dll` to players. Do not include game assemblies, MelonLoader DLLs, generated IL2CPP wrappers, `local.build.props`, or the entire `bin` folder.

If distributing a compiler-built library to other developers, preserve its `.s1interop/authoring` directory alongside the IL2CPP DLL. The compiler verifies those metadata companions against the library hash. Players do not need the authoring directory.

## Report a compatibility problem

Include the S1Interop version, selected configuration, game branch/version, MelonLoader version, first relevant error, and a small source example. Remove personal paths and credentials from logs. Keep proprietary binaries local.

For a S1Interop bug, use the [issue tracker](https://github.com/ifBars/S1Interop/issues). For a loader or dependency failure, first confirm which component emitted the error using [Troubleshooting](troubleshooting.md).
