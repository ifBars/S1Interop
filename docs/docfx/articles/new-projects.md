# New backend-neutral projects

Use `new` when you want to start with the backend-neutral shape instead of writing a Mono-only mod first.

```powershell
s1interop new .\MyBackendNeutralMod --apply
```

The scaffold includes:

- a C# project and solution;
- Mono and IL2CPP build configurations;
- `local.build.props.example` for machine-specific game paths;
- a starter `S1Interop.Generated/S1Interop.BackendNeutral.cs` declaration file;
- package references needed by the generated helpers.

After creating the project:

1. Copy `local.build.props.example` to `local.build.props`.
2. Set `MonoGamePath` and `Il2CppGamePath`.
3. If you are using unpublished local S1Interop packages, set `S1InteropGeneratorPackageSource` to the folder containing `S1Interop.Generators.*.nupkg`.
4. Open the `.sln` in Visual Studio or Rider.
5. Build `Debug` for Mono and `Debug Il2Cpp` for IL2CPP.

For a blank project, seed the SDK from your local game references:

```powershell
s1interop sdkgen . --full-sdk --apply
```

That generates declarations from local metadata. Full SDK mode adds broad type registration for Schedule One, then you add `S1InteropType` declarations for types where you want member facades.

It does not commit game assemblies, wrapper dumps, or decompiled source.
