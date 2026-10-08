# FAQ

## Do I need the Unity Editor?

No. Use a C# editor and the .NET SDK. The [first-mod walkthrough](first-mod.md) creates a project that builds against your installed game.

## Do I need both game installations?

Mono builds need Mono game metadata. IL2CPP builds need that metadata plus a matching IL2CPP installation with MelonLoader's generated interop assemblies. Configure both to build and test both outputs.

## Do I need wrappers or runtime conditionals?

Write ordinary `ScheduleOne.*` source. The compiler adapts supported operations and publicizes compile references for supported non-public access.

The compiler selects existing `MONO` branches for both outputs. It skips existing `IL2CPP` branches, so bring any independent features from those branches into your author source. Framework-specific symbols still follow the selected framework.

## Does every game operation work?

Not yet. Some source patterns and native operations remain unsupported. Publicization cannot restore missing APIs or stripped native code. See [Compatibility](compatibility.md).

Test the mod's actual feature on each runtime. Successful compilation alone does not establish gameplay or multiplayer compatibility.

## What do players install?

Players install the mod DLL for their runtime and its dependencies. Compiler-built IL2CPP mods also need compatible `S1Interop.Runtime.dll` support in `UserLibs`. They do not install the compiler tool.

See [Test and distribute a mod](distributing-mods.md) for archive contents.

## Can I use S1API or another library?

Yes, when its dependency APIs are compatible with your compiler build. Adding compiler imports to a mod does not automatically adapt prebuilt libraries.

[Existing-mod adoption](compiler-adoption.md) covers dependencies. [S1API and S1Interop](s1api-and-s1interop.md) explains their different responsibilities.

## Why does installation build from source?

The alpha.2 compiler candidate is not published yet. Published alpha.1 contains the older generator workflow. Follow [Install S1Interop](getting-started.md) for the version these docs use.

## Where are the generator and migration guides?

They are under [Advanced](advanced.md), along with diagnostics-only use and the experimental facade workflow.
