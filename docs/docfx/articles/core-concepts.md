# Core concepts

Read this page when the task guides use a term that needs context. It explains the model, not every command or generated symbol.

## Mono, IL2CPP, and backend-neutral code

Schedule I exposes different C# surfaces on its Mono and IL2CPP branches. Mono code normally uses `ScheduleOne.*`. IL2CPP code uses generated wrapper types such as `Il2CppScheduleOne.*`. Direct casts, delegates, reflection, and Harmony targets can therefore need different code.

S1Interop supports two project shapes:

| Shape | What you ship | When it fits |
| --- | --- | --- |
| Dual-runtime | Separate Mono and IL2CPP assemblies from one source tree. | The mod has meaningful runtime-specific code, or you want the clearest validation boundary. |
| Backend-neutral | One assembly that uses generated `S1Interop.*` facades for selected direct game access. | The direct game seam is narrow and both runtime branches are already validated. This path is experimental. |

Dual-runtime is a complete outcome. Backend-neutral does not replace it by default. Even a backend-neutral project benefits from separate Mono and IL2CPP reference builds.

## Two packages, two jobs

| Package | Runs when | Owns |
| --- | --- | --- |
| `S1Interop` | You run `s1interop` in a terminal. | Project analysis, scaffolding, migration plans, reversible changes, and sandbox verification. |
| `S1Interop.Generators` | The mod project builds. | Compiler diagnostics and generated interop helpers or facades. |

The CLI never runs as part of a mod build. The generator package is a build dependency, not a player-installed runtime library.

## What a declaration does

A declaration is an assembly attribute that tells the generator which game type or member to resolve. `sdkgen` can write declarations from source usage and local metadata. You can add a narrow declaration by hand when automatic discovery cannot express the binding.

- `S1InteropNamespace` registers types from a namespace.
- `S1InteropType` requests a facade and compatible public members for one type.
- `S1InteropMember` binds an explicit, private, or ambiguous member.

Generated facades preserve the game namespace under `S1Interop`. For example, `ScheduleOne.PlayerScripts.PlayerCamera` becomes `S1Interop.ScheduleOne.PlayerScripts.PlayerCamera`.

Read [Declarations](backend-neutral-declarations.md) before editing attributes. Read [Generated output](generator-package.md) to see the symbols available after a build.

## Safe migration workflow

S1Interop treats migration as a reviewable sequence:

1. Analyze the project.
2. Review a dry-run plan.
3. Apply only the plan you accept.
4. Verify it in a temporary copy.
5. Use the recorded manifest to roll back an applied migration if needed.

Unsafe or ambiguous source patterns remain review items. S1Interop does not promise to convert every mod automatically. [Migration overview](migrating-mono-mods.md) covers the commands and outputs.

## Boundaries that stay outside S1Interop

S1Interop handles low-level game-wrapper access. It does not replace S1API gameplay workflows, MAPI building workflows, networking frameworks, or dedicated-server lifecycle APIs. Use it beside those libraries when a mod still needs direct Schedule One access.

It reads local game references but must not package or commit game assemblies, generated IL2CPP wrappers, decompiled code, or game assets. Keep install paths in the ignored `local.build.props` file. [Local game paths](local-paths.md) explains that setup.
