# Local game paths

Schedule One install paths are machine-specific. Keep them out of source control.

S1Interop uses ignored local props files for developer-specific paths:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>...</MonoGamePath>
    <Il2CppGamePath>...</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

For projects created with `s1interop new`, copy `local.build.props.example` to `local.build.props`, set both paths, and open the generated solution in Visual Studio or Rider.

For existing migrated projects, S1Interop tries to create or repair the same stable path slots. Custom configurations such as `MonoStable` or `Il2cppDevelopment` should still read from `MonoGamePath` and `Il2CppGamePath`.

## Verification paths

Build verification can receive paths directly:

```powershell
s1interop verify-migration . --dual-runtime --build `
  --mono-game-path "<your Mono Schedule I install>" `
  --il2cpp-game-path "<your IL2CPP Schedule I install>"
```

Do not commit `local.build.props`. If a generated or migrated project needs an unpublished local generator package, set `S1InteropGeneratorPackageSource` in the same ignored file:

```xml
<S1InteropGeneratorPackageSource>...\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
```

Generated projects map that property into `RestoreAdditionalProjectSources`, which keeps IDE and command-line restores pointed at the local alpha package without committing a NuGet source.
