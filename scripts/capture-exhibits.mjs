// =============================================================================
//  Promotion capture — one full-page PNG per showcase exhibit (Phase 1129).
//
//  WHY A SCRIPT AND NOT COMMITTED IMAGES. A screenshot committed to the repo is
//  wrong the first time anyone edits the page it depicts, and nothing tells you
//  it has gone wrong — it just quietly starts advertising a version of the site
//  that no longer exists. A capture RUN is reproducible, so the images are
//  regenerated from whatever the site currently is, which is the only kind of
//  promotion asset that cannot be stale.
//
//  Output goes to `dist-showcase/captures/`, beside the built artifact and
//  already ignored by git: a capture is a build product, not a source.
//
//  DECIDED, Phase 1702, because the question had two answers on record. The
//  residue that asked for these PNGs said "commit the images where the script
//  says"; the paragraph above says a capture is a build product. Build product
//  wins, and the reason it wins is the paragraph above — an image in git goes
//  stale silently, which is the one failure mode a promotion asset must not
//  have. What the "commit them" ask was really after is REVIEWABILITY: someone
//  approving a promotion needs to know what was captured and that it matched
//  the page. `--manifest` serves that, and serves it better than the images
//  would, because a manifest is text: it names the commit, the viewport, the
//  browser build, and a sha256 per file, so a reviewer can check a capture they
//  were handed against the run that produced it, and a diff of two manifests
//  says which exhibits actually changed. See `docs/promotion-captures.md`.
//
//  Usage — the site must already be served somewhere:
//
//    pnpm run fable:app && pnpm exec vite --port 24071      # in one terminal
//    node scripts/capture-exhibits.mjs                       # in another
//
//    node scripts/capture-exhibits.mjs --base http://localhost:24071 \
//        --out dist-showcase/captures --width 1440 --scale 2
//    node scripts/capture-exhibits.mjs --mobile               # 390x844, touch
//    node scripts/capture-exhibits.mjs --dark                 # dark scheme
//    node scripts/capture-exhibits.mjs --route catalog        # just one
//    node scripts/capture-exhibits.mjs --manifest captures.json  # + a manifest
//
//  PREREQUISITE, stated because it is the failure everyone hits first:
//  `@playwright/test` is a devDependency here, but its BROWSER BINARIES are a
//  separate download and are not installed by `pnpm install`. Without them this
//  script exits non-zero naming the one command that fixes it, rather than
//  producing nothing and returning success.
//
//  OR DRIVE A BROWSER THE MACHINE ALREADY HAS. Playwright can attach to an
//  installed Chromium-family browser by CHANNEL instead of downloading its own:
//
//    PLAYWRIGHT_CHANNEL=msedge node scripts/capture-exhibits.mjs
//    node scripts/capture-exhibits.mjs --channel chrome
//
//  Usual channels: `chrome`, `chrome-beta`, `msedge`, `msedge-beta`,
//  `msedge-dev`. The DEFAULT IS UNCHANGED — Playwright's own bundled Chromium —
//  and deliberately so: that build is pinned by the lockfile, so two people
//  capturing on two machines get the same renderer. A channel trades that
//  pinning for being able to run at all, which is the right trade on a machine
//  that cannot download the binaries and the wrong one on a machine that can.
//  Which browser produced a capture is therefore recorded in the manifest
//  rather than left to be inferred from who ran it.
// =============================================================================

import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The routes this phase's exhibits live at, with the file name each captures to. */
const exhibits = [
  ['briefing', 'the media node that carries its own captions and transcript'],
  ['embedded', 'the sandboxed third-party frame and its permission ladder'],
  ['situation-room', 'a dense board whose affordances carry hints'],
  ['intake', 'combobox, tokens, rating and colour in one ordinary form'],
  ['bidi', 'a declared direction on one opaque identifier'],
  ['invoice', 'the four print-break declarations, and the Print action'],
  ['roster', 'rows moving between grids, and rows the reader may take'],
  ['catalog', 'a carousel from one number on the wire'],
  ['outline', 'a hierarchy walked with one focus'],
  ['handover', 'a clipboard payload that resolves when you press it'],
  ['attach', 'four upload gestures, and a destination honestly refused'],
];

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--')
    ? process.argv[i + 1]
    : fallback;
}
const flag = (name) => process.argv.includes(`--${name}`);

