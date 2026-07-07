# FAQ

Common questions about migration paths, packages, safety, and scope.

## Which migration path should I choose: backend-neutral or dual-runtime?

Choose based on how much runtime-specific code your mod has:

- **Backend-neutral**: one assembly uses generated `S1Interop.ScheduleOne.*` facades on Mono or IL2CPP.
- **Dual-runtime**: two assemblies, one for Mono and one for IL2CPP, keep runtime-specific code paths intact.

> [!TIP]
> Start with dual-runtime if your mod relies on Harmony transpilers, managed collection callbacks at IL2CPP boundaries, or other patterns that are hard to abstract away. Move toward backend-neutral as those surfaces are covered and the tool matures.

Both paths produce a rollback manifest so you can undo the migration if you change your mind.

## Do I need both the CLI and the generator package?

Most mods use both:

- The **CLI** (`S1Interop`) writes declaration files, migrates source, updates project files, and verifies results in sandboxes. It runs on demand from a terminal and never runs during compilation.
- The **generator package** (`S1Interop.Generators`) reads those declarations and emits facades, runtime helpers, and diagnostics during every build and IDE design-time compilation of your mod project.

If your project already has declarations and only needs the generated SDK surface, reference just `S1Interop.Generators` and author declarations by hand.

> [!NOTE]
> The `S1Interop.Generators` package ships only a Roslyn analyzer DLL under `analyzers/dotnet/cs`. It does not add a runtime assembly to your mod's output.

## Will S1Interop convert my entire mod automatically?

No. If S1Interop cannot prove a rewrite is safe, it produces a source-risk report instead of guessing.

- Advanced mods that use Harmony transpilers, reflection, or tightly coupled IL2CPP patterns may still need explicit `S1InteropMember` declarations or small manual source edits.
- Unsupported patterns surface as diagnostics (`S1I004`-`S1I008`) or source-risk entries.


## Does S1Interop redistribute Schedule One game files?

Never. S1Interop generates facades from local reference metadata on disk. It does not:

- commit game assemblies to version control,
- include game files in generated NuGet packages,
- redistribute IL2CPP wrappers, decompiled source, or game assets.

The `local.build.props` file that holds your install paths is gitignored precisely to keep those machine-specific paths out of source control.

## Does S1Interop replace S1API or other helper libraries?

No. S1Interop is a generated interop layer, not a gameplay API.

Use S1API when you want item, NPC, shop, saveable, or UI workflows. Use MAPI when you want building/model construction. Use SteamNetworkLib when you want Steam lobby or P2P helpers. Use DedicatedServerMod APIs for headless server/client extension points.

Use S1Interop when your mod or helper library still needs direct access to `ScheduleOne.*` or `Il2CppScheduleOne.*` types and you do not want every consumer to hand-maintain Mono and IL2CPP conditionals.

See [S1API and S1Interop](s1api-and-s1interop.md).

## Why are my declaration diagnostics (S1I001-S1I003) silent?

Declaration diagnostics only fire when Mono or IL2CPP reference assemblies are present in the compilation. When `MonoGamePath` and `Il2CppGamePath` are not configured, the generator has no game assembly surface to validate against and stays quiet so package-restore and docs-only builds do not fail.

**Fix:** Copy `local.build.props.example` to `local.build.props` and fill in both paths:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>C:\Program Files (x86)\Steam\steamapps\common\Schedule I</MonoGamePath>
    <Il2CppGamePath>C:\Program Files (x86)\Steam\steamapps\common\Schedule I IL2CPP</Il2CppGamePath>
  </PropertyGroup>
</Project>
```

Once both paths resolve to real game installs, `S1I001`-`S1I003` will fire on any declaration whose type or member cannot be found in those assemblies.

## Can I use sdkgen without running the CLI first?

Yes. If your project already references `S1Interop.Generators`, you can author declarations by hand in `S1Interop.Generated/S1Interop.BackendNeutral.cs` without ever running `sdkgen`. The generator will pick them up on the next build.

`sdkgen` is still the best starting point because it inspects real source usage, aliases, namespace imports, string-held game type names, and local metadata. Hand-authoring is mainly for `S1InteropMember` bindings that automatic discovery cannot infer.

## What does --dry-run do vs --apply?

- **`--dry-run`** shows operations without writing files: source rewrites, project edits, solution updates, and generated declarations.
- **`--apply`** writes the changes: modifies source files, updates `.csproj` and `.sln` files, writes declaration files, creates backups, and records a rollback manifest under `s1interop-runs/<run-id>/`.

> [!WARNING]
> Always review `--dry-run` output before running `--apply`. Once `--apply` runs you can roll back, but it is faster and less error-prone to catch surprises in the dry run first.

## How do I undo a migration?

Every applied migration writes backups of changed files and a rollback manifest under `s1interop-runs/<run-id>/`. To restore everything to its pre-migration state, run:

```powershell
s1interop migrate rollback .\s1interop-runs\<run-id>\manifest.json
```

Replace `<run-id>` with the directory name created by the `--apply` run you want to undo. The rollback restores all backed-up files and removes any files that were newly created by the migration.

## What is local.build.props and why is it gitignored?

`local.build.props` is a machine-specific MSBuild props file that holds your Schedule One install paths and, optionally, a pointer to a local NuGet feed for unpublished alpha packages:

```xml
<Project>
  <PropertyGroup>
    <MonoGamePath>C:\Program Files (x86)\Steam\steamapps\common\Schedule I</MonoGamePath>
    <Il2CppGamePath>C:\Program Files (x86)\Steam\steamapps\common\Schedule I IL2CPP</Il2CppGamePath>
    <!-- Optional: point at a local alpha package feed -->
    <S1InteropGeneratorPackageSource>..\S1Interop\artifacts\packages</S1InteropGeneratorPackageSource>
  </PropertyGroup>
</Project>
```

It is gitignored because install paths differ by machine. Committing it would break other developers and expose local file-system layout.

**To set it up:** copy `local.build.props.example` (which is committed) to `local.build.props` and fill in your own paths. The example file is the template; your filled-in copy stays local.

## When do generated symbols appear in IntelliSense?

Generated symbols appear after a design-time build (triggered automatically by your IDE) or a full build (`dotnet build`). The timeline is:

1. You edit or add a declaration in `S1Interop.BackendNeutral.cs`.
2. You save the file.
3. Your IDE runs a design-time build in the background, or you run `dotnet build` manually.
4. The new facade classes, `Handle` types, and member accessors appear in IntelliSense and are compiled into your assembly.

If symbols are missing immediately after editing declarations, build once. Generated symbols require a compilation pass; restore is not enough.

## Related pages

- [Troubleshooting](troubleshooting.md)
- [Core concepts](core-concepts.md)
- [Generated output](generator-package.md)
- [Declarations](backend-neutral-declarations.md)
