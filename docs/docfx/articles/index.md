# S1Interop docs

Build, migrate, analyze, and validate Schedule One mods that need direct game access on Mono, IL2CPP, or both.

S1Interop is modular. You can use diagnostics only, add dual-runtime build support, generate a few helper bindings, or move a mod toward one backend-neutral DLL.

## Start here

| Page | Use it when |
| --- | --- |
| [Introduction](introduction.md) | What S1Interop is and where it fits beside S1API, MAPI, and other modding libraries. |
| [Core concepts](core-concepts.md) | The vocabulary: facade, `Handle`, declarations, registry, and `S1InteropObject<TTag>`. |
| [Use cases](use-cases.md) | Pick the parts of S1Interop you actually need. |
| [Adoption guide](adoption-guide.md) | Pick a path for a new mod, existing Mono mod, S1API mod, or mixed Mono/IL2CPP project. |
| [S1API and S1Interop](s1api-and-s1interop.md) | Decide what stays in S1API and what should move behind generated interop. |
| [Installation](getting-started.md) | Build the local alpha packages and install the CLI. |
| [Local game paths](local-paths.md) | Configure local Mono and IL2CPP game references without committing machine paths. |

## CLI reference

The `s1interop` command handles project analysis, scaffolding, migration plans, file edits, and sandbox verification.

| Page | Use it when |
| --- | --- |
| [Commands](commands.md) | Command syntax and options. |
| [SDK generation](sdk-generation.md) | Generate declarations from source usage or local game metadata. |

## Generator package

The `S1Interop.Generators` package runs during compilation and emits the backend-neutral surface your mod uses.

| Page | Use it when |
| --- | --- |
| [Generated output](generator-package.md) | What appears after a build and why symbols may not exist yet. |
| [Backend-neutral SDK](backend-neutral-sdk.md) | The generated `S1Interop.ScheduleOne.*` facade and `Handle` model. |
| [Declarations](backend-neutral-declarations.md) | Attribute reference for types, members, patches, and bridges. |
| [Diagnostics](diagnostics.md) | Compiler diagnostics for declarations and IL2CPP boundary risks. |

## Workflows

End-to-end paths that combine CLI commands and generated code.

| Page | Use it when |
| --- | --- |
| [New projects](new-projects.md) | Start a backend-neutral MelonLoader mod. |
| [Migration overview](migrating-mono-mods.md) | Choose backend-neutral or dual-runtime migration. |
| [Migrate to backend-neutral](migrate-to-backend-neutral.md) | Move shared direct game access behind generated facades. |
| [Migrate to dual-runtime](migrate-to-dual-runtime.md) | Keep separate Mono and IL2CPP outputs from one source tree. |

## API reference

The generated API reference covers stable `S1Interop.Core` models and library entry points. Use [API reference](api-reference.md) when embedding S1Interop in another tool or reading result contracts.

## Troubleshooting

| Page | Use it when |
| --- | --- |
| [Common issues](troubleshooting.md) | Missing generated symbols, failed restores, stale IDE state, or migration rollback. |
| [FAQ](faq.md) | You have questions about migration paths, package usage, alpha limitations, or the safety model. |

For architecture, testing, publishing, and contributing, see [Contributors](../contributors/index.md).
