# Getting started

Build the local alpha packages, install the `s1interop` CLI, then run `analyze` on a mod or scaffold a backend-neutral project.

You need the .NET SDK (8.0 or later) installed before running these commands.

## 1. Build from source

From the S1Interop repository:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln
```

This builds the CLI, generator, and core library. Warnings are expected in an alpha build; errors are not.

## 2. Pack the alpha packages

Pack the CLI and generator into the same local feed, then install the tool from that feed.

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
```

> [!NOTE]
> Both packages must live in the same local feed. The CLI tool and the generator package are separate NuGet packages, but projects created or migrated by the CLI need to restore the generator from the same source. Keep both in `artifacts\packages` while you are testing local alpha builds.

## 3. Verify installation

Check the installed tool:

```powershell
.\.tools\s1interop --version
.\.tools\s1interop --help
```

If the tool is not found, check that `--tool-path` matches the install command.

## 4. Run your first command

Run `analyze` first on existing mods. It reports project shape and source risks without writing files.

```powershell
.\.tools\s1interop analyze .
```

For a new backend-neutral mod:

```powershell
.\.tools\s1interop new .\MyBackendNeutralMod --apply
```

Review dry-run output before adding `--apply`:

```powershell
.\.tools\s1interop new .\MyMod --dry-run
```

If you are not sure which command fits your situation, use the [Adoption guide](adoption-guide.md) before applying changes.

## Next steps

- [New projects](new-projects.md): scaffold a backend-neutral mod project from scratch and generate your first SDK declarations.
- [Migrate to backend-neutral](migrate-to-backend-neutral.md): migrate an existing Mono mod to a single assembly that runs on both Mono and IL2CPP.
- [Migrate to dual-runtime](migrate-to-dual-runtime.md): move an existing Mono mod toward separate Mono and IL2CPP assemblies from shared source.

## Local alpha packages

S1Interop ships as two local alpha packages:

- `S1Interop`: the CLI tool that provides the `s1interop` command.
- `S1Interop.Generators`: the Roslyn generator/analyzer package restored by backend-neutral or migrated mod projects.

Keep both packages in the same local feed, usually `artifacts/packages`. Projects created or migrated by the tool can restore the local generator package through `local.build.props`:

```xml
<S1InteropGeneratorPackageSource>...\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
```

Generated projects bridge that property into `RestoreAdditionalProjectSources`, so Visual Studio, Rider, and `dotnet build` can restore the local generator without committing a machine-specific package source.

See [Introduction](introduction.md) for the CLI/generator split, [Generated output](generator-package.md) for emitted symbols, and [Commands](commands.md) for the full command list.
