# Getting started

S1Interop is currently distributed as local alpha packages that you build and pack from source. This guide installs the `s1interop` CLI so you can analyze an existing mod or scaffold a backend-neutral one.

You need the .NET SDK (8.0 or later) installed before running these commands.

## 1. Build from source

Clone the S1Interop repository, then restore dependencies and build the solution.

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln
```

A successful build compiles the CLI, the Roslyn generator, and the core library. Warnings are expected in an alpha build; errors are not.

## 2. Pack the alpha packages

Pack both the CLI project and the generator project into a shared local feed at `artifacts\packages`, then install the CLI tool from that feed.

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
```

> [!NOTE]
> Both packages must live in the same local feed. The CLI tool and the generator package are separate NuGet packages, but projects created or migrated by the CLI need to restore the generator from the same source. Keep both in `artifacts\packages` while you are testing local alpha builds.

## 3. Verify installation

Confirm that the tool installed correctly and check its version.

```powershell
.\.tools\s1interop --version
.\.tools\s1interop --help
```

The command should print `0.1.0-alpha.1` and the command list. If the tool is not found, check that `--tool-path` matches the directory from the install command.

## 4. Run your first command

Point the CLI at a mod project directory to analyze it, or scaffold a new backend-neutral mod. Run `analyze` first to get a risk report without changing anything.

```powershell
.\.tools\s1interop analyze .
```

To scaffold a new backend-neutral mod project:

```powershell
.\.tools\s1interop new .\MyBackendNeutralMod --apply
```

Always review the dry-run output before adding `--apply`. Run `s1interop new .\MyMod --dry-run` to preview what the scaffold will create without writing any files.

If you are not sure which command fits your situation, use the [Adoption guide](adoption-guide.md) before applying changes.

## Next steps

You now have a working `s1interop` installation. Choose your path based on whether you are starting a new mod or migrating an existing one.

- [New projects](new-projects.md): scaffold a backend-neutral mod project from scratch and generate your first SDK declarations.
- [Migrate to backend-neutral](migrate-to-backend-neutral.md): migrate an existing Mono mod to a single assembly that runs on both Mono and IL2CPP.
- [Migrate to dual-runtime](migrate-to-dual-runtime.md): move an existing Mono mod toward separate Mono and IL2CPP assemblies from shared source.

## Local alpha packages

S1Interop currently ships as two packages:

- `S1Interop`: the CLI tool that provides the `s1interop` command.
- `S1Interop.Generators`: the Roslyn generator/analyzer package restored by backend-neutral or migrated mod projects.

When testing unpublished local builds, keep both packages in the same local feed, usually `artifacts/packages`. Projects created or migrated by the tool can restore the local generator package through `local.build.props`:

```xml
<S1InteropGeneratorPackageSource>...\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
```

Generated projects already bridge that property into `RestoreAdditionalProjectSources`, so Visual Studio, Rider, and `dotnet build` can restore the local generator package without committing a machine-specific package source.

For a conceptual overview of what each package does and when, see [Introduction](introduction.md). For what the generator emits and when symbols appear, see [Generated output](generator-package.md). For the full command list, see [Commands](commands.md).
