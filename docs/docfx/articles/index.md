# S1Interop docs

S1Interop helps Schedule I mods work across Mono and IL2CPP. You can use it for a new project, an existing mod, or one troublesome direct game call. It does not require a full migration.

## Start with your situation

| I want to... | Read this next |
| --- | --- |
| Build a first mod | [Build your first mod](first-mod.md) |
| Understand what S1Interop owns | [What S1Interop does](introduction.md) |
| Add IL2CPP support to an existing mod | [Choose an adoption path](adoption-guide.md) |
| Keep manual runtime branches but add guardrails | [Diagnostics](diagnostics.md) |
| Use one experimental assembly on both backends | [Migrate to backend-neutral](migrate-to-backend-neutral.md) |
| Look up a command or option | [Commands](commands.md) |
| Resolve an error or unexpected result | [Troubleshooting](troubleshooting.md) |

## The recommended learning path

If this is your first Schedule I mod, use the pages in this order:

1. [Install S1Interop](getting-started.md)
2. [Build your first mod](first-mod.md)
3. [Common tasks](common-tasks.md)

The starter keeps Mono and IL2CPP outputs explicit. This is the supported starting point. Generated backend-neutral facades are an experimental opt-in after both reference builds and in-game tests are reliable.

## Learn only the part you need

- [Core concepts](core-concepts.md) explains the runtime model and the boundary between the CLI and generator.
- [Migration overview](migrating-mono-mods.md) explains planning, sandbox verification, and rollback.
- [Generated output](generator-package.md), [Declarations](backend-neutral-declarations.md), and [Diagnostics](diagnostics.md) cover the generator package.
- [S1API and S1Interop](s1api-and-s1interop.md) explains where low-level interop fits beside gameplay APIs.

The [API reference](api-reference.md) is for tools that call `S1Interop.Core` directly. It is not required to create or migrate a mod.
