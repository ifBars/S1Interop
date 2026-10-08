# Native atomic event diagnostic

This native-only MelonLoader probe investigates the primitive needed to reconstruct field-like event accessors whose generated wrappers throw `Method unstripping failed`. It is not a compiler-built mod or a modification of game installation files. It includes an optional diagnostic event-accessor repair consumer. An optional lane exercises the compiler-generated delegate runtime helper.

Build the ordinary compiler runtime library first so the shared test runner has its usual payloads. Then, from the repository root:

```powershell
dotnet build tests/S1Interop.Compiler.NativeAtomicEventSmoke -c Release -p:Il2CppGamePath='<dedicated native test installation>'
./tests/Run-CompilerRuntimeSmoke.ps1 `
  -GamePath '<dedicated native test installation>' `
  -ModPath 'tests/S1Interop.Compiler.NativeAtomicEventSmoke/bin/Release/net6.0/S1Interop.Compiler.NativeAtomicEventSmoke.dll' `
  -Runtime Il2Cpp `
  -ProbeDisplayName 'S1Interop Native Atomic Event Smoke' `
  -ExpectedChecks 25
```

The installation must have generated interop assemblies, a `.s1interop-compiler-test` marker, and empty mod/user-library directories. The runner owns the launch, records hashes and token-tagged results, stops its process, and removes its unchanged deployment. Never use a normal play installation.

The probe creates an owned audio clip with no callbacks and no playback. Its helper resolves the native object `Interlocked.CompareExchange` overload, validates the exact signature, and passes the actual instance field address to native invocation. This avoids the wrapper overload's temporary local slot. The selected field must be a writable instance delegate field.

The first fifteen checks cover successful and unsuccessful CAS, native dispatch through stored callbacks, multicast order, removal, four workers adding/removing 64 handlers, and a barrier-forced collision that proves retries occurred. Worker threads attach to IL2CPP and detach afterward. Waits and cleanup are bounded; cancellation releases managed waits. If a native worker cannot finish, the probe retains its owner and synchronization objects until the runner stops the failed process.

Seven additional checks use native weak handles and managed weak references across separate update frames. They check an unrooted control, retention and invocation through the field alone, then reclamation after removal. Each phase waits at least eight frames and allows up to ten seconds for reclamation. Initial handle targets are validated; failed reclamation produces FAIL, not PASS.

On the verified IL2CPP 0.4.7f9 installation, the default stock-bridge run fails managed closure reclamation. The native control and removed native callback are reclaimed, while both managed closures remain alive. Inspection of the installed runtime shows mutually strong handles between its injected delegate bridge and managed peer.

For a controlled comparison, set `S1INTEROP_TEST_WEAK_CALLBACK_BRIDGE=1` in the launching environment and run the same command, then restore that environment variable. This diagnostic mode replaces the strong native handle on each newly created callback bridge with a weak handle. It validates the exact bridge type and private handle layout; the callback wrapper remains a strong owner during the replacement. The earlier 22-check lifetime probe passed in this mode; the extended acquisition probe is validated through the compiler lane below. The PASS line records `WeakBridge=True`. This direct private-layout intervention is separate from the compiler lane and does not make the stock run pass.

Set `S1INTEROP_TEST_COMPILER_CALLBACK_BRIDGE=1` instead to create the tracked callbacks through `S1InteropNativeDelegate.Convert<T>` from the actual generated runtime assembly. Rebuild the compiler runtime library before building this probe. The generated helper validates the installed bridge type, handle layout and callback identity, then weakens only the newly created bridge's native handle. This lane passes all 25 checks and records `CompilerBridge=True; WeakBridge=False` (as separate fields). The two intervention switches are mutually exclusive. Restore the environment variable after the run. The original fifteen atomic-update checks use direct delegates in all modes; the compiler lane applies to the seven lifetime checks. Three additional checks exercise pointer acquisition with a compiler-converted replacement callback.

The acquisition phase pauses an update after its raw pointer read and before wrapper creation. An attached worker removes the field reference and detaches; the reading thread then forces managed and native collection. A native weak handle confirms that the observed callback survived, the stale comparison retries exactly once, and dispatch invokes only the replacement. Later frame-based checks confirm that the removed callback and closure become reclaimable. This controlled interleaving passes on the verified f9 installation. Conservative stack scanning may preserve additional stale pointers; the test does not establish a universal collector guarantee or exercise every background-collection schedule. The production delegate cache's separate weak-handle acquisition path is not instrumented by this probe. The probe uses native object CAS, not generic method inflation.

The strict source recognizer in `AtomicEventBody` checks the complete original instance or static event loop, including the backing field, delegate operation, local slots, generic CAS signature, and retry target. `AtomicEventRepairPlan.Create(originalMonoBytes, originalWrapperBytes)` now produces a content-bound plan from those recognized loops. It records SHA-256 hashes, target assembly identity and module MVID, exact method tokens, callback types, native field names and add/remove operations. Publicized compiler references cannot supply the original bodies because their method IL is intentionally stripped.

To exercise the diagnostic consumer, serialize that plan with `System.Text.Json`, set `S1INTEROP_TEST_EVENT_REPAIR_PLAN` to its absolute local path, enable the compiler callback bridge lane, and run with `-ExpectedChecks 29`. Restore both environment variables afterward. This consumer selects instance accessors and validates the loaded wrapper image and each selected target signature before installing temporary Harmony prefixes on the failed managed wrapper methods. It unpatches its own prefixes at the end of the scene probe; installation files remain unchanged.

The verified f9 audio plan contains fourteen accessors: eight instance and six static. This diagnostic selects the eight instance accessors; the ordinary dual-runtime core smoke exercises the generated static repair path. Four additional checks exercise the four PCM accessors through `AudioClip.Create`'s internal calls, native callback dispatch, removal and re-addition. The complete probe passes29checks with `PlannedAccessors=8`. This standalone diagnostic consumer remains separate from the generated runtime implementation. Normal compiler project builds now plan repairs from prepared-reference provenance and install them at module startup. The unchanged 50-file streaming-buffer harness passes 31 checks per backend with that automatic path. Other planned accessors still lack dedicated behavioral reconstruction coverage.
