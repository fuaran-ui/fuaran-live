// Phase 713 – the Navigator's structural edits (insert / remove / move /
// reorder), exercised headlessly over the Fable output. The module lives in
// app/navigator/StructuralEdit.fs and compiles to
// app/output/navigator/StructuralEdit.js; this drives it in node via vitest,
// following the Phase 710/711/712 pattern (flat string/array projections across
// the Fable boundary, every fixture fed through the REAL strict decoder via the
// session's own ingest path, so nothing here is a hand-waved shape).
//
// The suite's central claim is the 0.4.0 placement rule: membership and order
// are SEPARATE ops, `InsertChild` / `MoveNode` append, and placing anywhere but
// last is `Batch [InsertChild …, ReorderChildren …]` stating the order by
// naming ids. So the strongest assertion here is a negative one — no integer
// position appears anywhere in any op this module emits, ever — and it is made
// by sweeping the canonical JSON of every op the whole suite produces.
//
// The second claim is that undo covers STRUCTURE for free: the ops go through
// the same recorded channel a property edit uses, so Phase 712's replay undoes
// a subtree removal exactly as it undoes a heading level, byte-for-byte.
//
// Requires `pnpm run fable:app` (the app build) to have produced app/output/.

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Fable-generated JS – no .d.ts; vitest runs it via esbuild (no typecheck).
// @ts-expect-error untyped Fable output
import { empty, ingestResult } from '../app/output/Session.js';
import {
  paletteKinds,
  insertAt,
  removeAt,
  moveTo,
  nudgeAt,
  childIds,
  defaultTargetSpec,
  fallbackIdAfterRemove,
  canDropAt,
  lastOpJson,
  schemaKinds,
  defaultWireFor,
  // @ts-expect-error untyped Fable output
} from '../app/output/navigator/StructuralEdit.js';
import { decodeNode, encodeNode } from '@fuaran-ui/ops';
import {
  undoN,
  redoN,
  canonicalTree,
  originKinds,
  originIds,
  logKinds,
  verifyResult,
  // @ts-expect-error untyped Fable output
} from '../app/output/navigator/OpLog.js';

// ─── fixtures ────────────────────────────────────────────────────────────────

// root ▸ (a, card ▸ (x, y), c) – two levels, so a move has somewhere to go.
const baseTree =
  '{"id":"root","kind":{"$type":"Box","children":[' +
  '{"id":"a","kind":{"$type":"Markdown","text":"A"}},' +
  '{"id":"card","kind":{"$type":"Box","children":[' +
  '{"id":"x","kind":{"$type":"Markdown","text":"X"}},' +
  '{"id":"y","kind":{"$type":"Markdown","text":"Y"}}],' +
  '"layout":{"$type":"Auto"},"role":"Card"}},' +
  '{"id":"c","kind":{"$type":"Markdown","text":"C"}}],' +
  '"layout":{"$type":"Auto"},"role":"Dashboard"}}';

function session(wire: string) {
  const r = ingestResult(empty, wire);
  if (!r.Ok) {
    throw new Error(`fixture did not decode: ${r.Error}\n${wire}`);
  }
  return r.Next;
}

const seeded = () => session(baseTree);

/** Every op emitted anywhere in this file, collected for the sweep at the end. */
const emitted: string[] = [];

/** Run a structural entry point, record whatever op it emitted, hand back the result. */
function act(result: { Ok: boolean; Error: string; Next: unknown }) {
  if (result.Ok) {
    emitted.push(lastOpJson(result.Next));
  }
  return result;
}

const op = (s: unknown) => JSON.parse(lastOpJson(s));

// ─── the palette is derived, not written ─────────────────────────────────────

