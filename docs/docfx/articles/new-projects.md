# New projects

`new` creates a compiler-enabled project by default:

```batch
s1interop new .\MyMod
s1interop new .\MyMod --apply
```

Preview does not create files. Apply requires an empty directory. The generated project contains ordinary game source, a pinned `.config/dotnet-tools.json`, matching `.s1interop` MSBuild files, an ignored local-path example, and a build/load README. It does not reference the generator package or deploy automatically.

Follow [Build your first mod](first-mod.md) to restore the tool, configure matching game installations, build both runtimes serially, and deploy their separate outputs. The compiler is an unpublished-candidate workflow; the published alpha.1 tool still has the earlier behavior.

## Earlier project styles

Existing generator users can request the former default explicitly:

```batch
s1interop new .\GeneratorMod --legacy-generator --apply
```

The separate one-DLL facade experiment remains opt-in:

```batch
s1interop new .\FacadeMod --backend-neutral --apply
```

These options are mutually exclusive. Neither route is the compiler workflow. See the [earlier generator walkthrough](legacy-generator-first-mod.md) for its helper APIs and deployment conventions.
