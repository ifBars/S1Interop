# FAQ

Answers to the most common questions about S1Interop: migration paths, alpha limitations, safety model, and when to use each package.

If you are unsure which migration path to take, what the tool will and won't do automatically, or how the two packages fit together, start here.

## Which migration path should I choose: backend-neutral or dual-runtime?

It depends on how much of your mod uses runtime-specific behaviour:

- **Backend-neutral** produces a single assembly that compiles and runs against either Mono or IL2CPP using generated `S1Interop.ScheduleOne.*` facades. This is the preferred long-term direction and the one the tool is most actively built around.
- **Dual-runtime** produces two separate assemblies — one targeting Mono, one targeting IL2CPP — and keeps your runtime-specific code paths intact. It is a lower-risk bridge step because it does not require rewriting runtime-specific logic immediately.

> [!TIP]
> Start with dual-runtime if your mod relies on Harmony transpilers, managed collection callbacks at IL2CPP boundaries, or other patterns that are hard to abstract away. Move toward backend-neutral as those surfaces are covered and the tool matures.

Both paths produce a rollback manifest so you can undo the migration if you change your mind.

## Do I need both the CLI and the generator package?

You usually need both:

- The **CLI** (`S1Interop`) writes declaration files, migrates source, updates project files, and verifies results in sandboxes. It runs on demand from a terminal and never runs during compilation.
- The **generator package** (`S1Interop.Generators`) reads those declarations and emits facades, runtime helpers, and diagnostics during every build and IDE design-time compilation of your mod project.

The exception: if your project already has declarations and you only want the generated SDK surface, you can reference just `S1Interop.Generators` and author declarations by hand. The CLI is the recommended way to produce those declarations, but it is not a runtime dependency of the generator.

> [!NOTE]
> The `S1Interop.Generators` package ships only a Roslyn analyzer DLL under `analyzers/dotnet/cs`. It does not add a runtime assembly to your mod's output.

## Will S1Interop convert my entire mod automatically?

No, and that is by design. S1Interop is intentionally conservative: if it cannot prove a rewrite is safe, it produces a source-risk report instead of guessing. This means:

- Advanced mods that use Harmony transpilers, reflection, or tightly coupled IL2CPP patterns may still need explicit `S1InteropMember` declarations or small manual source edits.
- Unsupported patterns surface as diagnostics (`S1I004`-`S1I007`) or entries in the source-risk report — never as silent, incorrect rewrites.

Think of S1Interop as a tool that handles the mechanical parts of migration safely and flags the hard parts for your review, not a single-command converter.

## Does S1Interop redistribute Schedule One game files?

Never. S1Interop generates facades from local reference metadata you already have on disk — game assemblies you own through your own Schedule One install. It does not:

- commit game assemblies to version control,
- include game files in generated NuGet packages,
- redistribute IL2CPP wrappers, decompiled source, or game assets.

The `local.build.props` file that holds your install paths is gitignored precisely to keep those machine-specific paths out of source control.

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

That said, `sdkgen` is the recommended starting point because it inspects your real source usage, existing aliases, namespace imports, and string-held game type names, then emits only the declarations your project appears to need. Hand-authoring is most useful for adding `S1InteropMember` bindings for private members or overloaded methods that the automatic facade cannot safely infer.

## What does --dry-run do vs --apply?

- **`--dry-run`** shows every operation S1Interop would perform — source rewrites, project edits, solution updates, generated declarations — without writing any files. Use it to understand the full scope of a migration before committing.
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

It is gitignored because every developer installs Schedule One in a different location. Committing those paths would break the build for anyone with a different install path and would reveal local file-system layout in a public repository.

**To set it up:** copy `local.build.props.example` (which is committed) to `local.build.props` and fill in your own paths. The example file is the template; your filled-in copy stays local.

## When do generated symbols appear in IntelliSense?

Generated symbols appear after a design-time build (triggered automatically by your IDE) or a full build (`dotnet build`). The timeline is:

1. You edit or add a declaration in `S1Interop.BackendNeutral.cs`.
2. You save the file.
3. Your IDE runs a design-time build in the background, or you run `dotnet build` manually.
4. The new facade classes, `Handle` types, and member accessors appear in IntelliSense and are compiled into your assembly.

If symbols are missing immediately after editing a declaration file, build the project once — that is almost always the cause. Generated symbols are not emitted at restore time; they require at least one compilation pass.

## Related pages

- [Troubleshooting](troubleshooting.md)
- [Core concepts](core-concepts.md)
- [Generated output](generator-package.md)
- [Declarations](backend-neutral-declarations.md)
