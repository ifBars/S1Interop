# Testing

S1Interop has separate executable harnesses for compiler contracts and CLI, migration, and generator fixtures. Run them from the repository root with the .NET 8 SDK.

## Compiler workflow

Run compiler contracts independently of the older migration/generator fixtures:

```powershell
dotnet run --project tests/S1Interop.Compiler.Tests -c Release -- --filter Reflection
dotnet run --project tests/S1Interop.Compiler.Tests -c Release --no-build
```

Use `--list` to discover compiler cases. See [Release readiness](https://github.com/ifBars/S1Interop/blob/main/docs/RELEASING.md) for isolated package installation and the optional real-reference build lane. The [source compiler guide](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) documents controlled real-mod source evaluation and dedicated live probes. Keep compiler contracts, reference builds, menu probes and gameplay evidence distinct.

## Migration and generator test modes

### Focused fixtures

The repository pins the .NET 8 SDK family in `global.json`. Build hooks execute the built CLI rather than starting a nested source build. Install a .NET 8 SDK even when newer SDKs are available.

After building, list fixtures or select a case-insensitive name fragment:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Release --no-build -- --list-tests
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Release --no-build -- --filter SetupSupportsEitherRuntime
```

An unmatched filter fails instead of reporting success with zero tests. Filters can select local integration fixtures too; choose a portable fixture when game references are unavailable. Use focused runs while iterating, then run the complete portable lane once the affected tests pass.

### Quick

Fast analyzer, rewriter, migration-planning, and generator checks:

```batch
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --quick
```

Use this during normal iteration. It avoids MSBuild/package/build-gate fixtures and should be much faster than full portable coverage.

Generator diagnostics that validate `S1InteropType` declarations and `S1InteropMember` overrides use synthetic game assemblies here, so the compile-time safety checks stay portable while still proving the game-reference path, including method overload parameter checks. The quick lane also covers IL2CPP source-boundary diagnostics for transpilers, managed collection callback signatures, managed byte-buffer fill calls, and object/proxy casts. It also covers the blank-project flow by creating a backend-neutral scaffold, pointing it at a synthetic local game reference, and applying `sdkgen --full-sdk`.

### Portable

CI-safe coverage that does not require private local mod checkouts or local game installs:

```batch
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --portable
```

Portable tests include slower MSBuild, packaging, sandbox verification, CLI, and build-hook fixtures where they can run without private dependencies.

### Integration

Local real-mod and game-path coverage:

```batch
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration
```

Integration tests may depend on the broader local Schedule One workspace, sibling mod checkouts, and local Mono/IL2CPP game installs. They should not run in public CI unless those dependencies are deliberately provided.

Use focused lanes during normal local iteration:

```batch
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-hoverboard
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-backend-neutral
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-build-gates
```

- `--integration-hoverboard`: fast real-mod coverage for `sdkgen --apply`, generated facade declarations, namespace-scoped SDK inference from local reference metadata, and compiling the CLI-generated SDK source against Mono and IL2CPP references.
- `--integration-backend-neutral`: broader real-mod backend-neutral coverage without the heaviest build-gate fixtures, including CLI-generated SDK compile checks for Hoverboard namespace imports, BarsGraphics source aliases and string-held game type names, and S1FuelMod migration-generated member-access declarations.
- `--integration-build-gates`: slower real-mod build verification for Mono/IL2CPP migration gates.

Run the full `--integration` lane when a change crosses multiple migration domains or before a broad release-facing validation pass. Do not use it as the default iteration loop.

See [Real-mod evidence](https://github.com/ifBars/S1Interop/blob/main/docs/REAL_MOD_EVIDENCE.md) for dated runs and the limits of those lanes.

### All

Portable plus integration when the local workspace is available:

```batch
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug
```

## Legacy facade validation

These runners validate experimental facade projects created with `s1interop new --backend-neutral`. They do not validate the default compiler scaffold.

```batch
powershell -NoProfile -File .\tests\Run-BackendNeutralBuildValidation.ps1 ^
  -ProjectPath C:\Path\To\YourBackendNeutralMod\YourBackendNeutralMod.sln ^
  -MonoGamePath "C:\Path\To\Schedule I_alternate" ^
  -Il2CppGamePath "C:\Path\To\Schedule I_public" ^
  -GeneratorPackageSource .\artifacts\packages
```

The script checks expected MelonLoader and Unity reference files before building. It does not launch the game or copy files into `Mods/`.

Use the runtime smoke runner when a backend-neutral mod logs deterministic probe markers:

```batch
powershell -NoProfile -File .\tests\Run-BackendNeutralRuntimeSmoke.ps1 ^
  -ProjectPath C:\Path\To\YourBackendNeutralMod\YourBackendNeutralMod.sln ^
  -Runtime Mono ^
  -GamePath "C:\Path\To\Schedule I_alternate" ^
  -GeneratorPackageSource .\artifacts\packages

powershell -NoProfile -File .\tests\Run-BackendNeutralRuntimeSmoke.ps1 ^
  -ProjectPath C:\Path\To\YourBackendNeutralMod\YourBackendNeutralMod.sln ^
  -Runtime Il2Cpp ^
  -GamePath "C:\Path\To\Schedule I_public" ^
  -GeneratorPackageSource .\artifacts\packages
```

The default expected marker is `S1InteropSmoke|PASS|Backend=<Runtime>`. The runner builds the selected runtime surface, audits the local game path, deploys only the built mod DLL, launches `Schedule I.exe`, polls MelonLoader logs, and then removes the deployed DLL unless `-KeepDeployed` is passed. It refuses dirty `Mods/*.dll` folders unless `-AllowExtraMods` is explicit, and it refuses to launch into an already-running matching game process unless `-AllowExistingProcess` is explicit.

For build-only/audit-only checks, add `-NoLaunch`. Runtime logs are copied under `artifacts/runtime-smoke/`, which is ignored by git. Do not commit game assemblies, generated IL2CPP wrappers, logs, or copied game installs.

## CI and release checks

Pull requests and pushes to `main` run two independent Windows workflows with .NET 8:

| Workflow | Checks |
| --- | --- |
| [CI](https://github.com/ifBars/S1Interop/blob/main/.github/workflows/ci.yml) | Solution restore/build, portable CLI and generator fixtures, package creation, and legacy generator package adoption. |
| [Source compiler contracts](https://github.com/ifBars/S1Interop/blob/main/.github/workflows/source-compiler.yml) | Compiler contracts and isolated installation of the packaged compiler workflow. |

The package scripts use fresh caches and the candidate feed. `Test-CompilerPackage.ps1` checks default compiler project creation and build imports. `Test-Packages.ps1` checks the explicitly selected legacy scaffold and its documentation sample against synthetic loader references. Neither establishes live game compatibility.

Use [Release readiness](https://github.com/ifBars/S1Interop/blob/main/docs/RELEASING.md#automated-gate-for-every-candidate) for the complete local command sequence, including compiler contracts, both package checks, and documentation validation. Run the Release build and portable fixtures before pushing CLI, packaging, generator, or build-verification changes.

## Fixture Organization

Test files are split by concern:

- `S1InteropFixtureTests.Runner.cs`: test mode dispatch.
- `S1InteropFixtureTests.SourceAndCliTests.cs`: source analysis, source rewrites, and CLI-facing fixture tests.
- `S1InteropFixtureTests.MigrationTests.cs`: migration planning/application and real-mod migration fixtures.
- `S1InteropFixtureTests.VerificationTests.cs`: sandbox build verification and build-hook fixtures.
- `S1InteropFixtureTests.GeneratorTests.cs`: Roslyn generator and backend-neutral runtime helper fixtures.
- `S1InteropFixtureTests.DualRuntimeAuthoringTests.cs`: explicit-runtime authoring support. Runtime helpers run the same source against Mono-shaped and Il2CppInterop-shaped stand-ins, injected-type constructors, build-property switches, the C# 9 gate, and the package's MSBuild targets (evaluated with `dotnet msbuild -getItem`, plus a deploy build that uses a temporary `Mods` folder).
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

Ordinary build tests must not write into game installs. The default compiler scaffold does not deploy. Dedicated opt-in runtime runners manage their own audited test-install deployments and cleanup. Debug builds of the legacy generator scaffold deploy into `<install>\Mods`, so any test that builds one against local game references passes `-p:S1InteropModsPath=<temp folder>`. Builds that restore a locally packed `S1Interop.Generators` should also pass `-p:RestorePackagesPath=<temp folder>`: the global NuGet cache may hold an older build of the same alpha version.

When manually debugging verifier sandboxes, clean these folders after use:

```text
%TEMP%\S1Interop.Tests\
%TEMP%\S1Interop.Verify.*
%TEMP%\S1Interop.Debug*
```

Do not commit generated test output, packaged artifacts, local props, game assemblies, or copied mod fixtures.
