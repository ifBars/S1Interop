# Ways to use S1Interop

Most mods use only part of S1Interop. These combinations are normal.

## Ordinary game source across runtimes

The default candidate workflow uses the source compiler. Write ordinary Mono game source and build separate Mono and IL2CPP outputs; the compiler adapts supported runtime differences without facade declarations. Start with [a new mod](first-mod.md) or [an existing mod](compiler-adoption.md). The [support guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) records current limits; the goal of unrestricted compatibility is not yet achieved.

The routes below retain the older analysis, migration, helper, and facade surfaces for projects that use them.

## Guardrails without migration

Keep manual Mono and IL2CPP code. Run `analyze` to inspect the project and `lint` to report known risks. Add `build-hook` only when you want those checks in the build. You do not need generated facades for this path.

## Explicit dual-runtime builds

Use `migrate --dual-runtime` when the mod needs separate Mono and IL2CPP assemblies. This is often the right final shape for a mod with runtime-specific dependencies or code. Generated facades can remain a small, optional addition.

## Narrow backend-neutral helpers

Reference `S1Interop.Generators` and declare only the type, member, patch target, or bridge you need. This works well for a direct Harmony target, a cached reflection binding, or one shared game type. Do not turn on broad facade generation unless it reduces real duplicated code.

## Experimental one-DLL facades

Use usage-driven `sdkgen` when a narrow direct-game seam can move behind generated `S1Interop.ScheduleOne.*` facades. Keep explicit Mono and IL2CPP validation and a dual-runtime fallback until both branches have sustained in-game evidence.

## Local API exploration

`sdkgen --full-sdk` registers broad type coverage from local game metadata. It is useful while exploring, but it is not the default scaffold or a final production shape.

For commands and a decision table, return to [Start here](adoption-guide.md).