describe('the insert palette is derived from the schema', () => {
  it('offers a substantial set of kinds under a Box', () => {
    const kinds = paletteKinds(seeded(), 'root', 'last') as string[];
    // The exact membership is a function of the schema, not of this file – so
    // the assertion is a floor, not a list. A palette that collapsed to a
    // handful would mean the synthesiser had stopped understanding the schema.
    expect(kinds.length).toBeGreaterThan(10);
    expect(kinds).toContain('Heading');
    expect(kinds).toContain('Markdown');
    expect(kinds).toContain('Box');
  });

  it('offers nothing under a kind that takes no children', () => {
    // `a` is a Markdown: InsertChild cannot reach into it, so the gate the
    // palette runs refuses every candidate and the offer set is empty. This is
    // the "validator-checked before offer" claim: the palette and the button
    // are the same code path, so they cannot disagree.
    expect(paletteKinds(seeded(), 'a', 'last')).toEqual([]);
  });

  it('names no node kind in its own source', () => {
    // The source lock, mirroring Phase 711's: the module derives its vocabulary
    // and must not carry a hand-maintained one. `Node` / `NodeKind` are the
    // schema's own structural def names, which the walk legitimately follows.
    const source = readFileSync(
      fileURLToPath(new URL('../app/navigator/StructuralEdit.fs', import.meta.url)),
      'utf8',
    );
    for (const kind of ['Heading', 'Markdown', 'Metric', 'DataGrid', 'Badge', 'Chart', 'Tabs']) {
      expect(source).not.toContain(kind);
    }
  });
});

// ─── insertion, and the placement rule ───────────────────────────────────────

describe('insertion states placement by naming ids', () => {
  it('emits a bare InsertChild when the node goes last', () => {
    const r = act(insertAt(seeded(), 'root', 'last', 'Heading'));
    expect(r.Ok).toBe(true);

    const emittedOp = op(r.Next);
    expect(emittedOp.$type).toBe('InsertChild');
    expect(emittedOp.parentId).toBe('root');
    expect((childIds(r.Next, 'root') as string[]).slice(0, 3)).toEqual(['a', 'card', 'c']);
    expect(childIds(r.Next, 'root')).toHaveLength(4);
  });

  it('emits exactly Batch [InsertChild, ReorderChildren] when it goes before a sibling', () => {
    const r = act(insertAt(seeded(), 'root', 'before:card', 'Heading'));
    expect(r.Ok).toBe(true);

    const batch = op(r.Next);
    expect(batch.$type).toBe('Batch');
    expect(batch.ops.map((o: { $type: string }) => o.$type)).toEqual([
      'InsertChild',
      'ReorderChildren',
    ]);

    // The order is stated by naming every sibling id, in the wanted order.
    const inserted = batch.ops[0].child.id as string;
    expect(batch.ops[1].parentId).toBe('root');
    expect(batch.ops[1].newOrder).toEqual(['a', inserted, 'card', 'c']);
    expect(childIds(r.Next, 'root')).toEqual(['a', inserted, 'card', 'c']);
  });

  it('places after a sibling by the same route', () => {
    const r = act(insertAt(seeded(), 'root', 'after:a', 'Markdown'));
    expect(r.Ok).toBe(true);

    const batch = op(r.Next);
    const inserted = batch.ops[0].child.id as string;
    expect(childIds(r.Next, 'root')).toEqual(['a', inserted, 'card', 'c']);
  });

  it('does not emit a reorder that restates the append', () => {
    // "after the last sibling" IS the append, so the second leg would say
    // nothing – and an op that says nothing is noise in a provenance log.
    const r = act(insertAt(seeded(), 'root', 'after:c', 'Markdown'));
    expect(r.Ok).toBe(true);
    expect(op(r.Next).$type).toBe('InsertChild');
  });

  it('mints an id that is fresh against the whole tree, every time', () => {
    let s = seeded();
    const before = new Set(childIds(s, 'root') as string[]);

    const first = act(insertAt(s, 'root', 'last', 'Heading'));
    expect(first.Ok).toBe(true);
    const second = act(insertAt(first.Next, 'root', 'last', 'Heading'));
    expect(second.Ok).toBe(true);

    const after = childIds(second.Next, 'root') as string[];
    const minted = after.filter((id) => !before.has(id));
    expect(minted).toHaveLength(2);
    expect(new Set(minted).size).toBe(2);
    // …and neither collides with anything that was already in the tree.
    for (const id of minted) {
      expect(['root', 'a', 'card', 'x', 'y', 'c']).not.toContain(id);
    }
  });

  it('refuses to insert into a kind that takes no children, changing nothing', () => {
    const s = seeded();
    const before = canonicalTree(s);
    const r = insertAt(s, 'a', 'last', 'Heading');

    expect(r.Ok).toBe(false);
    expect(r.Error).not.toBe('');
    expect(canonicalTree(r.Next)).toBe(before);
    expect(logKinds(r.Next)).toEqual(logKinds(s));
  });
});

