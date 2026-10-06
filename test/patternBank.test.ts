// Phase 2081 – the pattern bank is ONE shared file (app/shared/PatternBank.fs),
// generic over the message type, compiled by the playground and linked by the
// showcase. Before that the two entries each carried their own copy of the
// engine and of a seed catalogue, and the two catalogues were NOT the same: they
// share their twelve ids but differ in titles, summaries, holes and trees (the
// playground's builders ignore hole values; the showcase's honour them). Which
// one catalogue the site should offer is a product decision, not a refactoring
// one, so the merged file keeps both.
//
// This lock pins both catalogues to the bytes each entry shipped before the
// merge. `patternBankSeeds.golden.json` was captured from the two pre-merge
// files (each compiled with a one-off dump of its private seed list, encoded
// through the canonical encoder) and is not regenerated from the merged code:
// a change to a seed is a deliberate act that edits the golden beside it.
//
// Requires `pnpm run fable:app` to have produced app/output/.

import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import {
  playgroundSeedsFlat,
  showcaseSeedsFlat,
  // @ts-expect-error untyped Fable output (no .d.ts for app/output/testing/PatternBankProbes.js)
} from '../app/output/testing/PatternBankProbes.js';

interface SeedRow {
  id: string;
  title: string;
  summary: string;
  resultType: string;
  holes: string;
  tree: string;
  treeFilled: string;
}

const golden = JSON.parse(
  readFileSync(new URL('./patternBankSeeds.golden.json', import.meta.url), 'utf8'),
) as { playground: SeedRow[]; showcase: SeedRow[] };

const rows = (flat: string[]): SeedRow[] =>
  Array.from(flat).map((r) => {
    const [
      id = '',
      title = '',
      summary = '',
      resultType = '',
      holes = '',
      tree = '',
      treeFilled = '',
    ] = r.split('\t');
    return { id, title, summary, resultType, holes, tree, treeFilled };
  });

describe('the merged pattern bank holds both catalogues byte-for-byte', () => {
  it('the golden is the real thing: twelve patterns each, sharing their ids', () => {
    // The positive control: an empty or truncated golden would pass the
    // equalities below vacuously.
    expect(golden.playground).toHaveLength(12);
    expect(golden.showcase).toHaveLength(12);
    expect(golden.playground.map((r) => r.id)).toEqual(golden.showcase.map((r) => r.id));
    // ...and the two catalogues genuinely differ, which is why both are kept.
    expect(golden.playground).not.toEqual(golden.showcase);
  });

  it("the playground's catalogue equals what app/PatternBank.fs held", () => {
    expect(rows(playgroundSeedsFlat())).toEqual(golden.playground);
  });

  it("the showcase's catalogue equals what app/showcase/PatternBank.fs held", () => {
    expect(rows(showcaseSeedsFlat())).toEqual(golden.showcase);
  });

  it('the seed ids are the union of the two files', () => {
    const merged = new Set([
      ...rows(playgroundSeedsFlat()).map((r) => r.id),
      ...rows(showcaseSeedsFlat()).map((r) => r.id),
    ]);
    const before = new Set([
      ...golden.playground.map((r) => r.id),
      ...golden.showcase.map((r) => r.id),
    ]);
    expect(merged).toEqual(before);
  });
});
