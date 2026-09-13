import { createHash } from 'node:crypto';
import { promises as fs } from 'node:fs';
import path from 'node:path';

import react from '@vitejs/plugin-react';
import { defineConfig, type Plugin } from 'vite';

import { PROVIDER_ORIGINS } from './src/byok/origins';
import { corpusSinkConnectSrc } from './src/corpus/sink';

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

// The opt-in anonymous session-corpus sink (app/Contribute.fs). An operator who
// runs their own collector sets VITE_CORPUS_SINK; that endpoint's origin — and
// only that one — is then added to the PLAYGROUND's connect-src.
//
// THE PUBLIC BUILD SETS NOTHING, so this is the empty string and the policy is
// byte-for-byte the policy it was before the feature existed. That is the
// property worth stating rather than assuming: a corpus feature that widened
// the shipped CSP by a character would have widened the app's egress surface
// for every visitor, whether or not anyone ever contributed. It does not, and
// test/corpusSink.test.ts pins the unset case against the provider list.
//
// The showcase policy is deliberately untouched: the showcase entries import
// none of the session machinery and have no session to contribute.
const CORPUS_SINK_CONNECT_SRC = corpusSinkConnectSrc(process.env.VITE_CORPUS_SINK);

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
// Trusted Types: both renderers this site serves — the F# renderer
// (Fuaran.UI.Renderer 0.77.0+) on index.html and fable-host.html, and the TypeScript
// renderer (@fuaran-ui/renderer 0.21.0+) on ts-host.html — mint every raw-HTML DOM
// sink through the same named policy, `fuaran-renderer`, whose only creator applies
// the renderer's own sanitiser. Requiring Trusted Types here has the browser refuse
// any string that reaches a sink another way, on every strict-policy page.
const trustedTypesDirectives = [
  `require-trusted-types-for 'script'`,
  `trusted-types fuaran-renderer`,
];

