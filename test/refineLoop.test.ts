// Phase 715 – "refine from here": the playground's generative loop closed.
// Emit → walk and edit in the navigator → re-prompt with the EDITED tree as the
// emission context. The modules are app/Session.fs (the context assembly) and
// app/navigator/Refine.fs (the baseline, the comparison, the loop stage); this
// drives them headlessly in node via vitest over the Fable output, following the
// Phase 710–712 pattern — flat projections across the Fable boundary, every
// fixture fed through the REAL strict decoder via the session's own ingest path.
//
// The suite's claims are the phase's acceptance criteria, minus the one no test
// can honestly make:
//
//   1. The context payload demonstrably contains the EDITED tree and not the
//      original emission. Asserted both ways round — the edited text present AND
//      the superseded text absent — because a payload that carried both would
//      pass a presence-only check while leaving the model free to revert.
//   2. The human's ops since the last emission are summarised as constraints,
//      the trail is cut at the last model emission, and the summary is capped
//      with the OLDEST lines elided and the elision stated.
//   3. A re-emission is diffed against the captured baseline, so "did it keep my
//      edits?" is answered by comparison. Both outcomes are exercised: a
//      re-emission that respects the edit and one that overwrites it — a loop
//      that could only ever report "kept" would be decoration.
//
// The one criterion NOT asserted here is that a LIVE model honours the context.
// That is a claim about a provider, not about this code; the emissions below are
// scripted, so what is proven is that the loop hands the model the right thing
// and reports truthfully on what came back.
//
// Requires `pnpm run fable:app` (or `dotnet fable app --outDir app/output`).

import { describe, it, expect } from 'vitest';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
import {
  empty,
  ingestResult,
  refinePrompt,
  refineSystemSuffix,
  correctionLineArray,
  humanOpCount,
  correctionBudget,
  lastMessageContent,
  allMessageContents,
  withTurn,
  // @ts-expect-error untyped Fable output
} from '../app/output/Session.js';
// @ts-expect-error untyped Fable output
import { commitAt } from '../app/output/navigator/PropertyEditor.js';
// @ts-expect-error untyped Fable output
import { ProviderRole } from '../app/output/Ports.js';
import {
  baselineOf,
  changeLines,
  retainedIds,
  overwrittenIds,
  stage,
  stageLabel,
  // @ts-expect-error untyped Fable output
} from '../app/output/navigator/Refine.js';

// ─── fixtures ────────────────────────────────────────────────────────────────

const EMITTED_HEADING = 'Quarterly review';
const HUMAN_HEADING = 'Q3 revenue, actuals only';

/** The model's first emission — the baseline the human is about to correct. */
const tree = (heading: string, badge: string) =>
  '{"id":"root","kind":{"$type":"Box","children":[' +
  '{"id":"h","kind":{"$type":"Heading","level":2,"text":"' +
  heading +
  '","variant":"Standard"}},' +
  '{"id":"b","kind":{"$type":"Badge","label":"' +
  badge +
  '","variant":"Brand"}}' +
  '],"layout":{"$type":"Auto"},"role":"Dashboard"}}';

const emitted = tree(EMITTED_HEADING, 'New');

/** A model emission carrying one TreeOp — the canonical corpus shape. */
const modelOp = (nodeId: string, path: string, value: string) =>
  '{"$type":"UpdateProp","path":"' + path + '","target":"' + nodeId + '","value":"' + value + '"}';

function ingest(s: any, wire: string) {
  const r = ingestResult(s, wire);
  if (!r.Ok) {
    throw new Error(`fixture did not ingest: ${r.Error}\n${wire}`);
  }
  return r.Next;
}

function edit(s: any, nodeId: string, path: string, raw: string) {
  const r = commitAt(s, nodeId, path, raw);
  if (!r.Ok) {
    throw new Error(`commit refused: ${r.Error}`);
  }
  return r.Next;
}

/** Emitted, then corrected by hand in the navigator. */
function editedSession() {
  return edit(ingest(empty, emitted), 'h', 'Text', HUMAN_HEADING);
}

// ─── 1. the context payload carries the EDITED tree ──────────────────────────

