# Publishing docs

The DocFX project lives in `docs/docfx`.

Build it locally:

```powershell
dotnet build .\S1Interop.sln -c Release
docfx .\docs\docfx\docfx.json
```

The generated site is written to:

```text
docs/docfx/_site
```

The GitHub Actions workflow at `.github/workflows/docs.yml` builds the solution, runs DocFX, uploads `docs/docfx/_site`, and deploys it to GitHub Pages on pushes to `main`.

## Local preview

Use DocFX's built-in preview server:

```powershell
docfx .\docs\docfx\docfx.json --serve
```

Do not commit `_site` or generated API metadata. The workflow regenerates both during publish.

