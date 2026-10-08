# Release readiness

The current published version is **0.1.0-alpha.2**. It is experimental, not a stable release. Do not reuse a published version for changed package contents.

## Primary workflow

The `S1Interop` tool package provides one `s1interop` command, including the source compiler. `new` creates a compiler project with ordinary ScheduleOne source, a pinned local tool manifest and matching build imports. Developers restore that tool, configure matching game installations with `setup`, check them with `doctor`, and build the same source for each runtime. There is no separate compiler CLI to install.

Compiler projects use Mono game metadata for authoring. IL2CPP builds also require the matching native installation and generated interop assemblies. Runtime selection chooses the output; it does not switch mod source to an IL2CPP conditional branch. Existing source selects `MONO` for both outputs. See [Source compiler](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) for tested behaviors and unresolved compatibility gaps.

The compiler starter does not depend on `S1Interop.Generators` or automatically deploy into a game. IL2CPP compiler outputs include the matching shared `S1Interop.Runtime.dll`. Follow the [distribution guide](https://github.com/ifBars/S1Interop/blob/main/docs/docfx/articles/distributing-mods.md) for files and placement.

## Alpha.2 changes

- Compiler dispatch, reference preparation and lowering are consolidated into the tool package. The former compiler executable project is removed.
- The default scaffold uses the compiler, pins local tooling and preserves ordinary game source. Setup records local paths in an ignored file; builds verify the game pair and native generation provenance.
- The compiler automatically adapts supported casts, components, collections, callbacks and reflection from metadata. It also restores recognized failed event accessors. These features do not establish unrestricted game or language compatibility.
- README, onboarding, command help and existing-mod guidance lead with the compiler. Older workflows have explicit legacy navigation and scaffold options.
- Package tests install the actual candidate into an isolated cache and validate the compiler workflow separately from legacy generator compatibility. Optional local game paths enable both real-reference compiler builds from saved setup.
- Repository builds use the .NET 8 SDK family. A pinned DocFX build must pass before publication.

### Retained legacy compatibility

`new --legacy-generator`, `new --backend-neutral`, declarations and generator migration remain available. The separate `S1Interop.Generators` package supplies their Roslyn 4.8 generator and opt-in build integration. Its game-reference, deployment, runtime-import, typed-helper and injected-constructor features belong to that workflow; they are not requirements for the compiler starter. Alpha.2 also fixes UnityEvent listener removal and runtime detection in this legacy surface. Keep its package version aligned and validate it independently while it remains supported.

## Automated gate for every candidate

From the repository root in PowerShell, run each command separately and stop on failure:

```powershell
dotnet restore .\S1Interop.sln
dotnet build .\S1Interop.sln -c Release --no-restore
dotnet run --project .\tests\S1Interop.Tests -c Release --no-build -- --portable
dotnet run --project .\tests\S1Interop.Compiler.Tests -c Release --no-build
dotnet pack .\src\S1Interop.Cli -c Release --no-build -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators -c Release --no-build -o .\artifacts\packages
./tests/Test-CompilerPackage.ps1 -PackageDirectory artifacts/packages
./tests/Test-Packages.ps1
dotnet tool restore
dotnet tool run docfx .\docs\docfx\docfx.json --warningsAsErrors
git diff --check
```

Both package scripts require Windows and a .NET 8 SDK. They use isolated tool/package caches so a published package cannot mask a candidate defect. `Test-CompilerPackage.ps1` checks default project creation, pinned restore, compiler help and reference preparation. With game paths, it also checks saved setup, doctor and both builds. `Test-Packages.ps1` validates the explicitly selected legacy generator scaffold and its documentation sample against a small loader contract. Neither portable package lane proves live MelonLoader or gameplay behavior.

For local compiler validation against owned, matching game installations:

```powershell
./tests/Test-CompilerPackage.ps1 -PackageDirectory artifacts/packages `
    -MonoGamePath 'C:\Games\ScheduleI-Mono' `
    -Il2CppGamePath 'C:\Games\ScheduleI-Il2Cpp'
```

This builds in a temporary project and does not deploy the result. Keep proprietary references and local evidence out of Git.

## Additional gates before declaring stable

| Gate | Required evidence |
| --- | --- |
| Fresh installation | Install the published tool in an empty cache and follow the compiler guide with the minimum supported SDK. Validate the legacy generator package separately. |
| Beginner path | Follow setup through the first code change on a clean compiler project; confirm loading and its feature separately on Mono and IL2CPP. |
| Real references | Build both compiler outputs against the matching game/loader pair from saved configuration. |
| Existing mods | Evaluate unchanged source and dependencies in controlled compiler projects. Separate outdated author APIs from lowering failures, then verify applicable runtime behavior. |
| Gameplay | Exercise representative mod features on both backends, including saves and multiplayer where relevant. |
| Release contract | State supported SDK/game/loader versions, tested scope, known failures and changes since the previous package. |
| Public artifacts | Verify the tag, GitHub release assets, NuGet package versions and hosted documentation after publication. |

Portable tests and compilation do not complete gameplay gates. Retain alpha status until the required evidence is reviewed. Publish a non-proprietary summary of the validated matrix.

## Publication sequence

1. Keep both package versions and `S1InteropPackageInfo` aligned. Update installation examples and `docs/releases/<version>.md` for the chosen version. The workflow uses that file as the GitHub release body when present.
2. Review the candidate changes and land the release commit after CI and documentation checks pass.
3. When publication is authorized, push the matching `v<version>` tag. The release workflow runs compiler contracts, both package lanes and documentation validation before publishing.
4. Confirm both packages are downloadable, compare published artifacts with the validated packages, repeat fresh installation against NuGet.org and check hosted docs.
5. Remove the candidate/unpublished notice only after public verification succeeds. Keep unsupported paths explicit.

NuGet publication of two packages is not atomic. If only one publishes, inspect the workflow and feed before retrying. Never repack different contents under an already published version.
