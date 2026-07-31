---
_layout: landing
---

<section class="s1interop-hero">
  <p class="s1interop-eyebrow">Schedule I modding</p>
  <h1>Build and validate Schedule I mods across Mono and IL2CPP.</h1>
  <p class="s1interop-lead">S1Interop guides local setup, adds compile-time diagnostics, analyzes existing mods, previews safe migrations, and validates both runtime reference surfaces. Backend-neutral facades are an opt-in experiment.</p>
  <div class="s1interop-actions">
    <a class="s1interop-action s1interop-action-primary" href="articles/first-mod.md">Build your first mod</a>
    <a class="s1interop-action" href="articles/introduction.md">What S1Interop does</a>
    <a class="s1interop-action" href="articles/getting-started.md">Install S1Interop</a>
    <a class="s1interop-action" href="articles/adoption-guide.md">I already have a mod</a>
  </div>
</section>

<section class="s1interop-section">
  <div class="s1interop-section-heading">
    <p class="s1interop-eyebrow">Start small</p>
    <h2>Get one result, then add the next piece.</h2>
  </div>

  <div class="s1interop-card-grid">
    <a class="s1interop-card" href="articles/first-mod.md">
      <h3>Build both runtime targets</h3>
      <p>Diagnose local paths, build explicit Mono and IL2CPP DLLs, deploy one, and check the exact log message.</p>
    </a>
    <a class="s1interop-card" href="articles/common-tasks.md">
      <h3>Run the common checks</h3>
      <p>Validate setup, check both reference surfaces, analyze an existing mod, or preview a migration.</p>
    </a>
    <a class="s1interop-card" href="articles/s1api-and-s1interop.md">
      <h3>Know when to use S1API</h3>
      <p>Keep items, NPCs, quests, UI, and save workflows in S1API. Use S1Interop for the direct game access left over.</p>
    </a>
    <a class="s1interop-card" href="articles/adoption-guide.md">
      <h3>Work with an existing mod</h3>
      <p>Start with read-only analysis, then choose diagnostics, separate runtime builds, or an opt-in facade experiment.</p>
    </a>
    <a class="s1interop-card" href="articles/troubleshooting.md">
      <h3>Fix the first error</h3>
      <p>Match missing paths, package restores, generated symbols, runtime detection, and migration failures to concrete checks.</p>
    </a>
  </div>
</section>

<section class="s1interop-section">
  <div class="s1interop-section-heading">
    <p class="s1interop-eyebrow">Go deeper</p>
    <h2>Read these when the basic project works.</h2>
  </div>

  <div class="s1interop-card-grid s1interop-card-grid-wide">
    <a class="s1interop-card" href="articles/backend-neutral-sdk.md">
      <h3>Experimental generated facades</h3>
      <p>Evaluate the fragile `S1Interop.ScheduleOne.*` facade path while retaining an explicit dual-runtime fallback.</p>
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
