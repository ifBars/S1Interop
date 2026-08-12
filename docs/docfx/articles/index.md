# S1Interop documentation

S1Interop helps Schedule I mods work across Mono and IL2CPP. It supports gradual adoption: use one read-only check, add explicit dual-runtime builds, or experiment with a narrow generated facade.

## Begin with one route

[Start here](adoption-guide.md) and choose your situation: new to Schedule I modding, already maintaining a mod, or already looking for a specific feature. That page is the single entry point for setup and adoption decisions.

## Browse by documentation type

- **Tutorials:** [Install S1Interop](getting-started.md) and [build your first mod](first-mod.md).
- **Guides:** [Common tasks](common-tasks.md), [local game paths](local-paths.md), and [migration](migrating-mono-mods.md).
- **Concepts:** [What S1Interop does](introduction.md), [core concepts](core-concepts.md), and [architecture](architecture.md).
- **Reference:** [Commands](commands.md), [SDK generation](sdk-generation.md), and the [Core API](api-reference.md).
- **Help:** [Troubleshooting](troubleshooting.md) and [FAQ](faq.md).

The API reference is for tools that call `S1Interop.Core` directly. Mod authors can ignore it unless they are building their own automation or integration.
