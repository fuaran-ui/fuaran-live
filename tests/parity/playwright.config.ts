import { fileURLToPath } from 'node:url';

import { defineConfig } from '@playwright/test';

// Phase 85 — cross-host DOM/CSS parity gate.
//
// Serves a pre-built dual-host `dist/` (the `parity` npm script runs
// `build:dual` first) and drives the two render-host pages directly, posting
// the same canonical wire JSON to each and asserting their rendered DOM is
// structurally identical with the same CSS class vocabulary + ARIA contract.
//
// The preview server uses fuaran-live's allocated preview port (14040). base
// is './', so the host pages are served at /ts-host.html + /fable-host.html.

const PORT = 14040;

export default defineConfig({
  testDir: '.',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: `http://localhost:${PORT}`,
  },
  // `vite preview` just serves the pre-built dist/ (the `parity` script runs
  // `build:dual` first, baking VITE_DUAL_HOST into the artefact), so no build-
  // time env is needed here.
  webServer: {
    command: `vite preview --port ${PORT} --strictPort`,
    // Playwright spawns the command with cwd = this config file's directory
    // (tests/parity), where vite preview finds no dist/ and dies — pin the
    // repo root so a cold `pnpm run parity` (and CI) actually starts the server.
    cwd: fileURLToPath(new URL('../..', import.meta.url)),
    url: `http://localhost:${PORT}/ts-host.html`,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
