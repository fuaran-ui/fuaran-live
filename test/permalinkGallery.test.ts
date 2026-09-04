// Phase 326 – permalink (share/restore) + the gallery, over the Fable output.
// Permalink round-trips a tree through a base64 URL fragment byte-identically;
// every curated gallery example is a valid, shareable tree. Headless via vitest.
//
// Phase 286 – the gallery is the LIVE TIER of the get-started examples library
// (one cool simple app per feature area), so the certification below is what
// makes "schema-bound by construction" a checked claim rather than an assertion:
//
//   1. STRICTLY DECODABLE AND ALREADY CANONICAL. Each example's wire is decoded
//      by the real `@fuaran-ui/ops` strict decoder and must re-encode to itself
//      byte-for-byte. This is the same assertion the emitter-lock convention
//      applies to hand-authored in-page wire. These trees are built through the
//      real `Fuaran.*` constructors, so they are canon by construction and the
//      convention explicitly exempts them — but an exemption is a statement
//      about the ENCODER, and what a reader wants to know is that THIS entry, at
//      this pin, still decodes. A vocabulary the pinned decoder does not carry
//      fails here, in the repo that authored it, instead of on a visitor's
//      screen. (It has real bite: the playground is held at a deliberate
//      `Fuaran.UI` pin, so an entry authored against newer vocabulary would
//      compile perfectly and be undecodable by the very packages this app
//      ships with.)
//   2. FEATURE-TAGGED AND DISTINCT. Every entry carries a non-empty `Feature`
//      tag (the gallery groups by it and would silently drop an untagged entry
//      into a blank group) and no two entries share a title.
//   3. PROJECTABLE IN EVERY LANGUAGE. Each example projects to non-empty source
//      in each of the Output box's targets. That is the phase's "doc columns
//      from the projectors, not hand-duplicated" leg: an example is authored
//      once here and read in any host, so a projector that cannot render an
//      entry is caught with the entry rather than by a reader.

import { describe, it, expect } from 'vitest';
import { decodeNode, encodeNode } from '@fuaran-ui/ops';

// @ts-expect-error untyped Fable output
import { roundTrips } from '../app/output/Permalink.js';
// @ts-expect-error untyped Fable output
import { exampleWires, exampleTags } from '../app/output/Gallery.js';
// @ts-expect-error untyped Fable output
import { projectByName } from '../app/output/Projection.js';

const metricNode =
  '{"id":"metric-1","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"label":"Revenue","tone":"Brand","value":{"$type":"Static","value":1234.5}}}';

/** One entry's `(title, feature)` tag.
 *
 * `Gallery.fs` documents `exampleTags` as "a JS array of two-element arrays"
 * and builds each one as `[| e.Title; e.Feature |]`, so the pair has a fixed
 * arity the jagged `string[]` reading throws away. Stating the tuple is what
 * makes a known index readable under `noUncheckedIndexedAccess` — a tighter
 * type than the one it replaces, not a looser one.
 */
type GalleryTag = readonly [title: string, feature: string];

interface GalleryEntry {
  readonly title: string;
  readonly feature: string;
  readonly wire: string;
}

/** The gallery's two cross-boundary projections, paired.
 *
 * The tag list and the wire list are two projections of one `examples` list,
 * so a mismatch means one of them stopped tracking it — checked here, once,
 * rather than by reading an index off either array unchecked at each use.
 */
function galleryEntries(): GalleryEntry[] {
  const tags: GalleryTag[] = exampleTags();
  const wires: string[] = exampleWires();
  expect(wires.length, 'exampleWires and exampleTags disagree on entry count').toBe(tags.length);
  return tags.map(([title, feature], i) => {
    const wire = wires[i];
    if (wire === undefined) throw new Error(`no wire for gallery entry "${title}"`);
    return { title, feature, wire };
  });
}

describe('permalink share/restore', () => {
  it('round-trips a tree through the URL fragment byte-identically', () => {
    expect(roundTrips(metricNode)).toBe(true);
  });

  it('rejects a non-decodable fragment payload (no throw)', () => {
    expect(roundTrips('not a wire tree')).toBe(false);
  });
});

describe('the gallery', () => {
  it('offers several curated examples, each a valid + permalink-shareable tree', () => {
    const wires: string[] = exampleWires();
    expect(wires.length).toBeGreaterThanOrEqual(3);
    for (const w of wires) {
      expect(w).not.toBe('');
      expect(roundTrips(w)).toBe(true);
    }
  });

  it('covers the language vocabulary — one entry per feature area, several areas', () => {
    const features = new Set(galleryEntries().map((entry) => entry.feature));
    // The library's whole claim is breadth: a handful of demos is not a tour of
    // the language. Ten areas is the floor, not the target.
    expect(features.size).toBeGreaterThanOrEqual(10);
  });

  it('tags every entry with a feature area, and gives every entry a distinct title', () => {
    const entries = galleryEntries();
    const titles = new Set<string>();
    for (const { title, feature } of entries) {
      expect(title.trim()).not.toBe('');
      expect(feature.trim()).not.toBe('');
      expect(titles.has(title), `duplicate gallery title: ${title}`).toBe(false);
      titles.add(title);
    }
    expect(titles.size).toBe(entries.length);
  });

  it('emits wire the real strict decoder accepts, already in canonical form', () => {
    for (const { title, wire } of galleryEntries()) {
      const decoded = decodeNode(wire);
      // `ok` is the discriminant of the decoder's result union, so narrowing on
      // it is what makes `.value` readable at all — the repo's standing shape
      // for this assertion (see `test/emitterLocks.test.ts`).
      if (!decoded.ok) {
        throw new Error(
          `gallery entry "${title}" failed strict decode: ${JSON.stringify(decoded)}`,
        );
      }
      // decode -> re-encode is the identity: the entry is not merely decodable,
      // it is already the canonical form of itself.
      expect(encodeNode(decoded.value), `gallery entry "${title}" is not canonical`).toBe(wire);
    }
  });

  it('projects every entry into every language the Output box offers', () => {
    const targets = ['json', 'typescript', 'python', 'fsharp', 'csharp', 'vb'];

    for (const { title, wire } of galleryEntries()) {
      for (const target of targets) {
        const source: string = projectByName(target, wire);
        expect(source.length, `gallery entry "${title}" projected empty ${target}`).toBeGreaterThan(
          0,
        );
      }
    }
  });
});
