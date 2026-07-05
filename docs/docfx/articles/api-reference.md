# API reference

The API reference is for tooling integrations. It is not the main path for writing mods.

Most developers should use the CLI and generated SDK docs first:

- [New projects](new-projects.md) for backend-neutral scaffolding.
- [Backend-neutral SDK](backend-neutral-sdk.md) for generated facades.
- [Migration](migrating-mono-mods.md) for existing Mono mods.
- [Diagnostics](diagnostics.md) for compile-time checks.

The generated reference only includes stable `S1Interop.Core` contracts:

- analysis and diagnostic models;
- migration planning, apply, rollback, and verification result models;
- entry points for analysis, planning, applying, verification, and project scaffolding.

It skips CLI formatting code, Roslyn generator internals, source rewriters, and implementation helpers. Those pieces are still expected to change during alpha work.

Start with <xref:S1Interop.Core> when you need the library surface.
