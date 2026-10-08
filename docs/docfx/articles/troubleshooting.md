# Troubleshooting

Start with the first failing diagnostic. These fixes apply to compiler projects created by the default `s1interop new` command.

## Missing tool or failed restore

From the project directory, run `dotnet tool restore`, then `dotnet tool run s1interop -- compiler --help`. The project uses its pinned compiler, even if you have a different version installed globally.

If NuGet cannot find that version, check that the manifest uses a [published version](https://www.nuget.org/packages/S1Interop). Keep the tool manifest and `.s1interop` build imports from the same scaffold generation. Contributors testing local packages should follow [Test a source build](../contributors/source-builds.md).

## Rejected game references or versions

Run `dotnet tool run s1interop -- doctor .` and check both paths in the ignored `local.build.props`. Paths must name the installation roots. Compiler authoring needs Mono metadata even for an IL2CPP output; IL2CPP builds also need generated native references from a matching patch version. Launch the native installation with MelonLoader to generate those references before building.

Doctor checks local reference availability. The build's installation verifier checks game versions and native generation provenance. Preserve the exact failure and correct the installation pair rather than bypassing verification.

## Ordinary game source fails in the native build

Compiler projects should keep their ordinary `ScheduleOne.*` imports. Verify that the project imports both compiler build files and builds through its pinned tool. Keep your ordinary author source and report unsupported compiler operations with a small reproducer.

`S1IC001` or `S1IC002` indicates missing or ambiguous target metadata. Other `S1IC` diagnostics identify unsupported runtime adaptation. Preserve the original source, the diagnostic, and a small reproducer. See the [compiler support and limits](compatibility.md) before interpreting successful compilation as equivalent gameplay behavior.

## The compiler-built mod fails to load

Deploy the output for that runtime. IL2CPP also requires the adjacent matching `S1Interop.Runtime.dll` in `UserLibs`. Do not deploy game references or authoring metadata. Close the game before replacing DLLs, and inspect the first relevant MelonLoader failure. All compiler-built mods loaded together must use compatible runtime support generations.

## Generator or migration errors

For existing projects using the older S1I generator diagnostics or migration tools, see [Legacy troubleshooting](legacy-troubleshooting.md) under Advanced.
