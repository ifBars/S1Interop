# Native comparer lifetime diagnostic

This harness binds directly to the installed IL2CPP runtime. The separate `RuntimeSmoke` project remains the unchanged-source Mono/IL2CPP authoring example.

Build the compiler library against matching local game installations, then build and run this diagnostic:

```powershell
dotnet build tests/S1Interop.Compiler.RuntimeLibrary -c Il2Cpp -p:MonoGamePath="<mono-copy>" -p:Il2CppGamePath="<native-copy>"
dotnet build tests/S1Interop.Compiler.NativeLifetimeSmoke -c Release -p:Il2CppGamePath="<native-copy>"
./tests/Run-CompilerRuntimeSmoke.ps1 -GamePath "<native-copy>" -ModPath tests/S1Interop.Compiler.NativeLifetimeSmoke/bin/Release/net6.0/S1Interop.Compiler.NativeLifetimeSmoke.dll -Runtime Il2Cpp
```

The runner requires an isolated, otherwise empty mod installation marked with `.s1interop-compiler-test`. It loads only the menu, captures token-tagged results and binary hashes, and removes its deployed DLLs afterward. No saves or network submissions are used. Game assemblies remain local references and are not copied to the build output.

Five string comparers exercise custom equality, collisions, and original comparer identity. Each frame forces both managed and native collection. Retained native dictionaries must keep their comparers alive. Released owners must permit native adapter and managed comparer reclamation within the bounded observation period. Two comparers remain managed-rooted while their old adapters are reclaimed; new dictionaries must recreate adapters that preserve their behavior. Final release must reclaim those adapters and comparers too.

The harness tracks native objects through weak handles, separates helpers to reduce accidental stack roots, and clears stale stack memory before collection because the native collector is conservative. A timeout is a failure requiring investigation, not evidence that collection can never occur.

This verifies the installed runtime and these controlled game-thread cases. It does not prove concurrent collection safety, all key types, or exception propagation from comparer callbacks.
