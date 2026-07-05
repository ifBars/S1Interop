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

## Site structure

The published site has three top-level surfaces:

- **Docs**: conceptual articles under `articles/`, split into Get started, CLI reference, Generator package, and Workflows.
- **API**: generated reference under `api/`, filtered to stable `S1Interop.Core` contracts and entry points.
- **Contributors**: architecture, product direction, real-mod evidence, testing, publishing, and contributing notes.

When adding a new article, place it under the matching section in `docs/docfx/articles/toc.yml`. Contributor-only material goes under `docs/docfx/contributors/` and is also listed in `docs/docfx/contributors/toc.yml`.
