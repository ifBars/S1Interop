# Migrating Mono mods

Use migration when a project already builds against Mono and you want to move it toward dual-runtime or backend-neutral support.

Start with analysis:

```powershell
s1interop analyze .
```

Then run a dry-run migration:

```powershell
s1interop migrate . --dual-runtime --dry-run
```

Review the plan before applying. A migration may:

- add IL2CPP build configurations;
- update a sibling `.sln`;
- create ignored local path props;
- install the generator package reference;
- generate SDK facade declarations;
- rewrite safe source patterns;
- write a source-risk report for cases that still need review.

Apply only when the plan is reasonable:

```powershell
s1interop migrate . --dual-runtime --apply
```

## Rollback

Applied migrations write backups and a manifest under `s1interop-runs/<run-id>/`.

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

## Sandbox verification

Use verification before touching a real mod tree when possible:

```powershell
s1interop verify-migration . --dual-runtime --include-source-migrations
```

Build-gated verification can also compile both runtimes when local game paths are available:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "<your Mono Schedule I install>" `
  --il2cpp-game-path "<your IL2CPP Schedule I install>"
```

