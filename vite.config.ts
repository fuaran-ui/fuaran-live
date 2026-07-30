import { createHash } from 'node:crypto';
import { promises as fs } from 'node:fs';
import path from 'node:path';

import react from '@vitejs/plugin-react';
import { defineConfig, type Plugin } from 'vite';

import { PROVIDER_ORIGINS } from './src/byok/origins';

// ─── Content-Security-Policy ─────────────────────────────────────────────────
// The shipped static app is locked down: the only origins it may open a network
// connection to are the BYOK provider endpoints (Claude / OpenAI / Gemini /
// Kimi). This
// is the structural guard behind the "the key never leaves for any non-provider
// origin" posture — a strict connect-src means even a compromised dependency
// cannot exfiltrate the key to an attacker origin. The allow-list is the exact
// PROVIDER_ORIGINS set (imported from src/byok/origins, the same constants each
// adapter fetches), so the CSP and the egress code cannot drift apart — adding a
// provider is one entry in that file. `style-src 'unsafe-inline'` is required
// because the React renderer sets inline `style` attributes and injects the
// Theme's CSS custom properties inline at the render root; no inline *scripts*
// are allowed.
const PROVIDER_ORIGINS_CSP = PROVIDER_ORIGINS.join(' ');

// Live-drive Stage 2 (Phase 295) — cross-device WebRTC pairing uses a public
// STUN server (`WebRtc.stunServer`, app/WebRtc.fs) for NAT traversal during the
// handshake. That egress is deliberately NOT in `connect-src`: CSP has no valid
// source expression for the `stun:` scheme, and browsers do not gate
// `RTCPeerConnection` ICE on `connect-src` at all (CSP3 reserves a separate
// `webrtc` directive for it, defaulting to allow). The `stun:` entry this
// policy carried until 2026-07-30 was reported invalid and ignored by Chrome —
// pairing worked because the directive never applied, not because of the entry.

// BYOS (bring your own server) — the Go sessions page connects to a session server
// the visitor runs on their OWN machine as one binary (default http://localhost:14050,
// configurable). Any loopback port is allowed; no remote origin is. This is a
// deliberate, minimal relaxation of the showcase's zero-egress posture — like the STUN
// and Pyodide exceptions above, it is documented, narrow (loopback only), and the data
// is the user's own local session, never key-bearing egress. Kept in sync with
// GoSessions.defaultBaseUrl (app/showcase/GoSessions.fs).
const BYOS_LOCAL_ORIGINS = 'http://localhost:* http://127.0.0.1:*';

// Phase 85 dual-host mode frames the two same-origin render-host pages
// (ts-host.html + fable-host.html), so `frame-src 'self'` is required. It is a
// same-origin-only relaxation — no third-party frames — and is harmless when the
// flag is off (no iframes are created), so it stays in the baseline policy.
//
// Three 2026-07-30 corrections, each from a real console violation in the
// deployed artifact:
//  • `script-src` carries the sha256 of each entry's inline pre-paint theme
//    script (computed from the HTML at build time, so an edit can never drift
//    the hash) — under a bare `'self'` the browser blocked it and every
//    dark-preference load flashed light.
//  • `font-src` allows `data:` — Vite inlines assets under 4 KB as data: URIs,
//    which turned the smallest font subset into a blocked data: font.
//  • `frame-ancestors` is HEADER-ONLY by spec — in a <meta> policy it is
//    ignored (with a console warning), so it lives in
//    public/staticwebapp.config.json's globalHeaders instead, where it is
//    actually enforced.
const prodCsp = (scriptHashes: string) =>
  [
    `default-src 'self'`,
    `connect-src 'self' ${PROVIDER_ORIGINS_CSP}`,
    `img-src 'self' data:`,
    `style-src 'self' 'unsafe-inline'`,
    `script-src 'self'${scriptHashes}`,
    `font-src 'self' data:`,
    `frame-src 'self'`,
    `base-uri 'none'`,
    `object-src 'none'`,
    `form-action 'none'`,
  ].join('; ');