// ─── removal, and where the cursor lands ─────────────────────────────────────

describe('removal falls back to a surviving neighbour', () => {
  it('removes the focused node', () => {
    const r = act(removeAt(seeded(), 'card'));
    expect(r.Ok).toBe(true);
    expect(op(r.Next).$type).toBe('RemoveNode');
    expect(childIds(r.Next, 'root')).toEqual(['a', 'c']);
  });

  it('prefers the previous sibling, then the next, then the parent', () => {
    const s = seeded();
    expect(fallbackIdAfterRemove(s, 'card')).toBe('a'); // previous
    expect(fallbackIdAfterRemove(s, 'a')).toBe('card'); // no previous → next
    expect(fallbackIdAfterRemove(s, 'c')).toBe('card'); // previous
    expect(fallbackIdAfterRemove(s, 'root')).toBe(''); // the root has no parent
  });

  it('falls back to the parent when the last child goes', () => {
    let s = seeded();
    s = act(removeAt(s, 'x')).Next;
    expect(fallbackIdAfterRemove(s, 'y')).toBe('card');
  });

  it('refuses to remove the root, changing nothing', () => {
    const s = seeded();
    const before = canonicalTree(s);
    const r = removeAt(s, 'root');

    expect(r.Ok).toBe(false);
    expect(canonicalTree(r.Next)).toBe(before);
  });
});

// ─── moving ──────────────────────────────────────────────────────────────────

describe('a move appends, and states any other placement by id', () => {
  it('emits a bare MoveNode when the node lands last', () => {
    const r = act(moveTo(seeded(), 'a', 'card', 'last'));
    expect(r.Ok).toBe(true);
    expect(op(r.Next).$type).toBe('MoveNode');
    expect(childIds(r.Next, 'card')).toEqual(['x', 'y', 'a']);
    expect(childIds(r.Next, 'root')).toEqual(['card', 'c']);
  });

  it('emits Batch [MoveNode, ReorderChildren] for any other placement', () => {
    const r = act(moveTo(seeded(), 'a', 'card', 'before:y'));
    expect(r.Ok).toBe(true);

    const batch = op(r.Next);
    expect(batch.$type).toBe('Batch');
    expect(batch.ops.map((o: { $type: string }) => o.$type)).toEqual([
      'MoveNode',
      'ReorderChildren',
    ]);
    expect(batch.ops[1].newOrder).toEqual(['x', 'a', 'y']);
    expect(childIds(r.Next, 'card')).toEqual(['x', 'a', 'y']);
  });

  it('re-places a node within its current parent', () => {
    const r = act(moveTo(seeded(), 'c', 'root', 'before:a'));
    expect(r.Ok).toBe(true);
    expect(childIds(r.Next, 'root')).toEqual(['c', 'a', 'card']);
  });

  it('will not drop a node into itself or its own descendant', () => {
    const s = seeded();
    expect(canDropAt(s, 'card', 'card')).toBe(false);
    expect(canDropAt(s, 'card', 'x')).toBe(false); // x takes no children anyway
    expect(canDropAt(s, 'a', 'card')).toBe(true);

    const r = moveTo(s, 'root', 'card', 'last');
    expect(r.Ok).toBe(false);
    expect(canonicalTree(r.Next)).toBe(canonicalTree(s));
  });
});

// ─── sibling reordering ──────────────────────────────────────────────────────

