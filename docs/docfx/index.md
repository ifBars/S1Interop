---
_layout: landing
---

<section class="s1interop-hero">
  <p class="s1interop-eyebrow">Schedule One interop tooling</p>
  <h1>Move Mono mods toward IL2CPP without turning every source file into runtime glue.</h1>
  <p class="s1interop-lead">S1Interop analyzes Schedule One mod projects, generates backend-neutral SDK facades, migrates safe source patterns, and verifies the result in disposable sandboxes.</p>
  <div class="s1interop-actions">
    <a class="s1interop-action s1interop-action-primary" href="articles/adoption-guide.md">Choose your path</a>
    <a class="s1interop-action" href="articles/s1api-and-s1interop.md">S1API comparison</a>
    <a class="s1interop-action" href="articles/getting-started.md">Install</a>
    <a class="s1interop-action" href="articles/core-concepts.md">Core concepts</a>
    <a class="s1interop-action" href="articles/backend-neutral-sdk.md">Backend-neutral SDK</a>
  </div>
</section>

<section class="s1interop-section">
  <div class="s1interop-section-heading">
    <p class="s1interop-eyebrow">Use cases</p>
    <h2>Pick the path that matches the mod in front of you.</h2>
  </div>

  <div class="s1interop-card-grid">
    <a class="s1interop-card" href="articles/adoption-guide.md">
      <h3>Choose the right first command</h3>
      <p>Separate first-time mod scaffolds, existing mod migration, SDK generation, and sandbox verification before editing a project.</p>
    </a>
    <a class="s1interop-card" href="articles/s1api-and-s1interop.md">
      <h3>Use the right layer</h3>
      <p>Keep S1API for curated content workflows. Use S1Interop for generated direct game access, migration, and low-level backend glue.</p>
    </a>
    <a class="s1interop-card" href="articles/new-projects.md">
      <h3>Start backend-neutral</h3>
      <p>Create a new project with Mono and IL2CPP configurations, local path props, and generated SDK declarations from the start.</p>
    </a>
    <a class="s1interop-card" href="articles/migrate-to-backend-neutral.md">
      <h3>Migrate an existing mod</h3>
      <p>Move a Mono-only project toward one backend-neutral assembly using generated SDK declarations and compile-time checks.</p>
    </a>
    <a class="s1interop-card" href="articles/diagnostics.md">
      <h3>Catch IL2CPP failures earlier</h3>
      <p>Move known runtime-only failures, such as unsafe buffers and IL2CPP boundary collections, into compiler diagnostics.</p>
    </a>
  </div>
</section>

<section class="s1interop-section">
  <div class="s1interop-section-heading">
    <p class="s1interop-eyebrow">Current alpha</p>
    <h2>Useful now, still honest about the rough edges.</h2>
  </div>

  <div class="s1interop-card-grid s1interop-card-grid-wide">
    <a class="s1interop-card" href="articles/backend-neutral-sdk.md">
      <h3>Generated facades</h3>
      <p>Generate `S1Interop.ScheduleOne.*` facades from local reference metadata instead of hand-maintaining wrapper catalogs.</p>
    </a>
    <a class="s1interop-card" href="articles/generator-package.md">
      <h3>Generated at build time</h3>
      <p>The Roslyn generator emits facades, registries, and diagnostics during compilation. See what is generated and when symbols appear.</p>
    </a>
    <a class="s1interop-card" href="articles/local-paths.md">
      <h3>Local paths stay local</h3>
      <p>Use ignored `local.build.props` files for each developer's Mono and IL2CPP game installs.</p>
    </a>
    <a class="s1interop-card" href="contributors/testing.md">
      <h3>Verification paths</h3>
      <p>Run quick, portable, and real-mod integration tests without mutating a developer's real mod checkout.</p>
    </a>
    <a class="s1interop-card" href="contributors/real-mod-evidence.md">
      <h3>Real-mod evidence</h3>
      <p>Track which real Schedule One mods have exercised the migration and backend-neutral paths so claims stay grounded.</p>
    </a>
  </div>
</section>

<section class="s1interop-section s1interop-bottom-links">
  <a href="articles/commands.md">Commands</a>
  <a href="articles/generator-package.md">Generated output</a>
  <a href="api/S1Interop.Core.yml">API reference</a>
  <a href="contributors/index.md">Contributors</a>
  <a href="https://github.com/ifBars/S1Interop">GitHub repository</a>
</section>
