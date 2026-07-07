# New backend-neutral projects

Use `new` when you want to start backend-neutral instead of writing a Mono-only mod first.

```powershell
s1interop new .\MyBackendNeutralMod --apply
```

The scaffold includes:

- a C# project and solution;
- Mono and IL2CPP build configurations for validating the same backend-neutral source against both reference surfaces;
- `local.build.props.example` for machine-specific game paths;
- a starter `S1Interop.Generated/S1Interop.BackendNeutral.cs` declaration file;
- package references needed by the generated helpers;
- a normal MelonLoader entry point with `[MelonInfo]`, `[MelonGame]`, and a `MelonMod` class.

After creating the project:

1. Copy `local.build.props.example` to `local.build.props`.
2. Set `MonoGamePath` to an `alternate` or `alternate-beta` branch install.
3. Set `Il2CppGamePath` to a public `none` or `beta` branch install.
4. If you are using unpublished local S1Interop packages, set `S1InteropGeneratorPackageSource` to the folder containing `S1Interop.Generators.*.nupkg`.
5. Open the `.sln` in Visual Studio or Rider.
6. Build `Debug` for Mono and `Debug Il2Cpp` for IL2CPP.

Those two configurations are validation targets, not two source implementations. Prefer generated `S1Interop.ScheduleOne.*` facades and shared source wherever S1Interop can express the backend difference safely.

For a blank project, seed the SDK from your local game references:

```powershell
s1interop sdkgen . --full-sdk --apply
```

That generates declarations from local metadata. Full SDK mode adds broad type registration for Schedule One, then you add `S1InteropType` declarations for types where you want member facades.

Use [Backend-neutral declarations](backend-neutral-declarations.md) when you need to review or hand-edit the generated declaration file. See [Generated output](generator-package.md) for what the generator emits from those declarations and when symbols appear after a build.

## Working with other S1 modding libraries

S1Interop does not replace S1API, MAPI, SteamNetworkLib, bGUI, or dedicated server APIs. Add those dependencies the same way you would in a normal MelonLoader project, usually from release DLLs or NuGet packages that match the runtime you are validating.

Use S1Interop for direct game access:

- generated facades for `ScheduleOne.*` / `Il2CppScheduleOne.*` types;
- type/member lookup that should survive backend differences;
- simple public fields, properties, constructors, enum mirrors, and methods when metadata is safe;
- explicit `S1InteropMember` declarations for private, ambiguous, or migration-specific seams.

Use domain libraries for the work they already handle well. Keep S1API for item, NPC, shop, saveable, and UI workflows. Keep MAPI for building and model construction. Keep SteamNetworkLib for Steam lobby and P2P helpers. Use S1Interop for direct game-wrapper access underneath those libraries.

It does not commit game assemblies, wrapper dumps, decompiled source, prefabs, scenes, textures, or exported Unity projects.