describe('reordering names the full sibling order', () => {
  it('moves the focused node up one place', () => {
    const r = act(nudgeAt(seeded(), 'card', -1));
    expect(r.Ok).toBe(true);

    const emittedOp = op(r.Next);
    expect(emittedOp.$type).toBe('ReorderChildren');
    expect(emittedOp.parentId).toBe('root');
    expect(emittedOp.newOrder).toEqual(['card', 'a', 'c']);
    expect(childIds(r.Next, 'root')).toEqual(['card', 'a', 'c']);
  });

  it('moves the focused node down one place', () => {
    const r = act(nudgeAt(seeded(), 'a', 1));
    expect(r.Ok).toBe(true);
    expect(op(r.Next).newOrder).toEqual(['card', 'a', 'c']);
  });

  it('says so at either end rather than silently doing nothing', () => {
    const s = seeded();
    const up = nudgeAt(s, 'a', -1);
    expect(up.Ok).toBe(false);
    expect(up.Error).toContain('first');

    const down = nudgeAt(s, 'c', 1);
    expect(down.Ok).toBe(false);
    expect(down.Error).toContain('last');

    const root = nudgeAt(s, 'root', -1);
    expect(root.Ok).toBe(false);
  });
});

// ─── the destination a cursor implies ────────────────────────────────────────

describe('the destination a focused node implies', () => {
  it('is inside the node when it takes children', () => {
    expect(defaultTargetSpec(seeded(), 'card')).toBe('card|last');
    expect(defaultTargetSpec(seeded(), 'root')).toBe('root|last');
  });

  it('is beside the node when it does not', () => {
    expect(defaultTargetSpec(seeded(), 'a')).toBe('root|after:a');
    expect(defaultTargetSpec(seeded(), 'x')).toBe('card|after:x');
  });
});

// ─── undo covers structure, because it never knew about props ────────────────

describe('undo restores the prior structure byte-identically', () => {
  const roundTrip = (label: string, edit: (s: unknown) => { Ok: boolean; Next: unknown }) =>
    it(label, () => {
      const s = seeded();
      const before = canonicalTree(s);

      const r = edit(s);
      expect(r.Ok).toBe(true);
      expect(canonicalTree(r.Next)).not.toBe(before);

      expect(canonicalTree(undoN(r.Next, 1))).toBe(before);
      // …and forward again, so the op really is the whole difference.
      expect(canonicalTree(redoN(undoN(r.Next, 1), 1))).toBe(canonicalTree(r.Next));
    });

  roundTrip('after an insert', (s) => act(insertAt(s, 'root', 'before:card', 'Heading')));
  roundTrip('after a removal of a whole subtree', (s) => act(removeAt(s, 'card')));
  roundTrip('after a move', (s) => act(moveTo(s, 'a', 'card', 'before:y')));
  roundTrip('after a reorder', (s) => act(nudgeAt(s, 'card', -1)));

  it('undoes a mixed run of structural edits in one sweep', () => {
    let s = seeded();
    const before = canonicalTree(s);

    s = act(insertAt(s, 'card', 'before:y', 'Heading')).Next;
    s = act(nudgeAt(s, 'a', 1)).Next;
    s = act(moveTo(s, 'c', 'card', 'last')).Next;
    s = act(removeAt(s, 'x')).Next;

    expect(canonicalTree(s)).not.toBe(before);
    expect(canonicalTree(undoN(s, 4))).toBe(before);
  });
});

// ─── provenance ──────────────────────────────────────────────────────────────

describe('structural ops are recorded like every other edit', () => {
  it('attributes them to the navigator as a human actor', () => {
    let s = seeded();
    s = act(insertAt(s, 'root', 'last', 'Heading')).Next;
    s = act(nudgeAt(s, 'a', 1)).Next;

    expect(originKinds(s)).toEqual(['human', 'human']);
    expect(originIds(s)).toEqual(['navigator', 'navigator']);
    expect(logKinds(s)).toEqual(['InsertChild', 'ReorderChildren']);
  });

  it('keeps the session reconstructible from its base plus the ops', () => {
    let s = seeded();
    s = act(insertAt(s, 'card', 'before:x', 'Heading')).Next;
    s = act(removeAt(s, 'y')).Next;

    const report = verifyResult(s);
    expect(report.ReplayOk).toBe(true);
    expect(report.ChainOk).toBe(true);
    expect(report.Steps).toBe(2);
  });
});

