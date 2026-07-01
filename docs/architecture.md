# S1Interop Architecture

S1Interop is split into three layers.

## CLI and Core

`S1Interop.Cli` handles command parsing and output. `S1Interop.Core` owns analysis, migration planning, source rewrites, rollback, verification, and migration-time code generation.

The CLI generator layer lives in `S1Interop.Core.Generators`. It writes files into the target mod project, including:

- SDK/global-using facades
- UnityEvent and delegate bridges
- Harmony method target declarations
- member access target declarations
- source-risk reports

These generators can inspect the project on disk and can be paired with source rewrites because they run as part of `s1interop migrate`.

## Roslyn Generators

`S1Interop.Generators` is the compile-time analyzer/source-generator package that migrated mods reference privately. It emits additive source from attributes such as `S1InteropType` and `S1InteropMember`.

Roslyn source generators can add new source files and diagnostics. They cannot mutate existing user source or rewrite IL. That is why S1Interop currently combines Roslyn generation with explicit source migration for call sites that should use generated helpers.

## Single-Assembly Compatibility

The long-term goal is to make more mods build into one assembly that can run on Mono and IL2CPP without developers hand-writing every conditional. The realistic path is layered:

1. Generate backend-aware facades and reflection caches.
2. Rewrite safe source call sites to use those generated APIs.
3. Keep unsupported or ambiguous runtime differences in source-risk reports.
4. Consider IL weaving only after the generated API surface is stable and there is enough verification against real mods.

IL weaving could eventually rewrite compiled call sites to generated backend-specific adapters, but it is not the same as Roslyn source generation and should be treated as a separate backend with its own tests, rollback story, and compatibility checks.
