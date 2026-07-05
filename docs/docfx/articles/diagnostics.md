# Diagnostics

S1Interop tries to move common IL2CPP failures from "runtime surprise" to compile-time feedback.

All diagnostics on this page are reported by the `S1Interop.Generators` Roslyn package during compilation. They do not require the CLI to run. See [Generated output](generator-package.md) for build timing and the conditions under which each diagnostic group stays quiet.

## Declaration diagnostics

When a build has access to Mono or IL2CPP game reference assemblies, the Roslyn generator validates generated declarations and overrides.

| Diagnostic | Meaning |
| --- | --- |
| `S1I001` | A requested game type could not be found. |
| `S1I002` | A member override references an unknown owner alias. |
| `S1I003` | A member name or method overload signature was not found on the owner type. |

These diagnostics stay quiet when no game reference surface is available, so package restore and docs-only builds do not fail just because local game paths are not configured.

## IL2CPP boundary diagnostics

IL2CPP builds also report source shapes that usually compile but fail later in-game.

| Diagnostic | Severity | Meaning |
| --- | --- | --- |
| `S1I004` | Error | Harmony transpiler usage in IL2CPP-facing code. |
| `S1I005` | Error | Managed collection parameters in IL2CPP-facing callback signatures. |
| `S1I006` | Error | Managed `byte[]` buffers passed to native/game fill APIs. |
| `S1I007` | Warning | Plain C# casts from object/proxy values to Unity object types that should route through `S1InteropObjectCast`. |

The checks are intentionally narrow. Normal managed collections and arrays remain fine inside ordinary mod logic.

