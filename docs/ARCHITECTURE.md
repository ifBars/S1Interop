# Architecture

S1Interop uses one CLI to create projects, configure local game references, and run the source compiler. Mods use ordinary game types and build separate Mono and IL2CPP assemblies from shared source.

## Source compiler flow

```text
Mod source + publicized Mono reference metadata
  -> bind source against the Mono API
  -> compile the Mono output
  -> map supported operations to matching IL2CPP metadata
  -> compile the IL2CPP output and shared runtime support
```

Generated inputs stay under `obj`; the compiler leaves authored source unchanged. Reference preparation creates local metadata-only assemblies and preserves the original game files. Publicization exposes eligible nonpublic members for authoring; it cannot restore APIs absent from the target runtime.

The IL2CPP output uses `S1Interop.Runtime.dll` for shared adapters. Compiler-built libraries also produce authoring companions so consumers can bind against the original source-facing API. The build verifies those companions before using them.

Coverage comes from source analysis and local reference metadata. Unsupported operations need diagnostics or compiler work; there is no hand-maintained wrapper catalog that defines which game types a mod can use. See [Compiler support and evidence](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for supported cases and remaining limits.

## Module ownership

| Module | Responsibility |
| --- | --- |
| `src/S1Interop.Cli` | Parse commands, dispatch project and compiler operations, and format results. |
| `src/S1Interop.Compiler` | Prepare authoring references, map metadata, lower supported source operations, and generate runtime support. |
| `src/S1Interop.Core/Scaffolding` | Create compiler projects and their build imports. Also owns the explicit legacy generator scaffold. |
| `src/S1Interop.Core/Analysis` | Inspect existing projects and report migration risks. |
| `src/S1Interop.Core/Migration` | Plan and apply legacy migrations, record rollback metadata, and verify sandbox copies. |
| `src/S1Interop.Core/Rewriting` | Apply narrow legacy source transformations. |
| `src/S1Interop.Core/CodeGeneration` | Write declarations and helpers during legacy migration. |
| `src/S1Interop.Generators` | Emit additive source and diagnostics for projects using the legacy generator package. |

Keep command handlers thin. Compiler transformations belong in the compiler; project templates belong in Core scaffolding. Migration-time file edits and build-time source generation have separate owners.

## Legacy generator and migration

Existing generator projects use a different build flow:

```text
Source usage + local game references
  -> sdkgen writes declarations
  -> S1Interop.Generators reads declarations during compilation
  -> generated registries, facades, helpers, and diagnostics
  -> mod assembly
```

`sdkgen` writes declarations, not facade implementations. The Roslyn generator emits the implementations but cannot rewrite existing source. Legacy `migrate` commands can rewrite supported call sites before compilation.

Facade projects call generated `S1Interop.*` types that resolve game members at runtime. This experimental model has its own coverage limits and is not part of the default compiler project. The [Advanced documentation](https://ifbars.github.io/S1Interop/articles/advanced.html) retains its setup and reference material for existing users.

Migration plans must be reviewable before applying. Applied edits record backups and a manifest; verification runs against temporary project copies. Rewriters should be idempotent and leave ambiguous source unchanged with a diagnostic.

## Validation boundaries

- `tests/S1Interop.Compiler.Tests` checks compiler behavior against contract fixtures.
- `tests/S1Interop.Compiler.RuntimeSmoke` checks selected operations inside local Mono and IL2CPP game installs.
- `tests/S1Interop.Tests` covers CLI, scaffolding, analysis, migration, and generator behavior. Portable coverage excludes private game files; integration lanes use local mod copies.

A successful build establishes compile-time compatibility. Runtime probes establish only the behavior they exercise, and real-mod coverage must state which sources and features were tested. See [Testing](https://github.com/ifBars/S1Interop/blob/main/docs/TESTING.md) for commands and [Real-mod evidence](https://github.com/ifBars/S1Interop/blob/main/docs/REAL_MOD_EVIDENCE.md) for scope.

Game assemblies, generated game wrappers, local paths, and private test artifacts stay out of public packages and commits.