describe('"refine from here" sends the human-edited tree as the emission context', () => {
  it('carries the edited text and NOT the emission it replaced', () => {
    const s = editedSession();
    const payload = refinePrompt(s, 'add a second metric');

    expect(payload).toContain(HUMAN_HEADING);
    // The whole point: the model's own last wording is absent from the context,
    // so there is nothing for the next emission to revert to.
    expect(payload).not.toContain(EMITTED_HEADING);
    expect(payload).toContain('add a second metric');
  });

  it('reuses the closed loop’s own injection seam, not a parallel one', () => {
    const s = editedSession();
    // `refinePrompt` is `lastMessageContent` against a history-free session —
    // asserted by equality rather than by reading the source, so a future
    // divergence between the two paths fails here instead of drifting quietly.
    const viaSeam = lastMessageContent({ ...s, History: empty.History }, 'add a second metric');
    expect(refinePrompt(s, 'add a second metric')).toBe(viaSeam);
  });

  it('is self-contained — the transcript that names the old version is NOT along', () => {
    // The positive control for the assertion above. Without a transcript, "the
    // superseded text is absent" is true of any payload and proves nothing; so
    // this session CARRIES the model's original wording in its conversation,
    // and the two paths are compared on the same session.
    let s = editedSession();
    s = withTurn(s, { Role: new ProviderRole(1, []), Content: `I built "${EMITTED_HEADING}".` });

    const ordinary = Array.from(allMessageContents(s, 'add a second metric')) as string[];
    // The ordinary turn resumes the conversation, so the stale wording is in it…
    expect(ordinary.length).toBeGreaterThan(1);
    expect(ordinary.join('\n')).toContain(EMITTED_HEADING);

    // …and the refine payload, which replaces that baseline, is free of it.
    expect(refinePrompt(s, 'add a second metric')).not.toContain(EMITTED_HEADING);
    expect(refinePrompt(s, 'add a second metric')).toContain(HUMAN_HEADING);
  });

  it('reflects further edits — the context is the tree NOW, not at first edit', () => {
    let s = editedSession();
    s = edit(s, 'b', 'Label', 'Draft');
    const payload = refinePrompt(s, 'tighten it');

    expect(payload).toContain(HUMAN_HEADING);
    expect(payload).toContain('Draft');
    expect(payload).not.toContain('"New"');
  });
});

// ─── 2. the human-correction summary ─────────────────────────────────────────

describe('the human’s corrections ride the system prompt as constraints', () => {
  it('names the path, the node and the new value', () => {
    const s = editedSession();
    const lines = correctionLineArray(s) as string[];

    expect(lines).toHaveLength(1);
    expect(lines[0]).toContain('Text');
    expect(lines[0]).toContain('#h');
    expect(lines[0]).toContain(HUMAN_HEADING);
  });

  it('frames them as decided, not as suggestions', () => {
    const suffix = refineSystemSuffix(editedSession()) as string;

    expect(suffix).toContain('edited this UI by hand');
    expect(suffix).toContain('Treat those choices as decided');
    expect(suffix).toContain(HUMAN_HEADING);
  });

  it('is empty when the human has changed nothing since the emission', () => {
    const s = ingest(empty, emitted);

    expect(humanOpCount(s)).toBe(0);
    // An empty "the human changed:" heading would be a claim the model would
    // try to honour, so the block is absent rather than empty.
    expect(refineSystemSuffix(s)).toBe('');
  });

  it('counts only the ops SINCE the last model emission', () => {
    let s = ingest(empty, emitted);
    s = edit(s, 'h', 'Text', 'a first pass'); // human, then superseded
    s = ingest(s, modelOp('b', 'Label', 'Revised')); // the model emits again
    s = edit(s, 'h', 'Text', HUMAN_HEADING); // human, since that emission

    expect(humanOpCount(s)).toBe(1);
    const lines = correctionLineArray(s) as string[];
    expect(lines).toHaveLength(1);
    expect(lines[0]).toContain(HUMAN_HEADING);
    expect(lines[0]).not.toContain('a first pass');
  });

  it('excludes the model’s own ops from the human trail', () => {
    let s = ingest(empty, emitted);
    s = edit(s, 'h', 'Text', HUMAN_HEADING);
    s = edit(s, 'b', 'Label', 'Draft');

    expect(humanOpCount(s)).toBe(2);
    const lines = correctionLineArray(s) as string[];
    expect(lines.join(' ')).not.toContain('Revised');
  });
});

// ─── the budget + truncation rule ────────────────────────────────────────────

