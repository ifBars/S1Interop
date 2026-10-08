# Core concepts

## One source, two outputs

The source compiler binds ordinary C# against Mono game metadata. For Mono, it builds that source with local game references. For IL2CPP, it maps types and lowers supported operations before compiling against the generated native proxy assemblies. Original source files remain unchanged; generated inputs stay under `obj`.

Both builds select the Mono authoring branch in existing conditional source. The generated MelonLoader domain attribute identifies the actual output runtime independently. Custom and framework symbols remain available, so framework-specific conditionals can still differ.

The result is two mod DLLs. This is the default candidate workflow; it does not aim to put both managed type systems into one shipping assembly.

## One CLI and its build integration

| Component | Role |
| --- | --- |
| `s1interop` | Project creation, setup, analysis, and compiler operations. The project imports invoke its `compiler` commands during builds. |
| `.s1interop/S1Interop.Compiler.props` and `.targets` | Select references and outputs, verify installations, and invoke the compiler. New projects contain matching copies. |
| `.config/dotnet-tools.json` | Pins the local CLI package version. Run `dotnet tool restore` before building. |
| `S1Interop.Runtime.dll` | Shared support required by compiler-built IL2CPP mods. Deploy a compatible generation to `UserLibs`. |
| `S1Interop.Generators` | Older declaration/helper workflow. Not required by the default compiler scaffold. |

The compiler implementation remains a library inside the toolchain. There is no second CLI to install.

## Game metadata and authoring companions

The compiler needs matching Mono and IL2CPP game versions for native output. It prepares local reference-only copies that expose supported nonpublic access. Those copies are build inputs, not runtime dependencies, and must not be distributed.

A compiler-built IL2CPP library also produces `.s1interop/authoring` metadata companions. They let a consuming compiler project bind original signatures and verify that they match the compiled library. Keep these companions with developer-facing library distributions; players do not need them.

## Compatibility evidence

A build proves that the selected source binds and emits. A loader initialization check proves that a specific artifact loads. A runtime probe tests only its exercised behavior. Gameplay, saves, networking, and arbitrary content creation need their own evidence on each supported backend.

Metadata discovery avoids a manually maintained game wrapper catalog. It does not remove compiler maintenance or restore code absent from the native game. See the [compiler support guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for current limits.

## Older workflows

Legacy dual-runtime migration retains explicit runtime-specific code and may add generator helpers. Backend-neutral facades expose selected types through generated `S1Interop.*` declarations. Neither is the default compiler route.

Existing migration commands still preview changes and preserve rollback manifests. Their [migration guide](migrating-mono-mods.md), [declaration reference](backend-neutral-declarations.md), and [generated output reference](generator-package.md) remain available for those projects.
