import { defineConfig } from 'vitest/config';

// Separate from vite.config.ts: vitest pulls its own (older) vite types, which
// clash with the app's vite@6 plugin types if merged into one file. The tests
// are pure TS (session / codec / provider logic) and need no React plugin, so a
// minimal node-environment config is all that's required.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['test/**/*.test.ts'],
  },
});
