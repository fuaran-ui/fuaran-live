// =============================================================================
//  HOST 3's claim, locked (Phase 1589).
//
//  `ts-receiver.html` exists to demonstrate one specific thing: that a teleport
//  bundle minted by the F# tier resumes on a host with no F# on it at all. That
//  claim is not observable — it is a claim about what the page does NOT contain,
//  which is exactly the kind of property that rots silently. One `import` from
//  `app/showcase/output/` would falsify the whole page while it kept rendering
//  perfectly, and nothing would go red.
//
//  So this file holds it the way the repo holds its other page-level claims
//  (see `emitterLocks.test.ts`): source-level, in the ordinary unit suite, no
//  build step required.
//
//  Three assertions, and the third is the one that matters most:
//   1. The document loads the TypeScript entry and nothing else — in particular
//      no Fable output, which is what `receiver.html` (HOST 2) deliberately DOES
//      load. The contrast between the two documents IS the demonstration.
//   2. The entry module imports no Fable-compiled path.
//   3. The bundle the page ships as its demonstration is the shared conformance
//      corpus's own teleport fixture, and it really is an F#-produced bundle:
//      decoded here through the same decoder the page uses, its integrity digest
//      verifies and its tree decodes. A page that claimed a foreign host while
//      shipping a bundle nobody had verified would be a nicer-looking lie.
// =============================================================================

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { decodeTeleport } from '@fuaran-ui/op-stream';
import { describe, expect, it } from 'vitest';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, '..');
const read = (rel: string): string => readFileSync(join(repoRoot, rel), 'utf8');

const documentHtml = read('ts-receiver.html');
const entrySource = read('app/showcase/tsReceiver.tsx');

/** The corpus fixture the page inlines at build time. Resolved the way the page
 *  resolves it — as a sibling checkout — so a layout drift is caught here too. */
const fixturePath = join(
  repoRoot,
  '..',
  'wire-format-fixtures',
  'teleport',
  'tp-wizard-step1.json',
);

describe('ts-receiver.html — the host with no F# on it', () => {
  it('loads the TypeScript entry and no Fable output', () => {
    const scripts = [...documentHtml.matchAll(/<script[^>]*\ssrc="([^"]+)"/g)].map((m) => m[1]);
    expect(scripts).toEqual(['/app/showcase/tsReceiver.tsx']);

    // The negative half, stated separately so a failure says WHICH way it broke.
    // Measured over the MARKUP, not the comments: the document's comments
    // deliberately name receiver.html's Fable bundle, because saying what this
    // page is NOT is the explanation. A comment loads nothing.
    const markup = documentHtml.replace(/<!--[\s\S]*?-->/g, '');
    expect(markup).not.toContain('/output/');
    expect(markup.toLowerCase()).not.toContain('fable');
  });

  it('is the mirror image of receiver.html, which DOES load Fable output', () => {
    // Not decoration: the two documents are the demonstration, and this pins
    // that they still differ in the one way the page claims they do. If HOST 2
    // ever stops loading Fable output, this page's contrast is gone and the
    // showcase prose above it is wrong.
    expect(read('receiver.html')).toContain('/app/showcase/output/Receiver.js');
  });

  it('imports nothing Fable-compiled', () => {
    // `m[1]` is `string | undefined` under `noUncheckedIndexedAccess`, and the
    // assertion is sound rather than convenient: both patterns carry exactly one
    // capture group, so a match always has it.
    const imports = [...entrySource.matchAll(/from\s+'([^']+)'/g)].map((m) => m[1]!);
    const bare = [...entrySource.matchAll(/^import\s+'([^']+)';/gm)].map((m) => m[1]!);
    for (const spec of [...imports, ...bare]) {
      expect(spec, `${spec} reaches Fable-compiled output`).not.toMatch(/(^|\/)output\//);
      expect(spec.toLowerCase(), `${spec} names Fable`).not.toContain('fable');
    }
  });

  it('ships the corpus fixture as its demonstration bundle, and it verifies', async () => {
    // Skips only when the sibling corpus checkout is absent (a standalone clone);
    // CI asserts the corpus is present, so this runs there.
    if (!existsSync(fixturePath)) return;

    const declared = entrySource.match(
      /import referenceFixtureRaw from '([^']+)\?raw'/,
    ) as RegExpMatchArray | null;
    expect(declared, 'the page no longer inlines a corpus fixture').not.toBeNull();
    expect(declared?.[1]).toBe('../../../wire-format-fixtures/teleport/tp-wizard-step1.json');

    const { encoded } = JSON.parse(readFileSync(fixturePath, 'utf8')) as { encoded: string };
    expect(encoded.startsWith('FT1.')).toBe(true);

    const decoded = await decodeTeleport(encoded);
    expect(decoded.ok, 'the demonstration bundle does not decode').toBe(true);
    if (!decoded.ok) return;
    // The digest verified (decodeTeleport refuses otherwise), the tree is real,
    // and the app is mid-interaction rather than freshly started — which is the
    // thing a visitor is being shown.
    expect(decoded.value.digest).toHaveLength(64);
    expect(decoded.value.tree).toHaveProperty('kind');
    expect(Object.keys(decoded.value.state).length).toBeGreaterThan(0);
  });
});
