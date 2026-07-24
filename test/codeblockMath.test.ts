// Phase 294 – CodeBlock + Math adoption, over the Fable output.
//
// The developer-content funnel: the system prompt teaches the two primitives, the
// gallery docs/tutorial headline carries them as first-class nodes (not a Markdown
// fence / `$…$`), and the closed loop accepts such an emission. Headless via vitest.

import { describe, it, expect } from 'vitest';

// @ts-expect-error untyped Fable output
import { value as systemPrompt } from '../app/output/SystemPrompt.js';
// @ts-expect-error untyped Fable output
import { exampleWires } from '../app/output/Gallery.js';
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';
// @ts-expect-error untyped Fable output
import { roundTrips } from '../app/output/Permalink.js';

/** The canonical wire of the gallery docs/tutorial page (root id `ex-docs`). */
const docsWire = (): string => {
  const wires: string[] = exampleWires();
  return wires.find((w) => w.includes('ex-docs')) ?? '';
};

describe('Phase 294 – CodeBlock + Math adoption', () => {
  it('the system prompt teaches the CodeBlock + Math primitives', () => {
    expect(systemPrompt).toContain('CodeBlock');
    expect(systemPrompt).toContain('Math');
    expect(systemPrompt).toContain('language');
    expect(systemPrompt).toContain('LaTeX');
  });

  it('the gallery docs headline carries first-class CodeBlock + Math + a GFM table, and permalinks', () => {
    const w = docsWire();
    expect(w).not.toBe('');
    // First-class primitives, not a Markdown fence / `$…$`.
    expect(w).toContain('"$type":"CodeBlock"');
    expect(w).toContain('"$type":"Math"');
    // A GFM table (routes to the real `fuaran-table` render).
    expect(w).toContain('| Kind | Use |');
    // The headline renders + shares.
    expect(roundTrips(w)).toBe(true);
  });

  it('the closed loop accepts an emission carrying CodeBlock + Math (a real tree)', () => {
    const r = ingestResult(empty, docsWire());
    expect(r.Ok).toBe(true);
    expect(r.Mode).toBe('tree');
  });
});