describe('the correction block is capped, and says what it dropped', () => {
  // Enough edits that the budget must bite, each with a distinguishable value.
  const many = () => {
    let s = ingest(empty, emitted);
    for (let i = 0; i < 60; i++) {
      s = edit(s, 'h', 'Text', `revision number ${i} of the heading text`);
    }
    return s;
  };

  it('keeps the block inside its character budget', () => {
    const lines = correctionLineArray(many()) as string[];
    const body = lines.filter((l) => !l.startsWith('(')).join('\n');

    expect(humanOpCount(many())).toBe(60);
    expect(lines.length).toBeLessThan(60);
    expect(body.length).toBeLessThanOrEqual(correctionBudget);
  });

  it('drops the OLDEST lines and states the elision', () => {
    const lines = correctionLineArray(many()) as string[];

    // The marker leads, so the model reads "there was more" before it reads the
    // list — an elision discovered afterwards is one already acted on.
    expect(lines[0]).toMatch(/^\(\d+ earlier edit\(s\) omitted/);
    expect(lines[0]).toContain('already reflected in the tree');
    // The newest edit survives: later edits supersede earlier ones, so the tail
    // is the part worth spending the budget on.
    expect(lines[lines.length - 1]).toContain('revision number 59');
    expect(lines.join(' ')).not.toContain('revision number 0 ');
  });

  it('truncates an over-long value rather than the whole block', () => {
    let s = ingest(empty, emitted);
    s = edit(s, 'h', 'Text', 'x'.repeat(500));
    const lines = correctionLineArray(s) as string[];

    expect(lines).toHaveLength(1);
    const [line] = lines;
    if (line === undefined) {
      throw new Error('expected one correction line');
    }
    expect(line.length).toBeLessThan(200);
    expect(line).toContain('…');
  });
});

// ─── 3. the re-emission, diffed against the edited baseline ──────────────────

describe('a re-emission is compared with the version the human approved', () => {
  it('captures the baseline from the tree that was actually sent', () => {
    const s = editedSession();
    const baseline = baselineOf(s, 'add a second metric');

    expect(baseline).toBeTruthy();
    expect(baseline.TreeJson).toContain(HUMAN_HEADING);
    expect(baseline.Prompt).toBe('add a second metric');
    // The edited node is the one whose survival is under test.
    expect(Array.from(baseline.EditedIds)).toContain('h');
  });

  it('offers no baseline before there is a tree', () => {
    expect(baselineOf(empty, 'anything')).toBeFalsy();
  });

  it('reports an edit the re-emission RESPECTED', () => {
    const s = editedSession();
    const baseline = baselineOf(s, 'change the badge');

    // The model returns a full tree (ReplaceRoot semantics in the session): the
    // human's heading survives, the badge is what changed.
    const next = ingest(s, tree(HUMAN_HEADING, 'Updated'));

    expect(Array.from(retainedIds(next, baseline))).toContain('h');
    expect(Array.from(overwrittenIds(next, baseline))).toHaveLength(0);

    const changes = Array.from(changeLines(next, baseline)) as string[];
    expect(changes.join(' ')).toContain('#b');
    expect(changes.join(' ')).not.toContain('#h');
  });

  it('reports an edit the re-emission OVERWROTE — the honest half', () => {
    const s = editedSession();
    const baseline = baselineOf(s, 'change the badge');

    // The model ignored the context and restored its own heading.
    const next = ingest(s, tree(EMITTED_HEADING, 'Updated'));

    expect(Array.from(overwrittenIds(next, baseline))).toContain('h');
    expect(Array.from(retainedIds(next, baseline))).toHaveLength(0);

    const changes = Array.from(changeLines(next, baseline)) as string[];
    // The shipped TreeDiff narrows a Heading/Markdown literal change to
    // TextChanged, so the readout shows the actual character delta.
    expect(changes.join(' ')).toContain(HUMAN_HEADING);
    expect(changes.join(' ')).toContain(EMITTED_HEADING);
  });

  it('reports no changes when the re-emission is identical', () => {
    const s = editedSession();
    const baseline = baselineOf(s, 'leave it alone');
    const next = ingest(s, tree(HUMAN_HEADING, 'New'));

    expect(Array.from(changeLines(next, baseline))).toHaveLength(0);
    expect(Array.from(retainedIds(next, baseline))).toContain('h');
  });

  it('re-resolves across a re-emission that adds and removes nodes', () => {
    const s = editedSession();
    const baseline = baselineOf(s, 'swap the badge for a metric');

    const next = ingest(
      s,
      '{"id":"root","kind":{"$type":"Box","children":[' +
        '{"id":"h","kind":{"$type":"Heading","level":2,"text":"' +
        HUMAN_HEADING +
        '","variant":"Standard"}},' +
        '{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":42.0}}}' +
        '],"layout":{"$type":"Auto"},"role":"Dashboard"}}',
    );

    const changes = Array.from(changeLines(next, baseline)) as string[];
    expect(changes.join(' ')).toContain('added #m');
    expect(changes.join(' ')).toContain('removed #b');
    // The human's node is untouched by an otherwise structural rewrite.
    expect(Array.from(retainedIds(next, baseline))).toContain('h');
  });
});

// ─── 4. the loop affordance ──────────────────────────────────────────────────

describe('the cycle is legible without leaving the playground', () => {
  it('reads emitted → edited (n) → re-prompted → re-emitted', () => {
    expect(stageLabel(stage(empty, undefined, false))).toContain('nothing emitted yet');

    const fresh = ingest(empty, emitted);
    expect(stageLabel(stage(fresh, undefined, false))).toContain('emitted');

    const one = editedSession();
    expect(stageLabel(stage(one, undefined, false))).toContain('edited (1 of your ops)');

    const two = edit(one, 'b', 'Label', 'Draft');
    expect(stageLabel(stage(two, undefined, false))).toContain('edited (2 of your ops)');

    const baseline = baselineOf(two, 'go');
    expect(stageLabel(stage(two, baseline, true))).toContain('re-prompted');

    const next = ingest(two, tree(HUMAN_HEADING, 'Updated'));
    expect(stageLabel(stage(next, baseline, false))).toContain('re-emitted');
  });
});
