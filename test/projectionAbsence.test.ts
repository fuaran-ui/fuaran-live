// Phase 1603 — an absent wire member and an unreadable one are different
// things, and the projector must say which it saw.
//
// `app/Projection.fs` read every wire member through ONE total accessor until
// this phase: it answered `JNull` for a member the wire OMITTED and `JNull` for
// one it carried as an explicit `null`, so no call site could tell the two
// apart. `Tabs.activeIndex` is what that cost. It is the binding slot the
// canonical encoder omits at its default (`Static 0`), so an unset one carries
// no wire key at all — and the absence, handed on as a value, came back out as
// an EXPLICIT `Static` that the builder's own `?? Static 0` could no longer
// fill and the encoder's `value === 0` test could no longer drop. Three corpus
// fixtures failed on one spurious key each, and it read as a package-pin lag.
//
// These are the go-red tests for that class. Each pair asserts the omitted form
// AND the spelled form, because an assertion that only ever sees one of them
// cannot distinguish "the projector omits an absent member" from "the projector
// never emits this member at all" — and the second would pass while saying
// nothing. Run them against `app/output/` (`pnpm run fable:app`).

import { describe, expect, it } from 'vitest';

// Fable-generated JS — no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { projectByName } from '../app/output/Projection.js';

// wire-format-fixtures/nodes/tabs-1 — a `Tabs` whose active index is the
// identity, so the canonical wire carries no `activeIndex` key.
const tabsAbsent =
  '{"id":"tabs-1","kind":{"$type":"Tabs","children":[{"id":"markdown-1","kind":{"$type":"Markdown","text":"Updated hourly."}}],"onSelect":"<closure>"}}';

// The same tree with the member SPELLED — the control for every assertion
// below, and the reason each pair exists rather than a single negative.
const tabsSpelled =
  '{"id":"tabs-1","kind":{"$type":"Tabs","activeIndex":{"$type":"Static","value":1},"children":[{"id":"markdown-1","kind":{"$type":"Markdown","text":"Updated hourly."}}],"onSelect":"<closure>"}}';

// The same tree again with the member PRESENT AS NULL. A canonical emission
// never contains one — WIRE_FORMAT §4 rule 4, "null does not appear anywhere in
// a canonical Fuaran emission … absence is structural, expressed by a missing
// key" — so this is neither the absence the wire spells structurally nor a value
// the model has, and the projector refuses it BY NAME rather than picking one of
// the two wrong readings.
const tabsUnreadable =
  '{"id":"tabs-1","kind":{"$type":"Tabs","activeIndex":null,"children":[{"id":"markdown-1","kind":{"$type":"Markdown","text":"Updated hourly."}}],"onSelect":"<closure>"}}';

describe('an absent optional member projects as an omission, not a null', () => {
  it('TypeScript omits an absent `activeIndex`', () => {
    const out = projectByName('typescript', tabsAbsent) as string;
    expect(out).toContain('fuaran.tabs(');
    expect(
      out,
      'an absent `activeIndex` must not appear at all — an explicit `Static` survives the re-encode',
    ).not.toContain('activeIndex');
    expect(out, 'the empty-binding spelling is what the defect emitted').not.toContain(
      'binding.static(undefined)',
    );
  });

  it('TypeScript still emits `activeIndex` when the wire spells it', () => {
    const out = projectByName('typescript', tabsSpelled) as string;
    expect(
      out,
      'the negative above would pass vacuously if the member were never emitted',
    ).toContain('activeIndex');
    expect(out).toContain('binding.static(1)');
  });

  it('Python omits an absent `activeIndex`', () => {
    const out = projectByName('python', tabsAbsent) as string;
    expect(out).toContain('fuaran.tabs(');
    expect(out).not.toContain('active_index');
  });

  it('Python still emits `activeIndex` when the wire spells it', () => {
    const out = projectByName('python', tabsSpelled) as string;
    expect(out).toContain('active_index');
  });
});

describe('a member present but unreadable is refused by name', () => {
  it('TypeScript names the member rather than reading the null as an absence', () => {
    expect(() => projectByName('typescript', tabsUnreadable)).toThrowError(/activeIndex/);
  });

  it('Python names it too — the refusal is in the accessor, not in a leg', () => {
    expect(() => projectByName('python', tabsUnreadable)).toThrowError(/activeIndex/);
  });

  it('the same tree projects cleanly once the member is absent rather than null', () => {
    // The control: it is the NULL that is refused, not the tree around it.
    expect(() => projectByName('typescript', tabsAbsent)).not.toThrow();
  });
});
