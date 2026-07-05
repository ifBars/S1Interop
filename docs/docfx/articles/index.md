# S1Interop docs

Use these docs when you are building, migrating, or validating a Schedule One mod with S1Interop.

## Start here

| Page | Use it when |
| --- | --- |
| [Introduction](introduction.md) | You want the short version of what S1Interop is and what it is not. |
| [Getting started](getting-started.md) | You need the first commands for building and running the CLI. |
| [Local game paths](local-paths.md) | You are setting up Mono and IL2CPP game references on your machine. |

## Backend-neutral work

| Page | Use it when |
| --- | --- |
| [New projects](new-projects.md) | You want to start from a backend-neutral scaffold. |
| [Backend-neutral SDK](backend-neutral-sdk.md) | You want to understand the generated `S1Interop.ScheduleOne.*` facade model. |
| [SDK generation](sdk-generation.md) | You need to run `sdkgen` or decide between narrow and full SDK generation. |

## Migration and verification

| Page | Use it when |
| --- | --- |
| [Migrating Mono mods](migrating-mono-mods.md) | You are moving an existing Mono-only mod toward dual-runtime support. |
| [Diagnostics](diagnostics.md) | You want to understand compiler errors and IL2CPP boundary checks. |
| [Commands](commands.md) | You need the CLI command list and what each command is for. |
| [Testing](testing.md) | You are validating S1Interop itself or a real-mod migration probe. |

## API reference

The generated API reference is limited to stable `S1Interop.Core` models and library entry points. Use [API reference](api-reference.md) when you are embedding S1Interop in another tool or reading its result contracts.
