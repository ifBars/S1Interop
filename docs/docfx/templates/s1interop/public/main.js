(function () {
  const root = document.documentElement;
  root.dataset.s1interopDocs = "ready";

  const externalLinks = document.querySelectorAll('a[href^="http"]');
  for (const link of externalLinks) {
    if (!link.hostname || link.hostname === window.location.hostname) {
      continue;
    }

    link.setAttribute("rel", "noreferrer");
  }
})();
