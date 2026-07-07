# Use cases

S1Interop is not one migration mode. You can use one part, combine a few parts, or grow into a backend-neutral mod over time.

A common trap is treating S1Interop as "generate the SDK and rewrite everything." That is one path, but it is not the only useful one.

| What you want | Use | Skip |
| --- | --- | --- |
| Keep manual Mono/IL2CPP branches, but catch problems earlier. | `s1interop analyze`, `s1interop lint`, `s1interop build-hook`, and the `S1Interop.Generators` analyzer diagnostics. | `sdkgen`, generated facades, backend-neutral rewrites. |
| Start from a Mono mod and add separate IL2CPP builds. | `s1interop migrate . --dual-runtime`, local path props, solution/configuration updates, sandbox verification. | A full generated SDK unless you also want facades for specific direct game calls. |
| Ship one DLL that runs on both backends. | `s1interop init`, `s1interop sdkgen`, generated `S1Interop.ScheduleOne.*` facades, IL2CPP reference validation, in-game smoke tests. | Runtime-specific public release DLLs once the mod no longer needs them. |
| Patch game methods without writing backend-specific target resolution. | `S1InteropPatch`, `S1InteropPrefix`, `S1InteropPostfix`, patch target diagnostics, generated internal patch registration. | Manual `PatchAll` calls for those generated patch declarations. |
| Use a small generated helper but keep the rest of the mod as-is. | The generator package plus the specific helper surface, such as object casts, delegate bridges, Unity lookups, Steam P2P helpers, or a few `S1InteropMember` bindings. | Full SDK generation and source rewrites. |
| Explore the game API while prototyping. | `s1interop sdkgen . --full-sdk --apply` to seed broad type registration from local metadata. | Treating full SDK output as the final shape of a settled mod. |

## Diagnostics-only use

If you already prefer manual runtime branches, S1Interop can still be useful as a build-time guardrail.

Use this when you want the project to keep its own `#if MONO`, `#if IL2CPP`, or configuration-specific references, but you want common IL2CPP mistakes to fail in CI or show up in the IDE. The useful pieces are:

- `s1interop analyze .` for project shape, references, runtime defines, and source-risk reports;
- `s1interop lint .` for command-line checks that can run before build or in CI;
- `s1interop build-hook . --apply` when you want lint wired into MSBuild;
- `S1Interop.Generators` when you want Roslyn diagnostics such as declaration errors, managed collection boundaries, IL2CPP object-cast risks, Steam P2P byte-buffer risks, and patch target review warnings.

You do not need `sdkgen` for this path. You only add declarations when you want declaration diagnostics for a specific type/member, generated Harmony patch targets, event bridges, or a small helper facade.

## Dual-runtime use

Use dual-runtime migration when the mod should keep separate Mono and IL2CPP outputs for now.

This fits mods with runtime-specific dependencies, IL2CPP injected components, native wrapper differences, or maintainers who want explicit backend branches. The first goal is honest project shape: two builds, local game paths, correct references, and source-risk reports. Generated facades can come later, one direct game access point at a time.

## Backend-neutral use

Use backend-neutral generation when the goal is one assembly.

This path works best when the mod mostly needs direct game access, Harmony patch targets, simple member reads/writes, Unity object casts, delegate/event conversion, or Steamworks seams that S1Interop can hide. Keep Mono and IL2CPP validation builds even after the shipping output is one DLL.

## Mixed use

Most real mods are mixed.

An S1API content mod might keep S1API for items and saveables, use S1Interop diagnostics in CI, and add one generated facade for a Harmony patch. A SteamNetworkLib mod might keep its message protocol and sync vars, but use S1Interop for backend-neutral Steam IDs, P2P packet buffers, and Schedule One lobby lookup. A mature Mono mod might start with dual-runtime migration, then move only the worst direct game seams to generated facades.

Pick the part that removes the current pain. You can add more later.
