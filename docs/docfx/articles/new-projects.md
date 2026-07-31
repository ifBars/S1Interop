# New projects

Use `new` to create a normal MelonLoader project with explicit Mono and IL2CPP configurations:

```powershell
s1interop new .\MyMod
s1interop new .\MyMod --apply
```

The first command is a dry run. The second writes only to an empty target directory.

The default scaffold includes:

- `Debug Mono`, `Release Mono`, `Debug Il2Cpp`, and `Release Il2Cpp` configurations;
- `S1Interop.Generators` for compile-time diagnostics and runtime reporting;
- a `ModCore.cs` entry point that logs the selected runtime;
- `local.build.props.example` and an explicit `.gitignore` rule;
- an opt-in declaration file for later generated-helper experiments.

## Configure local inputs

```powershell
Set-Location .\MyMod
s1interop doctor .
s1interop setup .
s1interop setup . --apply
```

`doctor` is read-only. `setup` writes only ignored local configuration, never installs software, and never overwrites an existing `local.build.props`.

## Build

```powershell
dotnet build .\MyMod.sln -c "Debug Mono"
dotnet build .\MyMod.sln -c "Debug Il2Cpp"
```

Build and test the runtime matching the active game branch. When both installs are available, keep both builds as the compatibility proof.

## Experimental backend-neutral project

The generated one-DLL facade model is experimental and fragile. It is not the default.

Create it only with the explicit flag:

```powershell
s1interop new .\MyBackendNeutralExperiment --backend-neutral --apply
```

Validate that project against both reference surfaces and on both runtime branches. Keep the default dual-runtime project until the mod has sustained in-game validation.

See [Use cases](use-cases.md), [Backend-neutral SDK](backend-neutral-sdk.md), and [Real-mod evidence](../contributors/real-mod-evidence.md).
