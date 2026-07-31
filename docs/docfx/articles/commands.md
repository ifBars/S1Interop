# Commands

This is the CLI reference for the `s1interop` command. The `S1Interop` package is a .NET global tool; it does not run during compilation. The compile-time generator package has its own reference under [Generated output](generator-package.md) and [Declarations](backend-neutral-declarations.md).

Most commands default to the current directory when a path is optional.
Unknown options, missing option values, and invalid option values fail before command dispatch so migration typos do not silently fall back to defaults.

Use only the commands that fit your project. `analyze`, `lint`, and `build-hook` are useful even when you keep manual Mono/IL2CPP code. `migrate --dual-runtime` can add separate runtime builds without requiring a generated SDK. `sdkgen` is for projects that want generated facade declarations.

```text
s1interop doctor [path=.] [--mono-game-path path] [--il2cpp-game-path path] [--format text|json]
s1interop setup [path=.] [--mono-game-path path] [--il2cpp-game-path path] [--dry-run|--apply] [--format text|json]
s1interop analyze [path=.] [--configuration name] [--format text|json]
s1interop new <path> [--backend-neutral] [--dry-run|--apply] [--format text|json]
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
| `doctor` | Detect and validate a project, local game references, MelonLoader surfaces, and ignored local configuration. It is always read-only. |
| `setup` | Preview or write only `local.build.props`. It refuses missing prerequisites, unignored targets, and existing local configuration. It never installs software or edits committed project files. |
| `analyze` | Inspect projects, runtime references, configurations, packages, and source risks without changing files. |
| `new` | Create the recommended explicit Mono/IL2CPP project scaffold. `--backend-neutral` selects the experimental one-DLL facade scaffold. |
| `init` | Add a declaration file and generator support to an existing project. |
| `lint` | Report issues using inferred project/runtime context. Useful for diagnostics-only adoption. |
| `sdkgen` | Generate SDK declarations and facades when you want generated game access. |
| `build-hook` | Add build-time validation hooks where supported. Useful when you keep manual runtime branches. |
| `migrate` | Plan or apply migration changes. Use `--dual-runtime` for separate Mono and IL2CPP builds. |
| `verify-migration` | Run migration plans in a disposable sandbox, optionally with builds. |

## Dry-run and apply

Commands that change files default to dry-run mode unless `--apply` is provided. Use the dry-run output to inspect planned operations before writing source, project, solution, props, or target files.

`--dry-run` and `--apply` are mutually exclusive. Passing both is an error.

`setup --apply` is intentionally narrower than migration commands: it writes only an ignored `local.build.props` and never overwrites an existing one.

`sdkgen` is an experimental facade workflow and is usage-driven by default. Add `--full-sdk` only for broad local exploration from local game reference metadata.

`verify-migration` always works in a temporary sandbox. It does not mutate the source project, and `--include-source-migrations` only changes what gets applied inside that sandbox.
