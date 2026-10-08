# Compiler development sample

This sample uses build imports from the source checkout so compiler contributors can test local changes. Mod authors should follow [Build your first mod](../../docs/docfx/articles/first-mod.md).

## Configure local references

Build the CLI from the repository root:

```powershell
dotnet build src/S1Interop.Cli/S1Interop.Cli.csproj -c Release
```

Create an ignored `samples/SourceCompiler/local.build.props`:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>C:\Games\ScheduleI-Mono</MonoGamePath>
    <Il2CppGamePath>C:\Games\ScheduleI-Il2Cpp</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

Replace the example paths with matching game patch versions. Both installations need MelonLoader. Launch the IL2CPP installation once to generate its interop assemblies, then close it.

## Build the sample

Run these commands serially from the repository root:

```powershell
dotnet build samples/SourceCompiler/SourceCompiler.csproj -c Release -p:S1InteropCompilerRuntime=Mono
dotnet build samples/SourceCompiler/SourceCompiler.csproj -c Release -p:S1InteropCompilerRuntime=Il2Cpp
```

Outputs go under the sample's `bin/Release/<runtime>/<framework>` directory. Builds do not deploy or launch the game. Follow [Test and distribute a mod](../../docs/docfx/articles/distributing-mods.md) when loading an output.

Use [compiler contracts and runtime evidence](../../docs/SOURCE_COMPILER.md#verification) to validate compiler changes. A sample build alone does not establish gameplay compatibility.

## Earlier checkout overrides

Checkouts that used the removed `S1Interop.Compiler.Cli` executable need the current main CLI and matching build imports. Custom `S1InteropCompilerTool` overrides must point to `S1Interop.Cli.dll`; direct compiler invocations use its `compiler` command group.