// The showcase entries (showcase.html + receiver.html) are the zero-key-egress
// half of the site: no key is ever pasted (every page delivers its wow from
// recorded, scripted replay artefacts), so there is NO provider origin in this
// policy. `connect-src 'self'` covers fetching the recorded replay artefacts +
// the CI conformance report JSON from the site's own origin. Combined with the
// entries' bundles importing none of the provider machinery, the claim is
// enforced structurally, not just asserted.
//
// Pyodide exception (the Rosetta page): the "one wire, many hosts" page runs a
// genuine Python host in-browser — CPython compiled to WebAssembly, loaded from
// the pinned Pyodide distribution on the jsDelivr CDN, lazily, only when the
// visitor opts in. That needs the CDN in `script-src`/`connect-src` and
// `'wasm-unsafe-eval'` to instantiate the WebAssembly module. No `'unsafe-eval'`
// and no other origin; the "no key, no provider egress" posture is otherwise
// intact (Pyodide is a static runtime download, not a data/LLM egress).
const pyodideCdn = 'https://cdn.jsdelivr.net';
const showcaseCsp = (scriptHashes: string) =>
  [
    `default-src 'self'`,
    `connect-src 'self' ${pyodideCdn} ${BYOS_LOCAL_ORIGINS}`,
    `img-src 'self' data:`,
    `style-src 'self' 'unsafe-inline'`,
    `script-src 'self' 'wasm-unsafe-eval' ${pyodideCdn}${scriptHashes}`,
    `worker-src 'self' blob:`,
    `font-src 'self' data:`,
    `base-uri 'none'`,
    `object-src 'none'`,
    `form-action 'none'`,
  ].join('; ');

// Vite's dev server relies on inline scripts + eval (HMR) and a websocket, so
// the strict policy is applied only to the production build; dev gets a relaxed
// variant. The shipped `dist/index.html` always carries `prodCsp`.
const devCsp = [
  `default-src 'self'`,
  `connect-src 'self' ${PROVIDER_ORIGINS_CSP} ${pyodideCdn} ${BYOS_LOCAL_ORIGINS} ws: wss:`,
  `img-src 'self' data:`,
  `style-src 'self' 'unsafe-inline'`,
  `script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' ${pyodideCdn}`,
  `worker-src 'self' blob:`,
  `font-src 'self' data:`,
  `frame-src 'self'`,
].join('; ');

// Phase 85 — dual-host build. With VITE_DUAL_HOST set, the build emits the two
// extra render-host pages (ts-host.html + fable-host.html) alongside index.html.
// The fable-host page imports the Fable-compiled bundle, so this is the ONLY
// path that requires `dotnet fable` to have run first. With the flag off (the
// MVP default) the build is single-page and needs no Fable toolchain.
const dualHost = process.env.VITE_DUAL_HOST === '1' || process.env.VITE_DUAL_HOST === 'true';

// Two-origin build modes. The default build is the PLAYGROUND artifact
// (index.html → app/output/App.js). `VITE_SITE=showcase` builds the SHOWCASE
// artifact instead: showcase.html alone, emitted to dist-showcase/ and renamed
// to index.html so a static host serves it at the origin root. Each artifact
// carries only its own entry's code — the showcase bundle imports none of the
// provider/key machinery, so its zero-egress posture is inspectable in the
// shipped files.
const showcaseSite = process.env.VITE_SITE === 'showcase';

// `index.html` is the entirely-F#/Fable app (it loads app/output/App.js, the
// Fable-compiled Fuaran.Live.App). The optional VITE_DUAL_HOST flag additionally
// emits the two wire-format parity render-host pages (ts-host.html + fable-host.html).
const buildInputs: Record<string, string> = { main: 'index.html' };
if (dualHost) {
  buildInputs.tsHost = 'ts-host.html';
  buildInputs.fableHost = 'fable-host.html';
}

// The sha256 source expressions for every inline <script> in a page, computed
// from the page bytes the browser will hash — so the pre-paint theme script
// runs under a strict `script-src 'self'` without `'unsafe-inline'`, and an
// edit to the script re-derives the hash rather than silently breaking it.
function inlineScriptHashes(html: string): string {
  const hashes = [...html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi)].map(
    (m) =>
      `'sha256-${createHash('sha256')
        .update(m[1] ?? '', 'utf8')
        .digest('base64')}'`,
  );
  return hashes.length > 0 ? ` ${hashes.join(' ')}` : '';
}