const base = arg('base', 'http://localhost:24071');
const outDir = join(repoRoot, arg('out', join('dist-showcase', 'captures')));
const mobile = flag('mobile');
const width = Number(arg('width', mobile ? 390 : 1440));
const height = Number(arg('height', mobile ? 844 : 1000));
const scale = Number(arg('scale', 2));
const colorScheme = flag('dark') ? 'dark' : 'light';
const only = arg('route', null);
// Empty string means "Playwright's own bundled Chromium", which is what
// `launch()` does with no `channel` at all -- so the default path is unchanged
// and a machine with the binaries installed notices nothing.
const channel = arg('channel', process.env.PLAYWRIGHT_CHANNEL ?? '');
const manifestPath = arg('manifest', null);

const wanted = only ? exhibits.filter(([r]) => r === only) : exhibits;
if (wanted.length === 0) {
  console.error(
    `No exhibit matches --route ${only}. Known: ${exhibits.map(([r]) => r).join(', ')}`,
  );
  process.exit(2);
}

let chromium;
try {
  ({ chromium } = await import('@playwright/test'));
} catch {
  console.error('@playwright/test is not installed. Run `pnpm install` first.');
  process.exit(2);
}

let browser;
try {
  browser = await chromium.launch(channel ? { channel } : {});
} catch (e) {
  // The browser BINARIES are a separate download from the npm package. Say so,
  // and name the command — a capture run that failed for a knowable reason must
  // not read like the site being broken.
  if (channel) {
    console.error(`Could not launch the "${channel}" browser channel. That is`);
    console.error('Playwright driving a browser installed on this machine, so the fix is');
    console.error('to install that browser (or pass a different --channel) — not to');
    console.error('install anything from npm.');
  } else {
    console.error('Could not launch Chromium. The Playwright browser binaries are a');
    console.error('separate download from the npm package:');
    console.error('');
    console.error('    pnpm exec playwright install chromium');
    console.error('');
    console.error('On a machine that cannot download them, drive a browser it already');
    console.error('has instead:');
    console.error('');
    console.error('    PLAYWRIGHT_CHANNEL=msedge node scripts/capture-exhibits.mjs');
  }
  console.error('');
  console.error(String(e).split('\n')[0]);
  process.exit(2);
}

mkdirSync(outDir, { recursive: true });

const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: scale,
  colorScheme,
  hasTouch: mobile,
  isMobile: mobile,
});
const page = await context.newPage();

let failures = 0;
/** One record per captured file, for `--manifest`. */
const captured = [];
for (const [route, caption] of wanted) {
  const url = `${base}/showcase.html#/demo/${route}`;
  const suffix = `${mobile ? 'mobile' : 'desktop'}-${colorScheme}`;
  const name = `${route}.${suffix}.png`;
  const file = join(outDir, name);
  try {
    await page.goto(url, { waitUntil: 'networkidle' });
    // The pages mount, seed their state and read back from the live DOM in
    // effects, so a paint is not the same thing as a settled page.
    await page.waitForSelector('.px-page', { timeout: 10_000 });
    await page.waitForTimeout(900);
    await page.screenshot({ path: file, fullPage: true });
    // The full-page height is a property of the capture, not of the viewport,
    // and it is the number that moves first when a page gains or loses
    // content — so it is worth a reviewer's eye beside the digest.
    const pageHeight = await page.evaluate(() => document.documentElement.scrollHeight);
    const bytes = readFileSync(file);
    captured.push({
      route,
      caption,
      file: name,
      bytes: bytes.length,
      pageHeight,
      sha256: createHash('sha256').update(bytes).digest('hex'),
    });
    console.log(`  ${route.padEnd(16)} ${caption}`);
  } catch (e) {
    failures++;
    console.error(`  ${route.padEnd(16)} FAILED: ${String(e).split('\n')[0]}`);
  }
}

const browserVersion = browser.version();
await browser.close();

if (manifestPath) {
  // A capture is a build product (see the header), so the REVIEWABLE artefact
  // is this manifest rather than the images: it is text, it diffs, and it says
  // which run produced which bytes. It deliberately records the inputs a
  // capture depends on and not a timestamp — a manifest that differed on every
  // run would tell a reviewer nothing about whether the SITE changed.
  const manifest = {
    kind: 'fuaran-live/promotion-captures',
    version: 1,
    base,
    viewport: { width, height, deviceScaleFactor: scale },
    colorScheme,
    mobile,
    browser: { channel: channel || 'playwright-chromium', version: browserVersion },
    captured: captured.length,
    requested: wanted.length,
    exhibits: captured,
  };
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  console.log(`  manifest        ${manifestPath}`);
}

console.log(
  `\n${wanted.length - failures}/${wanted.length} captured to ${outDir} ` +
    `(${width}x${height} @${scale}x, ${colorScheme}${mobile ? ', touch' : ''}` +
    `${channel ? `, ${channel}` : ''}).`,
);
process.exit(failures === 0 ? 0 : 1);
