# How the compiler works

## One source, two outputs

S1Interop binds ordinary C# against Mono game metadata. For Mono, it compiles that source with local game references. For IL2CPP, it maps types and adapts supported operations before compiling against MelonLoader's generated interop assemblies.

Original source files remain unchanged. Generated inputs stay under `obj`. The build produces a mod DLL for the selected runtime.

Both builds select the `MONO` authoring branch in existing conditional source. The generated MelonLoader domain attribute identifies the actual output runtime. Framework symbols still follow the selected framework.

## Project files

| File | Purpose |
| --- | --- |
| `.config/dotnet-tools.json` | Pins the compiler tool version. |
| `.s1interop/S1Interop.Compiler.props` and `.targets` | Configure references and outputs, check installations, and invoke the compiler during builds. |
| `local.build.props` | Stores your game installation paths. Keep this file out of source control. |
| `Mod.cs` | Contains the starter's ordinary game code. Replace or extend it for your mod. |

Keep the tool manifest and build imports from the same scaffold generation. Restore the local tool before building a fresh checkout. There is one CLI, `s1interop`; MSBuild calls its `compiler` commands.

## Prepared game references

The compiler creates metadata-only reference copies that expose supported non-public members. It preserves original assembly hashes and field visibility for native repair validation and reflection adaptation.

These copies do not modify the installed game. Use them only as compile inputs; keep them out of deployments and downloads. [Non-public member access](compiler-adoption.md#access-non-public-game-members) describes the remaining visibility exceptions.

## Runtime support and libraries

IL2CPP outputs include `S1Interop.Runtime.dll`. Deploy a compatible generation to `UserLibs`; Mono outputs do not need it. [Distribution](distributing-mods.md) lists the shipping files.

Compiler-built IL2CPP libraries also produce `.s1interop/authoring` metadata companions. Consuming compiler projects use them to bind original signatures and verify the matching library. Include companions in developer-facing library packages, but leave them out of player downloads.

## Compatibility limits

Metadata discovery removes the need to maintain a wrapper catalog for every game type. The compiler itself still needs maintenance when runtime contracts change.

The compiler cannot restore native code absent from the game. [Compatibility](compatibility.md) records known gaps and distinguishes compile checks from runtime behavior.