function cspPlugin(): Plugin {
  return {
    name: 'fuaran-live-csp',
    transformIndexHtml: {
      // 'post' so the hash pass sees the final document — any script another
      // transform injected would otherwise be blocked by the shipped policy.
      order: 'post',
      handler(html, ctx) {
        const isShowcase =
          ctx.filename.endsWith('showcase.html') || ctx.filename.endsWith('receiver.html');
        const policy = ctx.server
          ? devCsp
          : (isShowcase ? showcaseCsp : prodCsp)(inlineScriptHashes(html));
        const meta = `<meta http-equiv="Content-Security-Policy" content="${policy}" />`;
        return html.replace('<!--CSP-INJECTION-POINT-->', meta);
      },
    },
  };
}

// Showcase artifact only: rename the emitted showcase.html to index.html so the
// showcase origin's static host serves it at `/` with no rewrite rules.
function showcaseIndexPlugin(): Plugin {
  let outDir = 'dist-showcase';
  return {
    name: 'fuaran-live-showcase-index',
    apply: 'build',
    configResolved(config) {
      outDir = path.resolve(config.root, config.build.outDir);
    },
    async closeBundle() {
      // closeBundle also fires when the build FAILED (rollup runs it as
      // cleanup) — tolerate the missing file so the real error surfaces.
      await fs
        .rename(path.join(outDir, 'showcase.html'), path.join(outDir, 'index.html'))
        .catch(() => {});
    },
  };
}

// Port allocation: the Fuaran workspace CLAUDE.md "Port allocation" table
// reserves the website Vite band 24040–24049 for fuaran-live. The app is static
// (no server tier), so only the Vite dev port (24040) + preview (14040) are
// wired. `base: './'` emits relative asset URLs so the build runs from a plain
// static host AND directly from file://.
export default defineConfig({
  base: './',
  plugins: showcaseSite ? [react(), cspPlugin(), showcaseIndexPlugin()] : [react(), cspPlugin()],
  resolve: {
    // A single React instance must back the renderer + the host app (duplicate
    // React instances break hooks).
    dedupe: ['react', 'react-dom'],
  },
  optimizeDeps: {
    // The default app (index.html) is entirely F#/Fable and imports no
    // @fuaran-ui/* package. Vite's dependency pre-scan otherwise crawls EVERY
    // root .html — including the opt-in parity-host pages (ts-host.html /
    // fable-host.html), whose @fuaran-ui/* imports only resolve once the sibling
    // fuaran-ts tier has been built (its dist/ exists). Scoping the scan to the
    // real entry keeps default dev/build from depending on a built fuaran-ts;
    // dual-host mode adds the host pages back explicitly.
    entries: dualHost
      ? ['index.html', 'showcase.html', 'receiver.html', 'ts-host.html', 'fable-host.html']
      : ['index.html', 'showcase.html', 'receiver.html'],
  },
  build: showcaseSite
    ? {
        outDir: 'dist-showcase',
        rollupOptions: {
          // Two documents: the showcase shell and the bare teleport receiver
          // (HOST 2). The receiver is deliberately a separate, visibly vacant
          // page, self-contained so it can be deployed to a second origin
          // unchanged. The CSP plugin's transformIndexHtml applies to both.
          input: { main: 'showcase.html', receiver: 'receiver.html' },
        },
      }
    : dualHost
      ? { rollupOptions: { input: buildInputs } }
      : {},
  server: {
    port: 24040,
    strictPort: true,
    fs: {
      // The parity pane inlines wire-format-fixtures corpus files via `?raw`
      // (src/hosts/parityFixtures.ts). At build time they are bundled; in dev
      // they are served from the sibling checkout outside this repo root, which
      // Vite's fs guard denies by default. Allowing the parent covers the
      // corpus + the linked fuaran-ts dist — the same sibling-checkout posture
      // the @fuaran-ui link bridge and the fable-host ProjectReference assume.
      allow: ['.', '..'],
    },
  },
  preview: {
    port: 14040,
    strictPort: true,
  },
});
