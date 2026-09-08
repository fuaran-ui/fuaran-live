import { fileURLToPath } from 'node:url';

import { defineConfig } from '@playwright/test';

// Phase 1628 — the render-latency capture, driven in a real browser.
//
// Serves the pre-built `dist-measure/` (the `measure` npm script runs
// `build:measure` first) and drives measure.html, whose F#/Fable harness renders
// the fixed corpus through the real Fuaran.UI.Renderer and returns a captured
// perf-baseline artefact.
//
// SERIAL, ONE WORKER, NO RETRIES, and each of those is a measurement decision
// rather than a default:
//   * a parallel worker is a second browser competing for the same cores, which
//     is noise injected into the instrument by the instrument;
//   * a retry would silently re-run a capture that failed its own sanity check,
//     which is the one failure a timing harness must never paper over.
//
// The preview port is 24041 — inside the client band this repo's own
// `ports.json` declares (24040-24049), one above the dev server's 24040, so a
// dev server and a measure run can coexist on a machine. Deliberately NOT the
// 14041 that would sit beside the parity gate's preview port: 14040-14049 is
// claimed in the workspace port registry by a different app, and this repo
// declares no server band at all.

const PORT = 24041;

export default defineConfig({
  testDir: '.',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  // A capture is `repeats` renders per corpus tree plus warm-ups; the default
  // 30s is comfortable for the committed defaults and tight for a deliberately
  // long spread run.
  timeout: 300_000,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: `http://localhost:${PORT}`,
  },
  webServer: {
    command: `vite preview --outDir dist-measure --port ${PORT} --strictPort`,
    // Playwright spawns the command with cwd = this config file's directory,
    // where vite preview finds no build output — pin the repo root.
    cwd: fileURLToPath(new URL('../..', import.meta.url)),
    url: `http://localhost:${PORT}/measure.html`,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
