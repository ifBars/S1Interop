# Troubleshooting

Use the first failing diagnostic and the workflow that produced it. Compiler projects use the pinned local tool and `S1IC` diagnostics. The older generator package reports `S1I` diagnostics; its fixes belong to that separate workflow.

## Compiler tool cannot be found or restored

From the project directory, run `dotnet tool restore`. For an unpublished candidate, add `--add-source <candidate-feed>` as described in [installation](getting-started.md#build-and-install-the-candidate-from-source). Invoke it with `dotnet tool run s1interop -- compiler --help`; a global alpha.1 installation cannot replace the compiler project's pinned candidate.

Keep the tool manifest and `.s1interop` build imports from the same scaffold generation. When rebuilding an unpublished package with the same version, use a fresh package cache and tool directory. Do not point build imports at the removed experimental compiler executable.

## Compiler game references or versions are rejected

Run `dotnet tool run s1interop -- doctor .` and check both paths in the ignored `local.build.props`. Paths must name the installation roots. Compiler authoring needs Mono metadata even for an IL2CPP output; IL2CPP builds also need generated native references from a matching patch version. Launch the native installation with MelonLoader to generate those references before building.

Doctor checks local reference availability. The build's installation verifier checks game versions and native generation provenance. Preserve the exact failure and correct the installation pair rather than bypassing verification.

## Ordinary game source fails in the native build

Compiler projects should keep their ordinary `ScheduleOne.*` imports. Verify that the project imports both compiler build files and builds through its pinned tool. Do not add `S1InteropUsing`, generated facade attributes, or conditional namespaces as a generic repair; those belong to the earlier workflow.

`S1IC001` or `S1IC002` indicates missing or ambiguous target metadata. Other `S1IC` diagnostics identify unsupported runtime adaptation. Preserve the original source, the diagnostic, and a small reproducer. See the [compiler support and limits](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md) before interpreting successful compilation as equivalent gameplay behavior.

## The compiler-built mod fails to load

Deploy the output for that runtime. IL2CPP also requires the adjacent matching `S1Interop.Runtime.dll` in `UserLibs`. Do not deploy game references or authoring metadata. Close the game before replacing DLLs, and inspect the first relevant MelonLoader failure. All compiler-built mods loaded together must use compatible runtime support generations.

## Earlier generator and migration workflows

The following advice applies to `--legacy-generator`, facade projects, and migration tooling. It is not required setup for the compiler starter.

### "s1interop" is not recognized

**Cause:** The global .NET tool is not installed, or the current terminal has not picked up the global tools path.

**Fix:** Check the installed tools:

```batch
dotnet tool list --global
```

If `s1interop` is listed, close and reopen Command Prompt. If it is not listed, repeat [Install the command](getting-started.md#2-install-the-command).

### S1I102-S1I105, or "Missing MelonLoader at ..."

**Cause:** The game path for the selected configuration is empty or points below the game root. The scaffold expects a folder containing `Schedule I.exe`, `Schedule I_Data`, and `MelonLoader`.

**Fix:** Run `s1interop doctor .`, then open `local.build.props` and correct `MonoGamePath` for a Mono build or `Il2CppGamePath` for an IL2CPP build. Do not point it at `Schedule I_Data`, `Managed`, or `MelonLoader` directly. For `S1I105`, launch that install once with MelonLoader so it generates `Il2CppAssemblies`.

Older scaffolds report the same mistake as `Missing Unity assemblies`, `Missing Schedule One game assembly`, or `Missing ScheduleOne.Core`. See [Local game paths](local-paths.md#which-folder-to-use) for the expected folder layout.

### S1I107: could not copy the DLL to Mods

**Cause:** The game is running and holds the previously deployed DLL open.

**Fix:** Close Schedule I and build again. The DLL in `bin\` is still valid; only the copy failed.

### "The type or namespace name 'ScheduleOne' could not be found" in an IL2CPP build

**Cause:** IL2CPP builds name game namespaces `Il2CppScheduleOne.*`, so a `using ScheduleOne...;` directive or a fully qualified `ScheduleOne.` name only compiles for Mono.

**Fix:** Remove the `using` directive and add the namespace to the project instead, which imports the right name for each build:

```xml
<S1InteropUsing Include="ScheduleOne.Product" />
```

Use the short type name in code. See [Write code for both runtimes](dual-runtime-code.md#import-game-namespaces-once).

### "is an ambiguous reference" after adding S1InteropUsing

**Cause:** `S1InteropUsing` imports a namespace into every file, and two imported namespaces define the same type name, or a game type shares a name with a BCL type such as `Console`.

**Fix:** Import one of them with an alias, `<S1InteropUsing Include="ScheduleOne.Console" Alias="GameConsole" />`, and write `GameConsole.ConsoleCommand`.

### Lambda does not convert to UnityAction on IL2CPP

**Cause:** IL2CPP's `UnityAction` is a wrapper class, not a delegate, so `button.onClick.AddListener(() => ...)` only compiles for Mono.

**Fix:** Add `using S1Interop;` (the scaffold imports it for every file). The generated `AddListener` overloads accept lambdas on both runtimes. Use `Subscribe` when you need to remove the listener later.

### S1I010: generated code needs C# 9

**Cause:** The project compiles as C# 8, the default for `netstandard2.1`.

**Fix:** Add `<LangVersion>latest</LangVersion>` to the project. Until then S1Interop reports diagnostics but generates no helpers, registries, or facades.

### Runtime shows Unknown

**Cause:** A backend-neutral build could not find the well-known Schedule I type or assembly names used by the generated runtime probe.

**Fix:** Confirm the DLL is running under MelonLoader inside Schedule I, not in a standalone test process. For the legacy generator scaffold, deploy the DLL from the output matching the active runtime (`bin\Mono\...` or `bin\Il2Cpp\...`). The `bin\Single` output exists only in the experimental backend-neutral scaffold. Check `MelonLoader\Latest.log` for assembly-load failures. If the game or generated IL2CPP assembly names changed, keep the log because the probe list may need a S1Interop update.

### "type or namespace 'S1InteropType' could not be found"

**Cause:** Your project does not reference `S1Interop.Generators`. The declaration attributes (`S1InteropType`, `S1InteropNamespace`, `S1InteropMember`) and all generated facades are emitted by the Roslyn generator package during compilation. Without the package reference, the compiler has no knowledge of those symbols.

**Fix:** Add the `PackageReference` to your `.csproj` and rebuild:

```xml
<PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.2" PrivateAssets="all" />
```

After adding the reference, run a full build so the generator emits the declaration attributes and your project can recognise them.

Alpha.2 is an unpublished candidate; use its [local feed instructions](getting-started.md#build-and-install-the-candidate-from-source), or select published alpha.1 with its required .NET 9 SDK.

### CS9057: generator references a newer compiler

**Cause:** The compiler cannot load the generator's Roslyn dependency. Published alpha.1 references Roslyn 4.13, which is newer than the compiler supplied by .NET 8.

**Fix:** Use a .NET 9 SDK (9.0.200 or newer) for alpha.1, or build the alpha.2 candidate, which targets Roslyn 4.8. Check `dotnet --version` from the mod folder because a `global.json` in a parent folder can select an older SDK. An IDE can use a different compiler from the terminal; check the first build warning before changing your source.

### setup rejects an IL2CPP-only install

Published alpha.1 requires Mono for automatic setup. Configure [local paths](local-paths.md#configure-a-new-project) manually, or use the alpha.2 candidate. The compiler scaffold needs Mono metadata even when building IL2CPP, and matching native references for an IL2CPP build. Only the earlier `--legacy-generator` scaffold can start with either runtime alone.

> [!NOTE]
> Published releases restore `S1Interop.Generators` from NuGet.org. Contributors validating an unpublished package should pass a temporary restore source on the command line rather than adding it to `local.build.props`.

### Generated type or member missing from IntelliSense

**Cause:** No design-time build has run since you added or changed a declaration. The Roslyn generator emits symbols during compilation, not at restore time. If you edit `S1Interop.BackendNeutral.cs` and immediately try to use the new facade in another file, the IDE may not yet know about it.

**Fix:** Build the project once, either via your IDE or `dotnet build`. After the build (or after the IDE runs its own design-time build), the new symbols appear in IntelliSense and are compiled into the assembly.

> [!NOTE]
> Generated symbols are emitted into the same compilation as the rest of your project. They are not a separate assembly and are not referenced from a runtime package. The `S1Interop.Generators` package ships only the generator DLL under `analyzers/dotnet/cs`.

### Generated code does not match the current source

**Cause:** You rebuilt a local alpha package without changing its version, but NuGet reused the older `S1Interop.Generators` package already stored on your machine. This can make a newly packed generator behave differently from the source you just built.

**Fix:** Use a fresh package cache and restore from the candidate feed in the same PowerShell terminal:

```powershell
$env:NUGET_PACKAGES = Join-Path $env:TEMP ("s1interop-cache-" + [guid]::NewGuid().ToString("N"))
dotnet restore
```

Follow the [candidate feed setup](getting-started.md#build-and-install-the-candidate-from-source) first. The isolated cache leaves existing packages untouched. Release publication must always use a new version for changed contents.

### Generated helper returns null or false

**Cause:** The helper compiled, but the active backend could not complete the runtime lookup or call. Common cases are a method renamed by a game update, an IL2CPP wrapper member that is not present, an overloaded method missing `ParameterTypeNames`, a parameter type that does not resolve on IL2CPP, or a value that cannot be converted to the runtime signature.

**Fix:** Inspect `S1Interop.Generated.S1InteropMemberRegistry.Reports` after the failed call. Check the latest report's `Status`, `OwnerTypeName`, `MemberName`, and `ParameterTypeNames`.

- `MissingMember` means the current backend type does not expose that member. Verify the current Mono and IL2CPP reference assemblies.
- `AmbiguousMember` means you probably need `ParameterTypeNames`.
- `MissingParameterType` means a declared overload parameter does not map to a runtime type.
- `ArgumentConversionFailed` means the target resolved, but the value you passed could not cross the backend boundary.

On IL2CPP, do not assume a Mono member still exists or still gets called. If the wrapper metadata is missing or the method is tiny enough to inline, patch or call a higher-level method that you can verify in the IL2CPP branch.

### S1I001: game type not found

**Cause:** The generator validated an `S1InteropType` declaration against an available Mono or IL2CPP reference surface and could not locate the type. The most common reasons are:

- The type name in the declaration does not match the actual Mono runtime type name.
- The selected reference surface is older or newer than the game version the declaration was written against.

**Fix:**

1. Open `local.build.props` (copy from `local.build.props.example` if it does not exist yet) and verify the path for the build you are running points to the correct Schedule I install:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>C:\Program Files (x86)\Steam\steamapps\common\Schedule I</MonoGamePath>
    <Il2CppGamePath>C:\Program Files (x86)\Steam\steamapps\common\Schedule I IL2CPP</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

2. Double-check the fully-qualified type name in your declaration matches what is in the game assembly (for example `ScheduleOne.Vehicles.LandVehicle`, not `ScheduleOne.Vehicle.LandVehicle`).

> [!NOTE]
> Declaration diagnostics `S1I001`-`S1I003` stay quiet when no game reference surface is available, so package-restore and docs-only builds never fail just because local game paths are missing.

### Package restore fails for S1Interop.Generators

**Cause:** NuGet.org is unavailable, disabled, or the requested prerelease version is not visible to the current NuGet configuration.

**Fix:**

1. Confirm NuGet.org is enabled:

```batch
dotnet nuget list source
```

2. Verify the requested version is published, or configure the candidate feed, then restore:

```batch
dotnet restore
```

Contributors testing an unpublished build should pass their temporary package folder as an explicit command-line restore source. Do not add package-feed paths to `local.build.props`.

> [!WARNING]
> Do not commit `local.build.props`. It holds machine-specific paths that differ between developers. Commit only `local.build.props.example`.

### Migration applied but something went wrong

**Cause:** A migration wrote unexpected changes: source rewrites, project edits, or solution updates that need to be undone.

**Fix:** Every applied migration writes a rollback manifest and file backups under `s1interop-runs/<run-id>/`. Run the rollback command to restore all backed-up files:

```batch
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

Replace `<run-id>` with the specific run directory created by `--apply`.

> [!TIP]
> Always review `--dry-run` output before using `--apply`. The dry-run shows every operation S1Interop would perform without writing any files, giving you a chance to catch unexpected rewrites before they happen.

### IDE still shows old build configurations after dual-runtime migration

**Cause:** Your IDE cached the old solution configuration. After a dual-runtime migration, S1Interop updates the `.sln` file to add the new configurations, but the IDE may not reload the solution automatically.

**Fix:** Verify the `.sln` file was updated (look for the new `Mono` and `IL2CPP` configuration entries), then close and reopen the solution in your IDE. The new configurations will be available after the reload.

### verify-migration fails to build

**Cause:** Either the game paths are not available in the sandbox environment, or the default build timeout is too short for your project.

**Fix:** Pass the game paths directly as flags and increase the timeout as needed:

```batch
s1interop verify-migration . --dual-runtime --build ^
  --mono-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I" ^
  --il2cpp-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I IL2CPP" ^
  --build-timeout-seconds 120
```

The `--mono-game-path` and `--il2cpp-game-path` flags override `local.build.props` values for the duration of the sandbox build, so you can verify on machines or CI environments where the props file is not present.

### S1I007 warning on a cast

**Cause:** You have a plain C# cast from `object` or `Il2CppObjectBase` to a Unity object type. These casts compile fine but fail at runtime on IL2CPP because the IL2CPP proxy boundary requires going through `TryCast<T>` rather than a direct CLR cast. `S1I007` fires only when the compilation targets IL2CPP, so the warning surfaces the problem at build time rather than as a runtime surprise.

**Fix:** In a dual-runtime project, replace the plain cast with `TryCast<T>()` from the runtime helpers:

```csharp
// Before - triggers S1I007 on IL2CPP builds
var obj = (MyUnityType)rawObject;

// After - Il2CppInterop's TryCast on IL2CPP, an ordinary cast on Mono
var obj = rawObject.TryCast<MyUnityType>();
```

The helpers need `using S1Interop;`, which the scaffold imports for every file. In a backend-neutral single-assembly project, use `S1Interop.Generated.S1InteropObjectCast.As<T>(value)` instead.

### S1I008 warning on a patch target

**Cause:** A backend-neutral `S1InteropPatch` target resolved, but the target needs IL2CPP review. Common cases are overloaded methods without `ParameterTypeNames`, property/event accessors, operator methods, and methods marked for aggressive inlining or optimization.

**Fix:** Add `ParameterTypeNames` for overloaded methods. For accessor-like or aggressively inlined targets, patch a higher-level method when possible. If you intentionally keep the target, validate that the handler fires on the IL2CPP branch you support.

### Related pages

- [FAQ](faq.md)
- [Diagnostics](diagnostics.md)
- [Local game paths](local-paths.md)
- [Commands](commands.md)
