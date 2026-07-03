# Testing

S1Interop uses an executable fixture harness instead of a framework-specific test runner.

## Test Modes

### Quick

Fast analyzer, rewriter, migration-planning, and generator checks:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --quick
```

Use this during normal iteration. It avoids MSBuild/package/build-gate fixtures and should be much faster than full portable coverage.

Generator diagnostics that validate `S1InteropType` and `S1InteropMember` strings use synthetic in-memory game assemblies here, so the compile-time safety checks stay portable while still proving the game-reference path.

### Portable

CI-safe coverage that does not require private local mod checkouts or local game installs:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --portable
```

Portable tests include slower MSBuild, packaging, sandbox verification, CLI, and build-hook fixtures where they can run without private dependencies.

### Integration

Local real-mod and game-path coverage:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration
```

Integration tests may depend on the broader local Schedule One workspace, sibling mod checkouts, and local Mono/IL2CPP game installs. They should not run in public CI unless those dependencies are deliberately provided.

Use focused lanes during normal local iteration:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-hoverboard
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-backend-neutral
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-build-gates
```

- `--integration-hoverboard`: fast real-mod coverage for `sdkgen --apply`, generated facade declarations, namespace-scoped SDK inference from local reference metadata, and compiling the CLI-generated SDK source against Mono and IL2CPP references.
- `--integration-backend-neutral`: broader real-mod backend-neutral coverage without the heaviest build-gate fixtures, including CLI-generated SDK compile checks for Hoverboard namespace imports and BarsGraphics source aliases.
- `--integration-build-gates`: slower real-mod build verification for Mono/IL2CPP migration gates.

Run the full `--integration` lane when a change crosses multiple migration domains or before a broad release-facing validation pass. Do not use it as the default iteration loop.

### All

Portable plus integration when the local workspace is available:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug
```

## Backend-Neutral Local Validation

Use the backend-neutral build validator for demo projects or new mods that should compile from one source tree against both reference surfaces:

```powershell
.\tests\Run-BackendNeutralBuildValidation.ps1 `
  -ProjectPath C:\Path\To\YourBackendNeutralMod\YourBackendNeutralMod.sln `
  -MonoGamePath "C:\Path\To\Schedule I_alternate" `
  -Il2CppGamePath "C:\Path\To\Schedule I_public" `
  -GeneratorPackageSource .\artifacts\packages
```

The script checks expected MelonLoader and Unity reference files before building. It does not launch the game or copy files into `Mods/`.

Use the runtime smoke runner when a backend-neutral mod logs deterministic probe markers:

```powershell
.\tests\Run-BackendNeutralRuntimeSmoke.ps1 `
  -ProjectPath C:\Path\To\YourBackendNeutralMod\YourBackendNeutralMod.sln `
  -Runtime Mono `
  -GamePath "C:\Path\To\Schedule I_alternate" `
  -GeneratorPackageSource .\artifacts\packages

.\tests\Run-BackendNeutralRuntimeSmoke.ps1 `
  -ProjectPath C:\Path\To\YourBackendNeutralMod\YourBackendNeutralMod.sln `
  -Runtime Il2Cpp `
  -GamePath "C:\Path\To\Schedule I_public" `
  -GeneratorPackageSource .\artifacts\packages
```

The default expected marker is `S1InteropSmoke|PASS|Backend=<Runtime>`. The runner builds the selected runtime surface, audits the local game path, deploys only the built mod DLL, launches `Schedule I.exe`, polls MelonLoader logs, and then removes the deployed DLL unless `-KeepDeployed` is passed. It refuses dirty `Mods/*.dll` folders unless `-AllowExtraMods` is explicit, and it refuses to launch into an already-running matching game process unless `-AllowExistingProcess` is explicit.

For build-only/audit-only checks, add `-NoLaunch`. Runtime logs are copied under `artifacts/runtime-smoke/`, which is ignored by git. Do not commit game assemblies, generated IL2CPP wrappers, logs, or copied game installs.

## CI-Equivalent Local Validation

GitHub Actions runs on Windows with .NET 8 and executes:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln --no-restore --configuration Release
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj --configuration Release --no-build -- --portable
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj --no-build --configuration Release --output .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj --no-build --configuration Release --output .\artifacts\packages
dotnet tool install S1Interop --tool-path .\.tools --add-source .\artifacts\packages --version 0.1.0-alpha.1
.\.tools\s1interop --help
```

Run the Release build and portable test steps locally before pushing changes that affect CLI packaging, source generators, build verification, or public command behavior.

## Fixture Organization

Test files are split by concern:

- `S1InteropFixtureTests.Runner.cs`: test mode dispatch.
- `S1InteropFixtureTests.SourceAndCliTests.cs`: source analysis, source rewrites, and CLI-facing fixture tests.
- `S1InteropFixtureTests.MigrationTests.cs`: migration planning/application and real-mod migration fixtures.
- `S1InteropFixtureTests.VerificationTests.cs`: sandbox build verification and build-hook fixtures.
- `S1InteropFixtureTests.GeneratorTests.cs`: Roslyn generator and backend-neutral runtime helper fixtures.
- `S1InteropFixtureTests.TestSupport.cs`: shared assertions, process helpers, fixture copying, reducers, and cleanup.

## Writing Tests

- Keep the test as close as possible to the behavior being changed.
- Add quick/portable coverage for pure analysis, rewrite, migration planning, and generator output.
- Add portable build-gate coverage for verifier behavior that can be modeled with synthetic projects.
- Add integration coverage for real open-source mods or local game assemblies.
- If a fixture requires private local state, skip cleanly or keep it in `--integration`.
- Prefer targeted integration modes for real-mod evidence. Add a new focused lane when a domain becomes slow enough that the full integration suite discourages regular use, and avoid duplicating expensive scaffold builds when a public CLI-output test already proves the same runtime surfaces.

## Temp Files and Real Projects

Tests must not mutate real sibling mod projects. Copy fixture directories into `%TEMP%\S1Interop.Tests\<guid>` or another temp folder, run the migration or verifier there, then delete the copy.

When manually debugging verifier sandboxes, clean these folders after use:

```text
%TEMP%\S1Interop.Tests\
%TEMP%\S1Interop.Verify.*
%TEMP%\S1Interop.Debug*
```

Do not commit generated test output, packaged artifacts, local props, game assemblies, or copied mod fixtures.
