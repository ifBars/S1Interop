# New backend-neutral projects

Use `new` when you want a MelonLoader mod that builds one backend-neutral DLL.

If you want diagnostics for an existing mod, separate Mono/IL2CPP builds, or only a few generated helpers, start with [Use cases](use-cases.md) instead.

```powershell
s1interop new .\MyBackendNeutralMod --apply
```

The scaffold includes:

- a C# project and solution;
- `Debug` and `Release` builds that produce one `bin/Single/...` assembly;
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
6. Build `Debug`.

The normal build uses Mono reference assemblies, emits `S1InteropTargetRuntime=Unknown`, and detects Mono or IL2CPP at runtime. That DLL is the one you ship to both game installs.

If you intentionally want an IL2CPP reference compile while developing declarations, run it explicitly and treat the output as a check:

```powershell
dotnet build .\MyBackendNeutralMod.sln -c Debug -p:S1InteropReferenceRuntime=Il2Cpp -p:S1InteropTargetRuntime=Il2Cpp
```

Do not publish separate Mono and IL2CPP DLLs for a backend-neutral project unless your mod still has runtime-specific code that S1Interop cannot cover yet.

For a blank project, seed the SDK from your local game references:

```powershell
s1interop sdkgen . --full-sdk --apply
```

That generates declarations from local metadata. Full SDK mode adds broad type registration for Schedule One. Add `S1InteropType` declarations for types where you want member facades.

Use [Backend-neutral declarations](backend-neutral-declarations.md) to review or hand-edit the declaration file. See [Generated output](generator-package.md) for what appears after a build.

## Working with other S1 modding libraries

S1Interop does not replace S1API, MAPI, SteamNetworkLib, bGUI, or dedicated server APIs. Add those dependencies the same way you would in a normal MelonLoader project.

Use S1Interop for direct game access:

- generated facades for `ScheduleOne.*` / `Il2CppScheduleOne.*` types;
- type/member lookup that should survive backend differences;
- Unity component lookup helpers such as `S1Interop.Generated.S1InteropUnity`;
- simple public fields, properties, constructors, enum mirrors, and methods when metadata is safe;
- explicit `S1InteropMember` declarations for private, ambiguous, or migration-specific seams.

Keep S1API for item, NPC, shop, saveable, and UI workflows. Keep MAPI for building and model construction. Keep SteamNetworkLib for higher-level networking clients, sync vars, DTOs, chunking, and message protocols.

Use S1Interop underneath those libraries when you still need direct backend-neutral game or Steamworks access. That includes Steam P2P byte buffers, relay/session calls, callback pumping, reliable send-mode lookup, Steam ID values, and Schedule One lobby member lookup.

It does not commit game assemblies, wrapper dumps, decompiled source, prefabs, scenes, textures, or exported Unity projects.
