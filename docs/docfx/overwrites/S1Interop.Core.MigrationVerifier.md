---
uid: S1Interop.Core.MigrationVerifier
summary: Runs migration in a temporary sandbox and optionally builds the migrated result.
---

# MigrationVerifier

`MigrationVerifier` is the safer path for automation and regression tests.

It copies the target project to a temporary sandbox, applies planned automatic operations there, re-analyzes the result, and can build the migrated configurations when local game paths are available.
