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
//  Usage — the site must already be served somewhere:
//
//    pnpm run fable:app && pnpm exec vite --port 24041      # in one terminal
//    node scripts/capture-exhibits.mjs                       # in another
//
//    node scripts/capture-exhibits.mjs --base http://localhost:24041 \
//        --out dist-showcase/captures --width 1440 --scale 2
//    node scripts/capture-exhibits.mjs --mobile               # 390x844, touch
//    node scripts/capture-exhibits.mjs --dark                 # dark scheme
//    node scripts/capture-exhibits.mjs --route catalog        # just one
//
//  PREREQUISITE, stated because it is the failure everyone hits first:
//  `@playwright/test` is a devDependency here, but its BROWSER BINARIES are a
//  separate download and are not installed by `pnpm install`. Without them this
//  script exits non-zero naming the one command that fixes it, rather than
//  producing nothing and returning success.
// =============================================================================

import { mkdirSync } from 'node:fs';
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

const base = arg('base', 'http://localhost:24041');
const outDir = join(repoRoot, arg('out', join('dist-showcase', 'captures')));
const mobile = flag('mobile');
const width = Number(arg('width', mobile ? 390 : 1440));
const height = Number(arg('height', mobile ? 844 : 1000));
const scale = Number(arg('scale', 2));
const colorScheme = flag('dark') ? 'dark' : 'light';
const only = arg('route', null);

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
  browser = await chromium.launch();
} catch (e) {
  // The browser BINARIES are a separate download from the npm package. Say so,
  // and name the command — a capture run that failed for a knowable reason must
  // not read like the site being broken.
  console.error('Could not launch Chromium. The Playwright browser binaries are a');
  console.error('separate download from the npm package:');
  console.error('');
  console.error('    pnpm exec playwright install chromium');
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
for (const [route, caption] of wanted) {
  const url = `${base}/showcase.html#/demo/${route}`;
  const suffix = `${mobile ? 'mobile' : 'desktop'}-${colorScheme}`;
  const file = join(outDir, `${route}.${suffix}.png`);
  try {
    await page.goto(url, { waitUntil: 'networkidle' });
    // The pages mount, seed their state and read back from the live DOM in
    // effects, so a paint is not the same thing as a settled page.
    await page.waitForSelector('.px-page', { timeout: 10_000 });
    await page.waitForTimeout(900);
    await page.screenshot({ path: file, fullPage: true });
    console.log(`  ${route.padEnd(16)} ${caption}`);
  } catch (e) {
    failures++;
    console.error(`  ${route.padEnd(16)} FAILED: ${String(e).split('\n')[0]}`);
  }
}

await browser.close();
console.log(
  `\n${wanted.length - failures}/${wanted.length} captured to ${outDir} ` +
    `(${width}x${height} @${scale}x, ${colorScheme}${mobile ? ', touch' : ''}).`,
);
process.exit(failures === 0 ? 0 : 1);
