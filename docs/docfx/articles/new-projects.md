# New backend-neutral projects

Use `new` when you want to start with the backend-neutral shape instead of writing a Mono-only mod first.

```powershell
s1interop new .\MyBackendNeutralMod --apply
```

The scaffold includes:

- a C# project and solution;
- Mono and IL2CPP build configurations for validating the same backend-neutral source against both reference surfaces;
- `local.build.props.example` for machine-specific game paths;
- a starter `S1Interop.Generated/S1Interop.BackendNeutral.cs` declaration file;
- package references needed by the generated helpers.

After creating the project:

1. Copy `local.build.props.example` to `local.build.props`.
2. Set `MonoGamePath` and `Il2CppGamePath`.
3. If you are using unpublished local S1Interop packages, set `S1InteropGeneratorPackageSource` to the folder containing `S1Interop.Generators.*.nupkg`.
4. Open the `.sln` in Visual Studio or Rider.
5. Build `Debug` for Mono and `Debug Il2Cpp` for IL2CPP.

Those two configurations are not meant to make you maintain two source implementations. They are validation targets. Your mod code should still prefer generated `S1Interop.ScheduleOne.*` facades and shared source wherever S1Interop can express the backend difference safely.

For a blank project, seed the SDK from your local game references:

```powershell
s1interop sdkgen . --full-sdk --apply
```

That generates declarations from local metadata. Full SDK mode adds broad type registration for Schedule One, then you add `S1InteropType` declarations for types where you want member facades.

Use [Backend-neutral declarations](backend-neutral-declarations.md) when you need to review or hand-edit the generated declaration file. See [Generated output](generator-package.md) for what the generator emits from those declarations and when symbols appear after a build.

It does not commit game assemblies, wrapper dumps, or decompiled source.
