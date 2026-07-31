# Ways to use S1Interop

Most mods use only part of S1Interop. These combinations are normal.

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

For commands and a decision table, return to [Choose an adoption path](adoption-guide.md).
