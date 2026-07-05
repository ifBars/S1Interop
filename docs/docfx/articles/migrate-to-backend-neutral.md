# Migrate to backend-neutral

Use this path when you want an existing Mono mod to move toward one assembly that can run on Mono or IL2CPP.

This is the main S1Interop product direction. It does not mean "compile the same source twice and ship two DLLs." It means the mod source moves away from direct `ScheduleOne.*` or `Il2CppScheduleOne.*` calls and uses generated facades under `S1Interop.ScheduleOne.*`.

## 1. Analyze the mod

```powershell
s1interop analyze .
```

Fix missing local paths or package restore problems first. The generator needs game reference metadata to validate requested types and produce useful facades.

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

## 4. Use the generated facade

Generated facades preserve the original runtime namespace under `S1Interop`:

```csharp
using S1Interop.ScheduleOne.Vehicles;

LandVehicle.Handle vehicle = LandVehicle.As(rawVehicle);

if (vehicle.HasValue)
{
    string? name = vehicle.VehicleName?.ToString();
    float? throttle = vehicle.GetCurrentThrottleValue<float>();
}
```

The goal is to keep mod code close to normal game API usage while moving backend differences into generated code.

## 5. Build and review diagnostics

Build the project from the IDE or command line:

```powershell
dotnet build
```

If both Mono and IL2CPP references are configured, generator diagnostics can catch bad type names, bad member overrides, and known IL2CPP boundary cases at compile time. See [Diagnostics](diagnostics.md).

## Current limits

Backend-neutral migration is still alpha. The tool can generate facades and move some source patterns, but advanced mods may still need explicit declarations or small source edits. Unsupported cases should show up as diagnostics or source-risk reports rather than silent guesses.
