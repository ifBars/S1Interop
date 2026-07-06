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

Use the branch names modders already use:

| Steam branch | Usual backend | Typical path property |
| --- | --- | --- |
| `none` / public default | IL2CPP | `Il2CppGamePath` |
| `beta` | IL2CPP | `Il2CppGamePath` |
| `alternate` | Mono | `MonoGamePath` |
| `alternate-beta` | Mono | `MonoGamePath` |

For projects created with `s1interop new`, copy `local.build.props.example` to `local.build.props`, set both paths, and open the generated solution in Visual Studio or Rider.

For existing migrated projects, S1Interop tries to create or repair the same stable path slots. Custom configurations such as `MonoStable` or `Il2cppDevelopment` should still read from `MonoGamePath` and `Il2CppGamePath`.

Many existing S1 mods use names such as `ScheduleOnePath`, `GameInstallPath`, `S1MonoDir`, or `S1IL2CPPDir`. You do not have to rewrite the whole project around S1Interop names, but new generated or migrated files should converge on `MonoGamePath` and `Il2CppGamePath`. Keep compatibility aliases in local props or project files while migrating older build scripts.

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
