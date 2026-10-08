# Commands

The `S1Interop` tool provides one `s1interop` command for project creation, setup, and compilation. Compiler projects pin it in a local tool manifest, and MSBuild invokes its `compiler` command group during builds. See [Build your first mod](first-mod.md) for the primary workflow. These commands describe the unpublished alpha.2 candidate; published alpha.1 uses the earlier generator workflow.

Most commands default to the current directory when a path is optional.
Unknown options, missing option values, and invalid option values fail before command dispatch so migration typos do not silently fall back to defaults.

Start with `new`, restore the project's tool, and use `setup` or the generated local configuration example to select installations. Run commands inside the project with `dotnet tool run s1interop -- <command>` to select its pinned version. Ordinary `dotnet build` commands run the compiler automatically.

The earlier analysis and generator commands remain available for existing projects. `analyze`, `lint`, and `build-hook` support manual runtime branches. `migrate --dual-runtime` changes project settings and source using the migration workflow; it does not enable the source compiler. `sdkgen` creates facade declarations.

```text
s1interop compiler --help
s1interop doctor [path=.] [--mono-game-path path] [--il2cpp-game-path path] [--format text|json]
s1interop setup [path=.] [--mono-game-path path] [--il2cpp-game-path path] [--dry-run|--apply] [--format text|json]
s1interop analyze [path=.] [--configuration name] [--format text|json]
s1interop new <path> [--dry-run|--apply] [--format text|json]
s1interop new <path> <--legacy-generator|--backend-neutral> [--dry-run|--apply] [--format text|json]
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
| `compiler` | Build operations for reference preparation, installation verification, and source lowering. Normally invoked by MSBuild. |
| `doctor` | Detect and validate a project, local game references, MelonLoader surfaces, and ignored local configuration. It is always read-only. |
| `setup` | Preview or write only `local.build.props`. It refuses missing prerequisites, unignored targets, and existing local configuration. It never installs software or edits committed project files. |
| `analyze` | Inspect projects, runtime references, configurations, packages, and source risks without changing files. |
| `new` | Create a compiler-enabled project with ordinary game source and pinned local tooling. `--legacy-generator` retains the earlier helper scaffold; `--backend-neutral` selects the separate one-DLL facade experiment. |
| `init` | Add a declaration file and generator support to an existing project. |
| `lint` | Report issues using inferred project/runtime context. Useful for diagnostics-only adoption. |
| `sdkgen` | Write declarations for the experimental facade SDK; the generator package emits their implementations during compilation. |
| `build-hook` | Add build-time validation hooks where supported. Useful when you keep manual runtime branches. |
| `migrate` | Plan or apply migration changes. Use `--dual-runtime` for separate Mono and IL2CPP builds. |
| `verify-migration` | Run migration plans in a disposable sandbox, optionally with builds. |

## Compiler build commands

The source checkout uses the same `s1interop` executable for automatic compiler adaptation:

```batch
s1interop compiler --help
```

MSBuild invokes `compiler prepare-references`, `compiler verify-installations`, and `compiler lower` using explicit file manifests and output paths. These are build operations, not migration previews: preparation and lowering write their designated generated outputs. The published alpha.1 package does not contain these commands. See the [source compiler guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for compiler behavior and compatibility limits.

## Dry-run and apply

Project setup and migration commands default to dry-run mode unless `--apply` is provided. Use the dry-run output to inspect planned operations before writing source, project, solution, props, or target files. Compiler build commands write generated outputs directly, as described above.

`--dry-run` and `--apply` are mutually exclusive. Passing both is an error.

`setup --apply` is intentionally narrower than migration commands: it writes only an ignored `local.build.props` and never overwrites an existing one.

`sdkgen` is an experimental facade workflow and is usage-driven by default. Add `--full-sdk` only for broad local exploration from local game reference metadata.

`verify-migration` always works in a temporary sandbox. It does not mutate the source project, and `--include-source-migrations` only changes what gets applied inside that sandbox.

## Build-hook command location

The committed `S1Interop.Build.targets` defaults to the installed `s1interop` command. An ignored `S1Interop.Build.local.props` can override it. When running from a built S1Interop checkout, new local props point at that built CLI assembly instead of rebuilding the CLI during every mod build.

Rebuild S1Interop after changing its source. If you move the checkout or use an older local props file containing `dotnet run --project`, update `S1InteropCommand` in that ignored file to the installed command or the current built CLI. For example:

```xml
<S1InteropCommand>dotnet &quot;C:\Path\To\S1Interop\src\S1Interop.Cli\bin\Release\net8.0\S1Interop.Cli.dll&quot;</S1InteropCommand>
```
