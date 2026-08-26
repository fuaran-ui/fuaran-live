// Phase 761 — run mode: a generated emission stops being a picture and becomes
// an app.
//
// The claim this suite makes checkable is the one the pane makes on screen, and
// it has three parts:
//
//   1. A wire-decoded tree RUNS. A click on a button whose `onClick` is a
//      bounded `SetState` writes the store, and a node BOUND to that key
//      re-resolves — with no hand-authored update function and no message type
//      anywhere in the emission.
//   2. The live `$state` map is what the panel reads, so a step that writes a
//      key is a step the panel shows.
//   3. A named-but-unregistered effect produces a RECORDED denial, not silence.
//      This is the part worth a test rather than a screenshot: "nothing
//      happened" and "this host refused that" are different facts, and only the
//      second one is a demonstration of default-deny.
//
// The wire below is hand-authored, so it is an in-page wire emitter under the
// repo's emitter-lock convention. It is certified in the strongest available
// form: every tree is put through the REAL strict decoder before it is run, and
// a decode failure fails the suite — so a shape that drifted from the format
// cannot pass as a passing test about something else.
//
// Requires `pnpm run fable:app` to have produced `app/output/`.

import { describe, expect, it } from 'vitest';

// @ts-expect-error untyped Fable output
import { start, step, consoleDenialSink } from '../app/output/RunMode.js';
import { decodeNode } from './tierOutput.js';

// A counter, expressed the way an emitter would express one: a button whose
// click is a bounded `SetState`, and a Markdown whose text is BOUND to that
// key. Plus one button that reaches for a capability this host does not offer.
const wire = JSON.stringify({
  id: 'root',
  kind: {
    $type: 'Box',
    children: [
      {
        id: 'bump',
        kind: {
          $type: 'Button',
          label: 'Click me',
          onClick: { $type: 'SetState', key: 'clicks', value: 'clicked!' },
          variant: 'Primary',
        },
      },
      {
        id: 'readout',
        kind: {
          $type: 'Markdown',
          text: {
            $type: 'Bound',
            binding: { $type: 'State', defaultValue: 'not clicked yet', key: 'clicks' },
          },
        },
      },
      {
        id: 'leave',
        kind: {
          $type: 'Button',
          label: 'Take me somewhere',
          onClick: { $type: 'Navigate', route: 'https://example.com/elsewhere' },
          variant: 'Secondary',
        },
      },
    ],
    layout: { $type: 'Flex', direction: 'Vertical', wrap: false },
    role: 'Dashboard',
  },
});

/**
 * Decode through the real strict decoder; a decode failure is a test failure.
 *
 * `decodeNode` yields a `WireTree` — the marker saying "this tree's closures are
 * inert sentinels" — and the session holds the node behind it, which is what
 * `RunMode.start` takes. Unwrapping the single-case union is what
 * `WireTree.reify` does in F#; from JS it is the inner field.
 */
const decoded = () => {
  const r = decodeNode(wire);
  expect(r.tag, 'the hand-authored wire must decode through the real strict decoder').toBe(0);
  return r.fields[0].fields[0];
};

/** The `$state` map the panel renders, as plain entries. */
const stateEntries = (s: { Program: { Store: { State: Iterable<[string, unknown]> } } }) =>
  Array.from(s.Program.Store.State);

/** Newest step first, matching what the journal renders. */
const latest = (s: { Steps: Iterable<unknown> }) =>
  Array.from(s.Steps)[0] as {
    Effects: Iterable<string>;
    Denials: Iterable<string>;
    Rejected: unknown;
    Diagnostics: Iterable<string>;
    OpCount: number;
  };

describe('Phase 761 – the emission runs, client-only', () => {
  it('starts with an empty store and the base tree resolved', () => {
    const s = start(consoleDenialSink, decoded());
    expect(stateEntries(s)).toEqual([]);
    expect(Array.from(s.Steps)).toEqual([]);
  });

  it('a bounded SetState click writes the store — no update function, no Msg', () => {
    const s = step(start(consoleDenialSink, decoded()), 'bump', 'click');
    expect(stateEntries(s)).toEqual([['clicks', 'clicked!']]);
    // Fable compiles `None` to `undefined`.
    expect(latest(s).Rejected).toBeUndefined();
  });

  it('the bound node re-resolves against the new store', () => {
    const before = start(consoleDenialSink, decoded());
    const after = step(before, 'bump', 'click');
    // The resolved projection is what the pane renders; the base tree is fixed.
    expect(JSON.stringify(before.Program.Resolved)).toContain('not clicked yet');
    expect(JSON.stringify(after.Program.Resolved)).toContain('clicked!');
  });

  it('journals the step, with the ops a server-run step would have journalled', () => {
    const s = step(start(consoleDenialSink, decoded()), 'bump', 'click');
    expect(latest(s).OpCount).toBeGreaterThan(0);
  });
});

describe('Phase 761 – default-deny is demonstrated, not merely claimed', () => {
  it('an effect this host does not register is REFUSED and RECORDED', () => {
    const s = step(start(consoleDenialSink, decoded()), 'leave', 'click');
    const record = latest(s);

    // The fold REACHED the effect — the emission asked, legitimately.
    expect(Array.from(record.Effects)).toContain('Navigate');
    // And this host declined it, by name, in a record the journal renders.
    const denials = Array.from(record.Denials);
    expect(denials).toHaveLength(1);
    expect(denials[0]).toContain('Navigate');
    expect(denials[0]).toContain('no performer is registered');
  });

  it('a refused effect leaves the store untouched — the refusal is not a mutation', () => {
    const s = step(start(consoleDenialSink, decoded()), 'leave', 'click');
    expect(stateEntries(s)).toEqual([]);
  });

  it('the denial record carries the capability name and never the destination', () => {
    const s = step(start(consoleDenialSink, decoded()), 'leave', 'click');
    const denial = Array.from(latest(s).Denials)[0];
    // A refusal record outlives the session; the route came off the wire.
    expect(denial).not.toContain('example.com');
    expect(denial).not.toContain('/elsewhere');
  });
});
