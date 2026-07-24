// Phase 465 §18 in the live loop – the askUser elicitation tool, exercised
// headlessly over the Fable output.
//
// The loop's ask seam is `AskUser: envelope wire -> Async<outcome wire>`; the
// probes inject a SCRIPTED answerer playing the human – the same injection
// point an evaluation harness uses. Asserted here: a valid ask suspends the
// loop and threads the typed outcome back as the tool result the model reads;
// a malformed envelope is refused with the typed decode error (and no ask row
// ever mounts); and the answer builder classifies committed state per §18.2
// (whole number → integer, empty string → omitted) through the real gate.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';

import {
  runAskProbe,
  runAskRefusedProbe,
  // @ts-expect-error untyped Fable output (no .d.ts for app/output/Agent.js)
} from '../app/output/Agent.js';
// @ts-expect-error untyped Fable output (no .d.ts for app/output/Ask.js)
import { buildAnswerJsonFlat } from '../app/output/Ask.js';

describe('askUser – the ask-and-answer round trip (scripted human)', () => {
  it('threads an Answered outcome back as the tool result the model reads', async () => {
    const r = await runAskProbe('answered');
    expect(r.HaltReason).toBe('completed');
    expect(r.Iterations).toBe(2);
    // The ask row mounted in the transcript, then the tool call resolved.
    expect(r.TimelineKinds).toContain('ask');
    expect(r.AskedElicitationId).toBe('probe-ask-1');
    // The model's second turn received the canonical outcome wire.
    const outcome = JSON.parse(r.ThreadedOutcome);
    expect(outcome.$type).toBe('Answered');
    expect(outcome.elicitationId).toBe('probe-ask-1');
    expect(outcome.answer).toEqual({ flavour: 'detailed' });
    // The visible tool-call row summarises the typed answer.
    expect(r.ToolSummaries.some((s: string) => s.includes('Answered – flavour=detailed'))).toBe(
      true,
    );
  });

  it('threads a Declined outcome – a first-class typed outcome, not an error', async () => {
    const r = await runAskProbe('declined');
    expect(r.HaltReason).toBe('completed');
    const outcome = JSON.parse(r.ThreadedOutcome);
    expect(outcome.$type).toBe('Declined');
    expect(outcome.elicitationId).toBe('probe-ask-1');
    expect(outcome.answer).toBeUndefined();
  });

  it('refuses a malformed envelope with the typed decode error and continues the loop', async () => {
    const r = await runAskRefusedProbe();
    expect(r.HaltReason).toBe('completed');
    // The refusal came back as the tool result (so the model can repair)…
    expect(r.RefusalThreaded).toBe(true);
    // …with the codec's real first error (a missing $elicitation version tag
    // surfaces as MISSING_FIELD, or the stray key as UNDECLARED_FIELD).
    expect(['MISSING_FIELD', 'UNDECLARED_FIELD']).toContain(r.RefusalCode);
    // …and no live ask row ever mounted for a refused envelope.
    expect(r.AskRowMounted).toBe(false);
  });
});

describe('the answer builder (committed state → canonical answer object)', () => {
  // A minimal envelope whose contract declares an enum + an int + an optional
  // string – built inline (the same wire shape the codec documents).
  const envelope = JSON.stringify({
    $elicitation: '1',
    contract: {
      fields: [
        {
          name: 'flavour',
          nodeId: 'ask-root',
          required: true,
          space: { $type: 'enum', values: ['compact', 'detailed'] },
          stateKey: 'k-flavour',
        },
        {
          name: 'count',
          nodeId: 'ask-root',
          required: true,
          space: { $type: 'intRange', max: 10, min: 1 },
          stateKey: 'k-count',
        },
        {
          name: 'note',
          nodeId: 'ask-root',
          required: false,
          space: { $type: 'stringLen', max: 40, min: 0 },
          stateKey: 'k-note',
        },
      ],
    },
    id: 'builder-probe',
    tree: { id: 'ask-root', kind: { $type: 'Markdown', text: { $type: 'Literal', text: 'q' } } },
  });

  it('classifies a whole-valued number as an integer (§18.2, by value not spelling)', () => {
    const json = buildAnswerJsonFlat(
      JSON.stringify({ 'k-flavour': 'compact', 'k-count': 3.0 }),
      envelope,
    );
    expect(JSON.parse(json)).toEqual({ count: 3, flavour: 'compact' });
  });

  it('omits empty strings and absent keys (a required omission is then refused by the gate)', () => {
    const json = buildAnswerJsonFlat(
      JSON.stringify({ 'k-flavour': 'detailed', 'k-count': 7, 'k-note': '' }),
      envelope,
    );
    expect(JSON.parse(json)).toEqual({ count: 7, flavour: 'detailed' });
  });

  it('carries an optional string through when non-empty', () => {
    const json = buildAnswerJsonFlat(
      JSON.stringify({ 'k-flavour': 'compact', 'k-count': 1, 'k-note': 'go fast' }),
      envelope,
    );
    expect(JSON.parse(json)).toEqual({ count: 1, flavour: 'compact', note: 'go fast' });
  });
});
