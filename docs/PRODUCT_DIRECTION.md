# Product direction

S1Interop aims to let mod authors write ordinary game C# once and build it for Mono and IL2CPP without maintaining wrappers or runtime conditionals. The source compiler is the primary authoring workflow. The current implementation is experimental; known unsupported behavior prevents a claim of unrestricted compatibility.

## Primary developer experience

- One `s1interop` command creates projects, configures local installations, and hosts the compiler operations invoked by MSBuild.
- Plain `new` creates a compiler project with ordinary `ScheduleOne.*` source, a pinned local tool, and matching build imports.
- Authors restore that tool, configure matching game installations, and build separate Mono and IL2CPP outputs from the same source.
- Builds do not deploy or launch the game. Mod authors validate each runtime's actual behavior separately.
- Evaluate existing projects in controlled copies with explicit source and dependency inputs. Automatic adoption of arbitrary projects is still unfinished.

Follow [the first-mod walkthrough](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/first-mod.md) and [existing-mod compiler evaluation](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/compiler-adoption.md) for the implemented path. The installation guide builds the alpha.2 candidate from source. Published alpha.1 retains the earlier generator workflow.

## Architecture and coverage

Compiler adaptation comes from authoring and target metadata plus reusable language/runtime transformations. It must not become a manually maintained catalog of gameplay APIs. Higher-level APIs can own domain abstractions independently.

The compiler must preserve observable behavior across supported boundaries: identity, aliases, mutations, exceptions, lifetimes, and serialization. A transformation that compiles while losing those properties is insufficient. Unsupported constructs need actionable diagnostics and a reproducible case that can drive broader coverage.

Use unchanged real mods and separately compiled libraries as inputs. Keep author compilation, target compilation, contract execution, live game checks, and gameplay/multiplayer validation distinct. Passing a selected corpus does not establish full-game coverage or eliminate future compiler/runtime maintenance. Stripped native implementations and AOT limitations require explicit investigation rather than claims based on available metadata alone.

See [the source compiler guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for current support and limits. The delivery bar includes isolated package installation, project setup, both builds, and matching live evidence for runtime changes.

## Existing workflows

`analyze`, `lint`, migration previews, and reversible migration remain available for existing projects. They do not enable the source compiler automatically. `new --legacy-generator` creates the earlier helper-based project, and `new --backend-neutral` creates the opt-in facade experiment. Neither is the default compiler authoring model.

Existing generator and facade projects have their own reference documentation under [Advanced](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/advanced.md). They do not define the compiler authoring experience.
