# Commands

Most commands default to the current directory when a path is optional.
Unknown options, missing option values, and invalid option values fail before command dispatch so migration typos do not silently fall back to defaults.

```text
s1interop analyze [path=.] [--configuration name] [--format text|json]
s1interop new <path> [--dry-run|--apply] [--format text|json]
s1interop init [path=.] [--dry-run|--apply] [--format text|json]
s1interop lint [path=.] [--configuration name] [--format text|json]
s1interop sdkgen [path=.] [--full-sdk] [--dry-run|--apply] [--format text|json]
s1interop build-hook [path=.] [--dry-run|--apply] [--format text|json]
s1interop migrate [path=.] [--dry-run|--apply] [--dual-runtime] [--format text|json]
s1interop verify-migration [path=.] [--dual-runtime] [--include-source-migrations] [--build] [--il2cpp-game-path path] [--mono-game-path path] [--build-timeout-seconds n] [--format text|json]
s1interop migrate rollback <manifest.json> [--format text|json]
s1interop --version
```

## Command roles

| Command | Use it for |
| --- | --- |
| `analyze` | Inspect projects, runtime references, configurations, packages, and source risks. |
| `new` | Create a backend-neutral project scaffold. |
| `init` | Add backend-neutral declarations and generator support to an existing project. |
| `lint` | Report issues using inferred project/runtime context. |
| `sdkgen` | Generate backend-neutral SDK declarations and facades. |
| `build-hook` | Add build-time validation hooks where supported. |
| `migrate` | Plan or apply dual-runtime migration changes. |
| `verify-migration` | Run migration plans in a disposable sandbox, optionally with builds. |

## Dry-run and apply

Commands that change files default to dry-run mode unless `--apply` is provided. Use the dry-run output to inspect planned operations before writing source, project, solution, props, or target files.

`sdkgen` is usage-driven by default. Add `--full-sdk` when seeding a blank backend-neutral project from local game reference metadata.

`verify-migration` always works in a temporary sandbox. It does not mutate the source project, and `--include-source-migrations` only changes what gets applied inside that sandbox.
