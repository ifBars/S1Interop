# S1Interop docs

S1Interop helps a Schedule I mod call game code without spreading Mono and IL2CPP differences through the whole project. It can also inspect an existing mod, point out risky interop code, and test migrations in a temporary copy.

If this is your first mod, follow these pages in order:

1. [What S1Interop does](introduction.md)
2. [Install S1Interop](getting-started.md)
3. [Build your first mod](first-mod.md)
4. [Common tasks](common-tasks.md)

The first mod is intentionally tiny: it builds one DLL and prints the active backend when Schedule I loads it. The common tasks page then adds one generated game type and an IL2CPP reference check.

## Coming from an existing mod

- [Choose an adoption path](adoption-guide.md) before changing files.
- [Ways to use S1Interop](use-cases.md) if you only need diagnostics or dual-runtime builds.
- [Migration overview](migrating-mono-mods.md) for the backend-neutral and dual-runtime paths.
- [S1API and S1Interop](s1api-and-s1interop.md) if a higher-level API already owns most of the mod.

## CLI reference

The `s1interop` command handles project analysis, scaffolding, migration plans, file edits, and sandbox verification.

| Page | Use it when |
| --- | --- |
| [Commands](commands.md) | Command syntax and options. |
| [SDK generation](sdk-generation.md) | Generate declarations from source usage or local game metadata. |

## Generated code

The `S1Interop.Generators` package runs during compilation and emits the backend-neutral surface your mod uses.

| Page | Use it when |
| --- | --- |
| [Generated output](generator-package.md) | What appears after a build and why symbols may not exist yet. |
| [Backend-neutral SDK](backend-neutral-sdk.md) | The generated `S1Interop.ScheduleOne.*` facade and `Handle` model. |
| [Declarations](backend-neutral-declarations.md) | Attribute reference for types, members, patches, and bridges. |
| [Diagnostics](diagnostics.md) | Compiler diagnostics for declarations and IL2CPP boundary risks. |

## Project workflows

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