// ─── the emitter lock ────────────────────────────────────────────────────────
//
// The synthesiser hand-shapes wire JSON in JS, which makes it an in-page wire
// emitter in this repo's sense, so it ships with its lock (CLAUDE.md
// "Emitter-lock convention"). It is registered in the standing inventory at
// test/emitterLocks.test.ts, which points back here.
//
// The lock is NOT the plain byte-identical round-trip the showcase emitters use,
// and the reason is worth stating because it was measured rather than assumed.
// The synthesiser emits each kind's REQUIRED fields and nothing else — a minimal
// emission in the §16 lenient-accept family — and the decoder normalises six of
// them UPWARD: it materialises defaulted optionals the schema does not mark
// required (`Chart.stacked`, `Tabs.activeIndex`, `Stepper.onSelect`) and the
// typed empty payload of a slot-typed `Static` binding (`Map`/`Select`/
// `Sparkline` sources). Asserting byte-identity would therefore assert a bar the
// format does not hold this input to. The other 34 kinds ARE byte-identical.
//
// So the lock makes the two claims that are genuinely load-bearing:
//
//   1. every synthesised default decodes through the real strict decoder, and
//      normalisation reaches a FIXED POINT in one pass (a shape that normalised
//      differently on each pass would be unstable on the wire);
//   2. normalisation is ADDITIVE ONLY — every key and value the synthesiser
//      emitted survives into the canonical form unchanged. That is the assertion
//      that catches the drift class the convention exists for: a union branch
//      spelled non-canonically (a `{"$type":"Literal"}` envelope where canon is
//      a bare string) decodes fine but is REPLACED on re-encode, so the emitted
//      value would not survive and this lock goes red.

/** Every key/value in `emitted` appears, deeply equal, in `canonical`. */
function survives(emitted: unknown, canonical: unknown, path: string): void {
  if (Array.isArray(emitted)) {
    expect(Array.isArray(canonical), path).toBe(true);
    expect((canonical as unknown[]).length, `${path}.length`).toBe(emitted.length);
    emitted.forEach((v, i) => survives(v, (canonical as unknown[])[i], `${path}[${i}]`));
    return;
  }
  if (emitted !== null && typeof emitted === 'object') {
    expect(canonical !== null && typeof canonical === 'object', path).toBe(true);
    for (const [key, v] of Object.entries(emitted as Record<string, unknown>)) {
      expect(canonical as Record<string, unknown>, `${path}.${key} survives`).toHaveProperty(key);
      survives(v, (canonical as Record<string, unknown>)[key], `${path}.${key}`);
    }
    return;
  }
  expect(canonical, path).toBe(emitted);
}

// The set the SYNTHESISER can shape but no decoder accepts, so `defaultNodeFor`
// drops it and the palette never offers it. Pinned by name rather than skipped
// silently, because the drop is the guard working and a NEW member is the guard
// hiding something — see the second lock below for why the two are separated.
//
// Every member fails the same way: the schema's `Binding` `$def` is UNPARAMETERISED
// (`"value": true` — any JSON), so a schema-driven walk cannot know that this
// slot's `Static` needs a string, a number or an integer, and emits the valueless
// `{"$type":"Static"}`. That form is legal — the corpus carries it in `controls-*`
// / `form-segmented` / `multiselect-1` — but only in an auto-bound CONTROL slot,
// where absence IS the binding. In a typed scalar slot it is WRONG_TYPE.
//
// It is an expressiveness gap in the AI-tools JSON schema, not a bug in this
// walker: no amount of care here recovers a type the schema does not carry, and
// guessing one from the property name is exactly the guess the synthesiser's
// docstring refuses to make. Closing it means parameterising `Binding` in the
// schema, which is a language-tier change.
const UNDECODABLE_SYNTHESIS = new Set([
  'Disclosure',
  'Image',
  'LabelValueRow',
  'Link',
  'Metric',
  'Modal',
  'Progress',
  'Stepper',
  'Toast',
]);

