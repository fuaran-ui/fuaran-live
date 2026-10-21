// Phase 2230 – the two sentences the playground says for cases its matches once
// left incomplete: a tree value space in the ask table, and a position a node
// may not leave or cross in the navigator's status line. Driven over the Fable
// output, so a regression to an incomplete match fails here as well as at the
// compiler (FS0025 is an error in this repository's F# projects).
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

// @ts-expect-error untyped Fable output (no .d.ts for app/output/testing/AskProbes.js)
import { slotTreeSpaceText } from '../app/output/testing/AskProbes.js';
// @ts-expect-error untyped Fable output (no .d.ts for app/output/testing/StructuralEditProbes.js)
import { positionNotStructuralText } from '../app/output/testing/StructuralEditProbes.js';

describe('Ask – a tree value space has a sentence', () => {
  it('names the kind a subtree must be', () => {
    expect(slotTreeSpaceText('Text')).toBe('a Text subtree');
  });

  it('says a plain subtree when no kind is constrained', () => {
    expect(slotTreeSpaceText('')).toBe('a subtree');
  });
});

describe('StructuralEdit – a non-structural position has a sentence', () => {
  it('names the node and the position it cannot leave or cross', () => {
    expect(positionNotStructuralText('n1', 'header')).toBe(
      "'n1' cannot leave or cross the 'header' position",
    );
  });
});
