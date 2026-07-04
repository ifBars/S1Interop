# SDK generation

`sdkgen` writes the generated declarations that power the backend-neutral SDK.

For existing source, use the narrow mode:

```powershell
s1interop sdkgen . --apply
```

This inspects source usage, aliases, namespace imports, string-held game type names, and local reference metadata. It generates the types the project appears to need.

For a blank or exploratory project, use the full SDK mode:

```powershell
s1interop sdkgen . --full-sdk --apply
```

This seeds declarations for discoverable Schedule One types from the configured local reference surface.

## What gets generated

Generated SDK source can include:

- `S1InteropType` declarations;
- root-preserving facades under `S1Interop.ScheduleOne.*`;
- runtime type registry entries;
- bridge helpers for Unity events or delegate conversion;
- compiler diagnostics for known IL2CPP boundary failures.

The generated SDK comes from local reference metadata. Do not commit game assemblies, generated IL2CPP wrappers, decompiled source, or static hand-maintained catalogs of Schedule One APIs.

## When to add manual overrides

Prefer generated type coverage first.

Add explicit member overrides only when a binding cannot safely come from metadata:

- the member is private or internal;
- an overload needs explicit parameter names or by-ref markers;
- Mono and IL2CPP disagree in a way that needs a pinned binding;
- a migration found reflection code that needs a stable generated target.

