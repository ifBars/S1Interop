# S1API and S1Interop

S1API provides curated gameplay workflows. S1Interop's source compiler adapts ordinary direct game access across Mono and IL2CPP. The compiler does not require an S1API wrapper or a manually maintained facade declaration for each game type.

## Different responsibilities

A gameplay library can register content, manage save loading, or provide an NPC builder. Compiler adaptation preserves supported program behavior across backend type systems; it does not invent those gameplay rules.

You can write directly against game APIs or use higher-level libraries where they help. S1Interop's current compatibility limits still apply to direct access and to dependencies. Metadata discovery alone is not proof that every native operation is available or correctly adapted.

## Existing S1API mods

Keep the original mod as a comparison point and follow [existing-mod compiler adoption](compiler-adoption.md) in a separate project. Inventory references, source selection, resources, and runtime dependencies. A prebuilt library whose API differs between Mono and IL2CPP needs compatible dependency surfaces; adding compiler imports to its consumer does not automatically port the library.

Compiler-built libraries can expose original signatures through verified authoring companions. Existing manually built Mono/IL2CPP dependencies are a different case. Build and test the complete dependency combination on each runtime before claiming compatibility.

The current [real-mod evidence](https://github.com/ifBars/S1Interop/blob/main/docs/SOURCE_COMPILER.md#existing-mod-corpus) distinguishes unresolved dependencies and outdated game API calls from compiler failures. It does not establish automatic conversion of every S1API mod.

## Keeping an existing build workflow

You can use `analyze` or `lint` without adopting the compiler. The older generator package also provides diagnostics, helpers, and selected facades. See [Advanced](advanced.md) for these tools. They do not change the compiler's ordinary-source authoring model.

## Distribution

Preserve the runtime dependencies required by your gameplay libraries. Compiler-built IL2CPP output additionally needs compatible `S1Interop.Runtime.dll` support; Mono output does not. Follow [Test and distribute a mod](distributing-mods.md).

Keep local game references, generated IL2CPP proxies, decompiled code, and game assets out of public artifacts.
