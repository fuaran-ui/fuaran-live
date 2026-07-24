// Phase 326 – permalink (share/restore) + the gallery, over the Fable output.
// Permalink round-trips a tree through a base64 URL fragment byte-identically;
// every curated gallery example is a valid, shareable tree. Headless via vitest.

import { describe, it, expect } from 'vitest';

// @ts-expect-error untyped Fable output
import { roundTrips } from '../app/output/Permalink.js';
// @ts-expect-error untyped Fable output
import { exampleWires } from '../app/output/Gallery.js';

const metricNode =
  '{"id":"metric-1","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"label":"Revenue","tone":"Brand","value":{"$type":"Static","value":1234.5}}}';

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
});
