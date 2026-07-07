# Migrate to backend-neutral

Use this path when an existing Mono mod should move toward one assembly that runs on Mono or IL2CPP.

Backend-neutral source moves away from direct `ScheduleOne.*` or `Il2CppScheduleOne.*` calls and uses generated facades under `S1Interop.ScheduleOne.*`. Mono and IL2CPP configurations remain useful as validation targets.

This is not the only S1Interop path. If you want diagnostics while keeping manual runtime branches, use [Use cases](use-cases.md#diagnostics-only-use). If you want separate Mono and IL2CPP outputs first, use [Migrate to dual-runtime](migrate-to-dual-runtime.md).

## 1. Analyze the mod

```powershell
s1interop analyze .
```

Fix missing local paths or package restore problems first. The generator needs game reference metadata to validate requested types and produce useful facades.

If the mod already uses native Mono/IL2CPP configurations, S1API, MAPI, SteamNetworkLib, bGUI, or a dedicated server API, keep those boundaries. Move only direct Schedule One access that can be shared safely.

## 2. Add backend-neutral support

Run a dry-run first:

```powershell
s1interop init . --dry-run
```

Apply when the plan looks right:

```powershell
s1interop init . --apply
```

`init` installs the generator package reference and creates an editable backend-neutral declaration file when the project does not already have one.

## 3. Generate SDK declarations

Start with usage-driven generation:

```powershell
s1interop sdkgen . --dry-run
s1interop sdkgen . --apply
```

This inspects source references, aliases, namespace imports, string-held game type names, and local game metadata. It emits the types the mod appears to use.

Usage-driven generation also picks up simple reflection bindings that point at Schedule One game types, such as `AccessTools.Field(typeof(S1Quests.Quest), "title")` or `AccessTools.PropertyGetter(typeof(Grid), "Container")`.

For exploratory projects, seed broad type coverage from local game references:

```powershell
s1interop sdkgen . --full-sdk --dry-run
s1interop sdkgen . --full-sdk --apply
```

Full SDK mode emits a compact namespace declaration instead of thousands of per-type attributes:

```csharp
[assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]
```

Namespace declarations are type-only by default. Add `S1InteropType` for types where you want generated member facades:

```csharp
[assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle")]
```

See [Backend-neutral declarations](backend-neutral-declarations.md) when you need to review generated declarations or add explicit `S1InteropMember` overrides by hand.

## 4. Use the generated facade

Generated facades preserve the original runtime namespace under `S1Interop`:

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);

if (vehicle.HasValue)
{
    string? name = vehicle.VehicleName;
    float? throttle = vehicle.CurrentThrottle;
}
```

Keep mod code close to normal game API usage. Generated code handles the backend differences.

For a mod that already uses S1API, keep the S1API calls where they are. For a mod that already has real Mono/IL2CPP configurations, keep those builds as validation targets. Move the direct Schedule One access that can be shared safely: Harmony targets, direct game-wrapper casts, cached member metadata, and small field/property reads around patches.

## 5. Build and review diagnostics

Build the project from the IDE or command line:

```powershell
dotnet build
```

If both Mono and IL2CPP references are configured, generator diagnostics can catch bad type names, bad member overrides, and known IL2CPP boundary cases at compile time. See [Diagnostics](diagnostics.md).

You can stop here if diagnostics were the goal. You do not need to generate or rewrite facades until you want S1Interop to own a specific game access seam.

## Current limits

Backend-neutral migration is still alpha. Advanced mods may need explicit declarations or small source edits. Unsupported cases should show up as diagnostics or source-risk reports, not silent guesses.

Ordinary public members are generated when Mono and IL2CPP metadata make them safe. Overloads, constructors, collection conversions, `Il2CppSystem.Guid`, Unity object/proxy casts, and arbitrary reflection flows may still need explicit `S1InteropMember` declarations, source-risk reports, or runtime-specific code.