const prodCsp = (scriptHashes: string, trustedTypes = true) =>
  [
    `default-src 'self'`,
    `connect-src 'self' ${PROVIDER_ORIGINS_CSP}${CORPUS_SINK_CONNECT_SRC}`,
    `img-src 'self' data:`,
    `style-src 'self' 'unsafe-inline'`,
    `script-src 'self'${scriptHashes}`,
    `font-src 'self' data:`,
    `frame-src 'self'`,
    `base-uri 'none'`,
    `object-src 'none'`,
    `form-action 'none'`,
    ...(trustedTypes ? trustedTypesDirectives : []),
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
  `connect-src 'self' ${PROVIDER_ORIGINS_CSP}${CORPUS_SINK_CONNECT_SRC} ${pyodideCdn} ${BYOS_LOCAL_ORIGINS} ws: wss:`,
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

// Phase 1628 — the render-latency measurement build. With VITE_MEASURE set the
// build emits measure.html ALONE, into its own `dist-measure/`, and nothing
// else: the harness page is a measuring instrument, not part of the site, and
// no deploy workflow sets the flag. Its own outDir rather than a third entry in
// `dist/` so the shipped artifact is byte-for-byte what it was before the
// harness existed — a measurement page that shipped to visitors would be a
// worse outcome than no measurement at all.
const measureSite = process.env.VITE_MEASURE === '1' || process.env.VITE_MEASURE === 'true';

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

// Phase 450 — the legacy-host redirect. Each artifact serves at a custom domain
// (fuaran-ui.live / fuaran-ui.gallery), but the Static Web App's DEFAULT
// hostname keeps answering, and a static host cannot 301 by Host header. So the
// production document carries a pre-paint script: when it finds itself on an
// `*.azurestaticapps.net` hostname it replaces the location with the canonical
// origin, keeping path, query AND fragment — which is the reason it is a script
// rather than a meta-refresh: fragments do not ride a server redirect, and the
// share permalinks live in the fragment. Emitted ONLY when the build declares a
// canonical origin (VITE_CANONICAL_ORIGIN — set by the deploy workflows for
// main-branch builds, never for PR previews, whose hostnames are also
// `*.azurestaticapps.net` and must keep serving themselves). A build with the
// variable unset ships no redirect at all: dev, tests and previews are
// untouched. `location.origin !== o` is belt-and-braces against a
// misconfigured origin redirecting to itself.
const canonicalOrigin = (process.env.VITE_CANONICAL_ORIGIN ?? '').trim();
const LEGACY_REDIRECT_INJECTION_POINT = '<!--LEGACY-HOST-REDIRECT-INJECTION-POINT-->';
function legacyHostRedirectScript(origin: string): string {
  if (!/^https:\/\/[a-z0-9.-]+$/i.test(origin)) {
    throw new Error(`VITE_CANONICAL_ORIGIN must be a bare https origin, got: ${origin}`);
  }
  return (
    `<script>(function(){var o='${origin}';` +
    `if(location.origin!==o&&/\\.azurestaticapps\\.net$/i.test(location.hostname)){` +
    `location.replace(o+location.pathname+location.search+location.hash);}})();</script>`
  );
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
          ctx.filename.endsWith('showcase.html') ||
          ctx.filename.endsWith('receiver.html') ||
          ctx.filename.endsWith('ts-receiver.html');
        // Legacy-host redirect (Phase 450) — injected BEFORE the hash pass so it
        // ships under the strict policy like the theme script. See
        // `legacyHostRedirectScript` for what it does and when it is emitted.
        // A function replacer, so `$`-sequences in the script are never read
        // as String.replace substitution patterns.
        const redirect =
          !ctx.server && canonicalOrigin ? legacyHostRedirectScript(canonicalOrigin) : '';
        const withRedirect = html.replace(LEGACY_REDIRECT_INJECTION_POINT, () => redirect);
        const hashes = inlineScriptHashes(withRedirect);
        const policy = ctx.server ? devCsp : isShowcase ? showcaseCsp(hashes) : prodCsp(hashes);
        const meta = `<meta http-equiv="Content-Security-Policy" content="${policy}" />`;
        return withRedirect.replace('<!--CSP-INJECTION-POINT-->', meta);
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

// Port allocation, declared in this repo's own `ports.json` and validated by the
// workspace port registry: Vite dev band 24070–24079, server band 14070–14079.
// The app is static (no server tier), so only the dev port (24070) and preview
// (14070) are wired.
//
// Preview moved 14040 → 14070 on 2026-09-08. 14040 is formally claimed by another
// app in this band, and this repo's manifest declared no server band at all — so
// the registry read green while the two overlapped, because an undeclared port
// cannot clash with anything. Declaring the band is what makes the check real.
//
// The DEV band then moved 24040–24049 → 24070–24079 (roadmap-engine Phase 423), so
// the two halves share one slot INDEX: `1407x` with `2407x`. That phase made paired
// allocation the default in `roadmapctl ports claim` and added RM-PORT-UNPAIRED
// (Info), which named this claim — the 2026-09-08 move had left the server half at
// index 7 against a client half at index 4, two numbers to remember where a paired
// slot is one. Four claims in this band sat on a four-CYCLE of such pairs, so all
// four had to move together: no single one of them could be paired on its own,
// because each one's target was held by the next.
//
// `base: './'` emits relative asset URLs so the build runs from a plain static
// host AND directly from file://.
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
    entries: measureSite
      ? ['measure.html']
      : dualHost
        ? [
            'index.html',
            'showcase.html',
            'receiver.html',
            'ts-receiver.html',
            'ts-host.html',
            'fable-host.html',
          ]
        : ['index.html', 'showcase.html', 'receiver.html', 'ts-receiver.html'],
  },
  build: measureSite
    ? {
        outDir: 'dist-measure',
        rollupOptions: { input: { measure: 'measure.html' } },
      }
    : showcaseSite
      ? {
          outDir: 'dist-showcase',
          rollupOptions: {
            // Two documents: the showcase shell and the bare teleport receiver
            // (HOST 2). The receiver is deliberately a separate, visibly vacant
            // page, self-contained so it can be deployed to a second origin
            // unchanged. The CSP plugin's transformIndexHtml applies to both.
            input: {
              main: 'showcase.html',
              receiver: 'receiver.html',
              tsReceiver: 'ts-receiver.html',
            },
          },
        }
      : dualHost
        ? { rollupOptions: { input: buildInputs } }
        : {},
  server: {
    port: 24070,
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
    port: 14070,
    strictPort: true,
  },
});
