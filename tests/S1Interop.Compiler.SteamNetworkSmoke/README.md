# Real SteamNetworkLib compiler probe

This probe compiles an audited copy of existing SteamNetworkLib source through the source compiler, then optionally exercises it inside both game runtimes. It never imports or builds the original project. Library and game source are supplied locally, not vendored here.

Requires PowerShell 7 and the repository's .NET 8 SDK. From the S1Interop repository root:

```powershell
./tests/Test-CompilerSteamNetwork.ps1 `
  -SourceRoot '<SteamNetworkLib checkout>' `
  -SourcesList '<audited absolute source paths, one per line>' `
  -MonoGamePath '<matching Mono test installation>' `
  -Il2CppGamePath '<matching IL2CPP test installation>' `
  -RunRuntime
```

Omit `-RunRuntime` for compilation only. The script builds the local CLI, then builds the controlled library and probe serially for each backend. Source selection must match the original compile items and exclusions. The manifest is caller-audited; the script does not evaluate the original project. Duplicate, relative, and out-of-root entries are rejected. Do not include generated `bin` or `obj` sources.

The base selection uses 49 source files. Its selected sources do not reference Opus types, so this harness does not require the original project's unused Opus package. Other library revisions may require updating the test.

For the streaming-buffer extension, add the unchanged `Examples/AudioStreamingExample/StreamingAudioBuffer.cs` to the audited manifest and pass `-AuthoringDefines SCHEDULE_ONE_INTEGRATION -ExpectedChecks 31`. This explicitly selects an example excluded by the original library build; it is not a claim about the original project configuration. The compiler selects the same Mono authoring branch on both backends. Feature symbols are recorded in the result, and `MONO`/`IL2CPP` overrides are rejected.

Live runs use `Run-CompilerRuntimeSmoke.ps1`. Each installation must be a dedicated test copy containing `.s1interop-compiler-test`, with no existing DLLs in `Mods`, `Plugins`, or `UserLibs`. The runner launches and stops its own game process and removes its matching deployment. Do not use your normal play installation.

## Scope

`RuntimeChecks.cs` is compiled into the copied library to use its internal dedicated-mode constructor. Its transport override loops packets back locally; no Steam peer connection is used. Twenty-seven checks cover send metadata, return values, shared byte-array mutation, Base64 encoding, retention after managed collection, text dispatch, and subscription disposal. Binary file and stream messages additionally exercise header encoding/decoding, array copies, independent deserialized payload storage, enum and nullable fields, and truncated or oversized header handling.

The optional extension constructs the original streaming buffer with a muted audio source and invokes the game clip's PCM reader callback. Four additional checks cover callback construction, shared output-array writes, consumed samples/silence, and reset. It does not start playback or validate the audio device, audio thread, Opus codec, or voice transport.

Current f9 evidence: the extension passes 31 checks on Mono but fails on IL2CPP because the generated `AudioClip` PCM event accessor throws `Method unstripping failed`. The compiler's direct PCM delegate checks pass separately. Keep this extension as a failing real-source regression case until the accessor reconstruction gap is resolved.

`Mod.cs` runs these checks once in Menu and emits a token-tagged result. This is not evidence for Steam networking, multiplayer gameplay, voice, resource packaging, or the original project's build settings.

## Evidence

`artifacts/compiler-steam/<run-id>` retains build logs, source and dependency fingerprints, installation verification, and combined results. `BuildsPassed` and `RuntimePassed` are separate; the latter is null when runtime testing was not requested. Live artifacts and loader logs also remain in `artifacts/compiler-smoke`. Original source hashes are checked again at completion, and every audited source must appear in the native library's author-source manifest. The unique temporary build workspace, including copied source and generated game references, is removed. Cleanup failures are recorded without replacing the original failure. Evidence directories are ignored by Git.
