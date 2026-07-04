# Testing

S1Interop has a small custom test runner instead of a full xUnit/NUnit suite.

Quick tests are the normal inner loop:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --quick
```

Portable tests avoid private local fixtures:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --portable
```

Backend-neutral integration tests require the local ScheduleOne workspace and game paths:

```powershell
dotnet run --project .\tests\S1Interop.Tests\S1Interop.Tests.csproj -c Debug -- --integration-backend-neutral
```

## Real-mod testing rule

Do not mutate real local mod projects during test probes.

The safe validation shape is:

1. Copy the target mod to a temporary directory.
2. Pack the current generator into a temporary local NuGet feed if generator changes are being tested.
3. Point the temp copy at that feed.
4. Run migration or SDK generation.
5. Build the requested Mono and IL2CPP configurations.
6. Delete the temporary directory.

This keeps local projects clean and avoids accidentally testing stale published packages.

