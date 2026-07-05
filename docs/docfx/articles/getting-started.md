# Getting started

Build S1Interop from source:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln
```

Run the CLI from source:

```powershell
dotnet run --project .\src\S1Interop.Cli\S1Interop.Cli.csproj -- analyze .
```

Pack the local alpha packages when you want the `s1interop` command and local generator restores:

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
.\.tools\s1interop --help
.\.tools\s1interop --version
```

## Local alpha packages

S1Interop currently ships as two packages:

- `S1Interop`: the CLI tool that provides the `s1interop` command.
- `S1Interop.Generators`: the Roslyn generator/analyzer package restored by backend-neutral or migrated mod projects.

When testing unpublished local builds, keep both packages in the same local feed, usually `artifacts/packages`. Projects created or migrated by the tool can restore the local generator package through `local.build.props`:

```xml
<S1InteropGeneratorPackageSource>...\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
```

Generated projects already bridge that property into `RestoreAdditionalProjectSources`, so Visual Studio, Rider, and `dotnet build` can restore the local generator package without committing a machine-specific package source.

## First commands

Analyze a project:

```powershell
s1interop analyze .
```

Create a backend-neutral mod:

```powershell
s1interop new .\MyBackendNeutralMod --apply
```

Generate SDK declarations from existing source usage:

```powershell
s1interop sdkgen . --apply
```

Seed a blank backend-neutral project from local game references:

```powershell
s1interop sdkgen . --full-sdk --apply
```

Plan a dual-runtime migration:

```powershell
s1interop migrate . --dual-runtime --dry-run
```

Verify a migration in a sandbox:

```powershell
s1interop verify-migration . --dual-runtime
```

Use `--apply` only after the dry-run output makes sense.
