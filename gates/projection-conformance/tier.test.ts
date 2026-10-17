// The AHEAD grade's comparison (WIRE_FORMAT.md 11.3): a fixture whose `since` post-dates the
// pinned tier is AHEAD; one at or below it is not, and an unreadable version grades nothing AHEAD.
import { describe, expect, it } from 'vitest';

import { isAhead, nodeFixtures, pinnedTierVersion } from './tier';

describe('the AHEAD grade', () => {
  it('a fixture specified after the tier is AHEAD', () => {
    expect(isAhead('0.95.0', '0.94.0')).toBe(true);
    expect(isAhead('1.0.0', '0.99.9')).toBe(true);
    expect(isAhead('0.94.1', '0.94.0')).toBe(true);
    expect(isAhead('0.10.0', '0.9.0')).toBe(true); // numeric, not lexical
  });
  it('a fixture at or below the tier is not', () => {
    expect(isAhead('0.94.0', '0.94.0')).toBe(false);
    expect(isAhead('0.3.0', '0.94.0')).toBe(false);
  });
  it('an absent or unreadable version grades nothing AHEAD', () => {
    expect(isAhead(undefined, '0.94.0')).toBe(false);
    expect(isAhead('next', '0.94.0')).toBe(false);
    expect(isAhead('0.95.0', 'main')).toBe(false);
  });
  it('the split is a partition of the corpus node fixtures at the pinned tier', () => {
    const { certified, ahead } = nodeFixtures;
    expect(certified.length).toBeGreaterThan(0);
    for (const f of ahead) expect(isAhead(f.since, pinnedTierVersion)).toBe(true);
    for (const f of certified) expect(isAhead(f.since, pinnedTierVersion)).toBe(false);
  });
});
