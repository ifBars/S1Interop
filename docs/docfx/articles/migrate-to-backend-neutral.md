# Migrate to backend-neutral

Use this path when you want an existing Mono mod to move toward one assembly that can run on Mono or IL2CPP.

This is the main S1Interop product direction. It does not mean "compile the same source twice and ship two DLLs." It means the mod source moves away from direct `ScheduleOne.*` or `Il2CppScheduleOne.*` calls and uses generated facades under `S1Interop.ScheduleOne.*`.

Mono and IL2CPP build configurations can still be useful after the migration. In a backend-neutral project they are validation targets for the same source, not a commitment to keep two conditional implementations alive.

## 1. Analyze the mod

```powershell
s1interop analyze .
```

Fix missing local paths or package restore problems first. The generator needs game reference metadata to validate requested types and produce useful facades.

If the mod already uses S1API, MAPI, SteamNetworkLib, bGUI, or a dedicated server API, keep those dependencies in place. Backend-neutral migration is about the direct Schedule One seams that still require runtime-specific references. It is not a reason to replace a working item builder, networking helper, UI library, or server lifecycle API.

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

For a real Mono mod, start with usage-driven generation:

```powershell
s1interop sdkgen . --dry-run
s1interop sdkgen . --apply
```

This inspects source references, aliases, namespace imports, string-held game type names, and local game metadata. It emits the types the mod appears to use.

Usage-driven generation also picks up simple reflection metadata bindings when they point at Schedule One game types. For example, a consumer mod that keeps S1API for quests but caches `AccessTools.Field(typeof(S1Quests.Quest), "title")` for its own patch can get a generated `S1InteropMember` target instead of keeping that backend-sensitive lookup in mod code.

For a blank or exploratory backend-neutral project, seed broad type coverage from local game references:

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

The goal is to keep mod code close to normal game API usage while moving backend differences into generated code.

For a real patch mod, that usually means replacing code like "read the vehicle/player/UI object differently on Mono and IL2CPP" before touching unrelated systems. Keep Harmony patch methods small and let services use generated facades behind them.

For a mod that already uses S1API, keep the S1API calls where they are. Move only the direct Schedule One access that S1API does not own: Harmony targets, direct game-wrapper casts, cached member metadata, and small field/property reads around the patch.

## 5. Build and review diagnostics

Build the project from the IDE or command line:

```powershell
dotnet build
```

If both Mono and IL2CPP references are configured, generator diagnostics can catch bad type names, bad member overrides, and known IL2CPP boundary cases at compile time. See [Diagnostics](diagnostics.md).

## Current limits

Backend-neutral migration is still alpha. The tool can generate facades and move some source patterns, but advanced mods may still need explicit declarations or small source edits. Unsupported cases should show up as diagnostics or source-risk reports rather than silent guesses.

The current generated facade surface is useful but still conservative. Ordinary public members are generated when Mono and IL2CPP metadata make them safe. Overloads, constructors, collection conversions, `Il2CppSystem.Guid`, Unity object/proxy casts, and arbitrary reflection flows may still need explicit `S1InteropMember` declarations, generated reports, or hand-written runtime-specific code.