describe('the synthesised defaults are strictly decodable and normalisation-stable', () => {
  // The subject is what the palette OFFERS, not everything the walker can shape.
  // Those differ, and the difference is load-bearing: `defaultNodeFor` reads its
  // synthesis back through the real strict decoder and returns None on refusal,
  // so an undecodable shape never reaches a user's tree.
  //
  // This lock swept `schemaKinds()` until 2026-08-30 and passed — on a premise
  // that was already false. The F# reference decoder rejected nine of those
  // kinds the whole time; `@fuaran-ui/ops` 0.9.0 happened to accept them, so the
  // lock was measuring the laxer of the two hosts and reporting agreement that
  // did not exist. Raising the pins to 0.19.0 — which moved TOWARD the F#
  // reference — is what surfaced it. Widening a lock's subject past what the
  // module guarantees does not make it stronger; it makes it wrong in a
  // direction nobody reads.
  it('decodes, reaches a fixed point, and loses nothing it emitted', () => {
    const s = seeded();
    const kinds = paletteKinds(s, 'root', 'last') as string[];
    expect(kinds.length).toBeGreaterThan(20);

    let locked = 0;
    for (const kind of kinds) {
      const wire = defaultWireFor(s, kind) as string;
      if (wire === '') {
        continue; // not synthesisable, and so never offered — nothing to lock.
      }

      // Narrow through the result union rather than asserting on `.ok` — an
      // `expect` does not tell the compiler anything, so `.value` below is only
      // reachable behind a real guard. The failure carries the same detail.
      const first = decodeNode(wire);
      if (!first.ok) {
        throw new Error(`${kind} does not decode: ${first.error.code}`);
      }

      const canonical = encodeNode(first.value);
      const second = decodeNode(canonical);
      if (!second.ok) {
        throw new Error(`${kind} does not re-decode: ${second.error.code}`);
      }
      expect(encodeNode(second.value), `${kind} normalisation is a fixed point`).toBe(canonical);

      survives(JSON.parse(wire), JSON.parse(canonical), kind);
      locked++;
    }

    // Guard the guard: an empty sweep would pass while locking nothing.
    expect(locked).toBeGreaterThan(20);
  });

  // Narrowing the sweep above to the offered kinds would, on its own, let a kind
  // fall out of the palette silently — the exact failure mode that let the old
  // premise stand for as long as it did. So the dropped set is itself pinned:
  // it may only change deliberately, in both directions.
  it('the kinds the walker shapes but no decoder accepts are exactly the pinned set', () => {
    const s = seeded();
    const offered = new Set(paletteKinds(s, 'root', 'last') as string[]);
    const dropped = (schemaKinds() as string[])
      .filter((k) => (defaultWireFor(s, k) as string) !== '')
      .filter((k) => !offered.has(k))
      .sort();

    expect(
      dropped,
      'a kind entered or left the undecodable set — if it LEFT, delete it from ' +
        'UNDECODABLE_SYNTHESIS (the schema or the walker improved); if it ENTERED, ' +
        'the palette just lost a kind and the cause is upstream, not here',
    ).toEqual([...UNDECODABLE_SYNTHESIS].sort());
  });
});

// ─── the negative claim, swept over everything above ─────────────────────────

describe('no emitted op carries an integer position', () => {
  it('never writes position / newPosition, and never an ordinal in place of an id', () => {
    // Guard the guard: if the suite above stopped emitting, this would pass
    // vacuously and prove nothing.
    expect(emitted.length).toBeGreaterThan(10);

    const forbidden = (value: unknown, path: string): void => {
      if (Array.isArray(value)) {
        value.forEach((v, i) => forbidden(v, `${path}[${i}]`));
        return;
      }
      if (value !== null && typeof value === 'object') {
        for (const [key, v] of Object.entries(value as Record<string, unknown>)) {
          expect(key, `${path}.${key}`).not.toBe('position');
          expect(key, `${path}.${key}`).not.toBe('newPosition');
          forbidden(v, `${path}.${key}`);
        }
      }
    };

    for (const json of emitted) {
      expect(json).not.toBe('');
      forbidden(JSON.parse(json), 'op');
    }

    // Every `newOrder` entry is an id string, never a number – the whole point
    // of the rule is that order is stated in the tree's own identity model.
    for (const json of emitted) {
      for (const match of json.matchAll(/"newOrder":\[(.*?)\]/g)) {
        expect(match[1]).not.toMatch(/(^|,)\s*-?\d/);
      }
    }
  });
});
