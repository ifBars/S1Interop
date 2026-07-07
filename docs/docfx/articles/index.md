# S1Interop docs

Use these docs when you are building, migrating, or validating your Schedule One mod with S1Interop.

## Start here

| Page | Use it when |
| --- | --- |
| [Introduction](introduction.md) | You want the short version of what S1Interop is, including the CLI vs generator split. |
| [Core concepts](core-concepts.md) | You want the vocabulary (`S1InteropObject<TTag>`, facade, `Handle`, declarations, registry) defined once before reading the workflow pages. |
| [Adoption guide](adoption-guide.md) | You need to choose between a first-time mod scaffold, existing mod migration, SDK generation, or sandbox verification. |
| [S1API and S1Interop](s1api-and-s1interop.md) | You know S1API and want to understand why S1Interop exists, where the overlap is, and which layer should own your modding work. |
| [Installation](getting-started.md) | You need the first commands for building and running the CLI, or packing the local alpha packages. |
| [Local game paths](local-paths.md) | You are setting up Mono and IL2CPP game references on your machine. |

## CLI reference

The `s1interop` command handles project-level work: analysis, scaffolding, migration plans, file edits, and sandbox verification.

| Page | Use it when |
| --- | --- |
| [Commands](commands.md) | You need the `s1interop` command list and what each command is for. |
| [SDK generation](sdk-generation.md) | You need to run `sdkgen` or choose between narrow and full SDK generation. |

## Generator package

The `S1Interop.Generators` package runs during compilation. Use these pages when you need to know which generated symbols should appear in your mod and which declarations control them.

| Page | Use it when |
| --- | --- |
| [Generated output](generator-package.md) | You want to know what the generator emits, when it runs, and which symbols appear after a build. |
| [Backend-neutral SDK](backend-neutral-sdk.md) | You want to understand the generated `S1Interop.ScheduleOne.*` facade model. |
| [Declarations](backend-neutral-declarations.md) | You need the attribute reference for `S1InteropType`, `S1InteropNamespace`, `S1InteropMember`, and bridge attributes. |
| [Diagnostics](diagnostics.md) | You want to understand compiler errors and IL2CPP boundary checks. |

## Workflows

End-to-end paths that combine CLI commands and the generator package.

| Page | Use it when |
| --- | --- |
| [New projects](new-projects.md) | You want to start from a backend-neutral scaffold. |
| [Migration overview](migrating-mono-mods.md) | You need to choose between backend-neutral and dual-runtime migration. |
| [Migrate to backend-neutral](migrate-to-backend-neutral.md) | You want one assembly backed by generated `S1Interop.*` facades. |
| [Migrate to dual-runtime](migrate-to-dual-runtime.md) | You want separate Mono and IL2CPP assemblies/configurations. |

## API reference

The generated API reference is limited to stable `S1Interop.Core` models and library entry points. Use [API reference](api-reference.md) when you are embedding S1Interop in another tool or reading result contracts.

## Troubleshooting

| Page | Use it when |
| --- | --- |
| [Common issues](troubleshooting.md) | Something is silently wrong — missing generated symbols, failed restores, stale IDE state, migration rollback. |
| [FAQ](faq.md) | You have questions about migration paths, package usage, alpha limitations, or the safety model. |

For architecture, testing, publishing, and contributing, see [Contributors](../contributors/index.md).
