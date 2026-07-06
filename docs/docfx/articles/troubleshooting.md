# Troubleshooting

This page covers the most common issues you will run into when using S1Interop and how to resolve them. If something is silently wrong — no generated symbols, failed restores, stale IDE state — start here.

## "type or namespace 'S1InteropType' could not be found"

**Cause:** Your project does not reference `S1Interop.Generators`. The declaration attributes (`S1InteropType`, `S1InteropNamespace`, `S1InteropMember`) and all generated facades are emitted by the Roslyn generator package during compilation. Without the package reference, the compiler has no knowledge of those symbols.

**Fix:** Add the `PackageReference` to your `.csproj` and rebuild:

```xml
<PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.1" />
```

After adding the reference, run a full build so the generator emits the declaration attributes and your project can recognise them.

> [!NOTE]
> If you are using an unpublished local build, you also need `S1InteropGeneratorPackageSource` configured in `local.build.props` so the package restore can find the local feed. See [package restore failure](#package-restore-fails-for-s1interopgenerators) below.

## Generated type or member missing from IntelliSense

**Cause:** No design-time build has run since you added or changed a declaration. The Roslyn generator emits symbols during compilation, not at restore time. If you edit `S1Interop.BackendNeutral.cs` and immediately try to use the new facade in another file, the IDE may not yet know about it.

**Fix:** Build the project once — either via your IDE's build command or `dotnet build`. After the build (or after the IDE runs its own design-time build), the new symbols appear in IntelliSense and are compiled into the assembly.

> [!NOTE]
> Generated symbols are emitted into the same compilation as the rest of your project. They are not a separate assembly and are not referenced from a runtime package. The `S1Interop.Generators` package ships only the generator DLL under `analyzers/dotnet/cs`.

## S1I001: game type not found

**Cause:** The generator validated an `S1InteropType` declaration against your referenced Mono or IL2CPP assemblies and could not locate the type. The most common reasons are:

- `MonoGamePath` or `Il2CppGamePath` is not set in `local.build.props`, so no game assembly surface is available.
- The type name in the declaration does not match the actual runtime type name in the game assembly.

**Fix:**

1. Open `local.build.props` (copy from `local.build.props.example` if it does not exist yet) and verify both paths point to your Schedule One installs:

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

## Package restore fails for S1Interop.Generators

**Cause:** NuGet cannot find `S1Interop.Generators` because the local alpha package is not in any configured feed and `S1InteropGeneratorPackageSource` is not set in `local.build.props`.

**Fix:**

1. Pack both packages into the same output folder:

```powershell
dotnet pack .\src\S1Interop.Cli\S1Interop.Cli.csproj -c Release -o .\artifacts\packages
dotnet pack .\src\S1Interop.Generators\S1Interop.Generators.csproj -c Release -o .\artifacts\packages
```

2. Add `S1InteropGeneratorPackageSource` to your `local.build.props` pointing at that folder:

```xml
<S1InteropGeneratorPackageSource>..\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
```

Generated and migrated projects already bridge that property into `RestoreAdditionalProjectSources`, so Visual Studio, Rider, and `dotnet build` pick up the local package without committing a machine-specific NuGet source to version control.

> [!WARNING]
> Do not commit `local.build.props`. It holds machine-specific paths that differ between developers. Commit only `local.build.props.example`.

## Migration applied but something went wrong

**Cause:** A migration wrote unexpected changes — source rewrites, project edits, or solution updates that need to be undone.

**Fix:** Every applied migration writes a rollback manifest and file backups under `s1interop-runs/<run-id>/`. Run the rollback command to restore all backed-up files:

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

Replace `<run-id>` with the specific run directory created by `--apply`.

> [!TIP]
> Always review `--dry-run` output before using `--apply`. The dry-run shows every operation S1Interop would perform without writing any files, giving you a chance to catch unexpected rewrites before they happen.

## IDE still shows old build configurations after dual-runtime migration

**Cause:** Your IDE cached the old solution configuration. After a dual-runtime migration, S1Interop updates the `.sln` file to add the new configurations, but the IDE may not reload the solution automatically.

**Fix:** Verify the `.sln` file was updated (look for the new `Mono` and `IL2CPP` configuration entries), then close and reopen the solution in your IDE. The new configurations will be available after the reload.

## verify-migration fails to build

**Cause:** Either the game paths are not available in the sandbox environment, or the default build timeout is too short for your project.

**Fix:** Pass the game paths directly as flags and increase the timeout as needed:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I" `
  --il2cpp-game-path "C:\Program Files (x86)\Steam\steamapps\common\Schedule I IL2CPP" `
  --build-timeout-seconds 120
```

The `--mono-game-path` and `--il2cpp-game-path` flags override `local.build.props` values for the duration of the sandbox build, so you can verify on machines or CI environments where the props file is not present.

## S1I007 warning on a cast

**Cause:** You have a plain C# cast from `object` or `Il2CppObjectBase` to a Unity object type. These casts compile fine but fail at runtime on IL2CPP because the IL2CPP proxy boundary requires going through `TryCast<T>` rather than a direct CLR cast. `S1I007` fires only when the compilation targets IL2CPP, so the warning surfaces the problem at build time rather than as a runtime surprise.

**Fix:** Replace the plain cast with `S1InteropObjectCast.As<T>(value)`:

```csharp
// Before — triggers S1I007 on IL2CPP builds
var obj = (MyUnityType)rawObject;

// After — routes through TryCast<T> on IL2CPP, safe on both runtimes
var obj = S1InteropObjectCast.As<MyUnityType>(rawObject);
```

`S1InteropObjectCast.As<T>` is generated by `S1Interop.Generators` and handles the proxy dispatch automatically. The call is a no-op on Mono, so the same code runs correctly on both backends.

## Related pages

- [FAQ](faq.md)
- [Diagnostics](diagnostics.md)
- [Local game paths](local-paths.md)
- [Commands](commands.md)
